#nullable enable

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using Godot;

namespace ReForgeFramework.Networking;

/// <summary>
/// ENet 传输实现。
/// 采用与 STS2 官方一致的控制包协议：握手 / 断开 / 应用消息。
/// </summary>
public sealed class ReForgeENetTransport : IReForgeNetTransport
{
	private enum ReForgeENetPacketType : byte
	{
		HandshakeRequest = 0,
		HandshakeResponse = 1,
		Disconnection = 2,
		ApplicationMessage = 3,
	}

	private enum ReForgeENetHandshakeStatus : byte
	{
		Success = 0,
		IdCollision = 1,
		Rejected = 2,
	}

	private readonly struct ReForgeENetServiceData
	{
		public ReForgeENetServiceData(
			ENetConnection.EventType type,
			ENetPacketPeer peer,
			int channel,
			ReForgeNetTransferMode mode,
			byte[] packetData,
			Error error)
		{
			Type = type;
			Peer = peer;
			Channel = channel;
			Mode = mode;
			PacketData = packetData;
			Error = error;
		}

		public ENetConnection.EventType Type { get; }

		public ENetPacketPeer Peer { get; }

		public int Channel { get; }

		public ReForgeNetTransferMode Mode { get; }

		public byte[] PacketData { get; }

		public Error Error { get; }
	}

	public enum ReForgeENetRole
	{
		Host,
		Client,
	}

	public sealed record ReForgeENetConfig(
		ReForgeENetRole Role,
		string Host,
		ushort Port,
		int MaxClients,
		ulong LocalPeerId,
		bool EnableAutoReconnect,
		int MaxReconnectAttempts,
		int ReconnectInitialDelayMs,
		int ReconnectMaxDelayMs
	);

	private const int HandshakeTimeoutMs = 10000;
	private const ulong HostNetId = 1;

	private readonly Dictionary<ulong, ENetPacketPeer> _hostPeerByNetId = new();
	private readonly Dictionary<ulong, ulong> _hostNetIdByPeerObjectId = new();
	private bool _hostHasDisconnectRecord;
	private ulong _hostLastDisconnectedPeerId;
	private string _hostLastDisconnectReason = string.Empty;
	private long _hostLastDisconnectAtMs;

	private ENetConnection? _connection;
	private ENetPacketPeer? _clientHostPeer;
	private bool _isConnected;
	private bool _clientAwaitingHandshake;
	private long _clientHandshakeSentAtMs;
	private long _clientConnectStartedAtMs;
	private bool _clientReconnectScheduled;
	private int _clientReconnectAttempt;
	private long _clientNextReconnectAtMs;
	private string _clientLastDisconnectReason = string.Empty;
	private bool _clientReconnectSuppressed;
	private bool _disposed;

	private ReForgeENetTransport(ReForgeENetConfig config)
	{
		Config = config;
		if (config.LocalPeerId == 0)
		{
			throw new ArgumentOutOfRangeException(nameof(config.LocalPeerId), "LocalPeerId cannot be 0.");
		}

		if (config.Role == ReForgeENetRole.Client)
		{
			if (config.ReconnectInitialDelayMs < 0)
			{
				throw new ArgumentOutOfRangeException(nameof(config.ReconnectInitialDelayMs));
			}

			if (config.ReconnectMaxDelayMs < config.ReconnectInitialDelayMs)
			{
				throw new ArgumentOutOfRangeException(nameof(config.ReconnectMaxDelayMs));
			}

			if (config.MaxReconnectAttempts < 0)
			{
				throw new ArgumentOutOfRangeException(nameof(config.MaxReconnectAttempts));
			}
		}

		if (config.Role == ReForgeENetRole.Host)
		{
			StartHost();
		}
		else
		{
			StartClient(isReconnect: false);
		}
	}

	public ReForgeENetConfig Config { get; }

	public ulong LocalPeerId => Config.LocalPeerId;

	public bool IsConnected => !_disposed && _isConnected;

	public bool IsReconnecting => !_disposed && Config.Role == ReForgeENetRole.Client && _clientReconnectScheduled;

	public int ReconnectAttempt => _clientReconnectAttempt;

	public long NextReconnectAtMs => _clientNextReconnectAtMs;

	public string LastDisconnectReason => _clientLastDisconnectReason;

	public ulong[] GetHostConnectedPeerIdsSnapshot()
	{
		if (Config.Role != ReForgeENetRole.Host)
		{
			return Array.Empty<ulong>();
		}

		ulong[] ids = new ulong[_hostPeerByNetId.Count];
		int index = 0;
		foreach (KeyValuePair<ulong, ENetPacketPeer> pair in _hostPeerByNetId)
		{
			ids[index++] = pair.Key;
		}

		Array.Sort(ids);
		return ids;
	}

	public bool TryGetHostLastDisconnect(out ulong peerId, out string reason, out long atMs)
	{
		if (Config.Role != ReForgeENetRole.Host || !_hostHasDisconnectRecord)
		{
			peerId = 0;
			reason = string.Empty;
			atMs = 0;
			return false;
		}

		peerId = _hostLastDisconnectedPeerId;
		reason = _hostLastDisconnectReason;
		atMs = _hostLastDisconnectAtMs;
		return true;
	}

	public event Action<ulong, byte[], ReForgeNetTransferMode, int>? PacketReceived;

	public static ReForgeENetTransport CreateHost(ushort port, int maxClients, ulong localPeerId = 1)
	{
		if (maxClients <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(maxClients));
		}

		return new ReForgeENetTransport(new ReForgeENetConfig(
			ReForgeENetRole.Host,
			Host: "0.0.0.0",
			Port: port,
			MaxClients: maxClients,
			LocalPeerId: localPeerId,
			EnableAutoReconnect: false,
			MaxReconnectAttempts: 0,
			ReconnectInitialDelayMs: 0,
			ReconnectMaxDelayMs: 0
		));
	}

	public static ReForgeENetTransport CreateClient(
		string host,
		ushort port,
		ulong localPeerId,
		bool autoReconnect = true,
		int maxReconnectAttempts = 0,
		int reconnectInitialDelayMs = 500,
		int reconnectMaxDelayMs = 8000)
	{
		ArgumentNullException.ThrowIfNull(host);

		return new ReForgeENetTransport(new ReForgeENetConfig(
			ReForgeENetRole.Client,
			Host: host,
			Port: port,
			MaxClients: 1,
			LocalPeerId: localPeerId,
			EnableAutoReconnect: autoReconnect,
			MaxReconnectAttempts: maxReconnectAttempts,
			ReconnectInitialDelayMs: reconnectInitialDelayMs,
			ReconnectMaxDelayMs: reconnectMaxDelayMs
		));
	}

	public void SendToPeer(ulong peerId, byte[] packetBytes, int length, ReForgeNetTransferMode mode, int channel)
	{
		ThrowIfDisposed();
		if (!_isConnected)
		{
			return;
		}

		if (Config.Role == ReForgeENetRole.Host)
		{
			if (!_hostPeerByNetId.TryGetValue(peerId, out ENetPacketPeer? peer))
			{
				GD.PrintErr($"[ReForge.Network] ENet host cannot find peer id={peerId}.");
				return;
			}

			SendAppPacket(peer, packetBytes, length, mode, channel);
			return;
		}

		if (peerId != HostNetId)
		{
			GD.PrintErr($"[ReForge.Network] ENet client can only send to host id={HostNetId}, requested={peerId}.");
			return;
		}

		if (_clientHostPeer == null)
		{
			GD.PrintErr("[ReForge.Network] ENet client host peer is null.");
			return;
		}

		SendAppPacket(_clientHostPeer, packetBytes, length, mode, channel);
	}

	public void SendToAll(byte[] packetBytes, int length, ReForgeNetTransferMode mode, int channel, ulong? excludePeerId = null)
	{
		ThrowIfDisposed();
		if (!_isConnected)
		{
			return;
		}

		if (Config.Role == ReForgeENetRole.Host)
		{
			foreach (KeyValuePair<ulong, ENetPacketPeer> pair in _hostPeerByNetId)
			{
				if (excludePeerId.HasValue && pair.Key == excludePeerId.Value)
				{
					continue;
				}

				SendAppPacket(pair.Value, packetBytes, length, mode, channel);
			}

			return;
		}

		if (excludePeerId.HasValue && excludePeerId.Value == HostNetId)
		{
			return;
		}

		if (_clientHostPeer != null)
		{
			SendAppPacket(_clientHostPeer, packetBytes, length, mode, channel);
		}
	}

	public void Update()
	{
		ThrowIfDisposed();
		if (_connection == null)
		{
			if (Config.Role == ReForgeENetRole.Client)
			{
				TryRunScheduledReconnect(GetTicksMs());
			}

			return;
		}

		ENetConnection activeConnection = _connection;
		while (TryService(activeConnection, out ReForgeENetServiceData serviceData))
		{
			switch (serviceData.Type)
			{
				case ENetConnection.EventType.Connect:
					OnPeerConnected(serviceData.Peer);
					break;
				case ENetConnection.EventType.Disconnect:
					OnPeerDisconnected(serviceData.Peer, "enet disconnect event");
					break;
				case ENetConnection.EventType.Receive:
					OnPacketReceived(serviceData);
					break;
				default:
					break;
			}

			if (_connection == null || !ReferenceEquals(activeConnection, _connection))
			{
				break;
			}
		}

		if (Config.Role == ReForgeENetRole.Client)
		{
			long now = GetTicksMs();
			if (_clientAwaitingHandshake && now - _clientHandshakeSentAtMs > HandshakeTimeoutMs)
			{
				OnClientConnectionLost("handshake timeout");
				return;
			}

			if (!_isConnected && !_clientAwaitingHandshake && _clientHostPeer != null && now - _clientConnectStartedAtMs > HandshakeTimeoutMs)
			{
				OnClientConnectionLost("connect timeout");
				return;
			}

			TryRunScheduledReconnect(now);
		}
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		if (Config.Role == ReForgeENetRole.Host)
		{
			foreach (KeyValuePair<ulong, ENetPacketPeer> pair in _hostPeerByNetId)
			{
				try
				{
					pair.Value.PeerDisconnectNow();
				}
				catch
				{
					// 忽略销毁阶段异常
				}
			}

			_hostPeerByNetId.Clear();
			_hostNetIdByPeerObjectId.Clear();
		}
		else
		{
			_clientReconnectSuppressed = true;
			_clientReconnectScheduled = false;
			DisconnectClientNow();
		}

		try
		{
			_connection?.Flush();
			_connection?.Destroy();
		}
		catch
		{
			// 忽略销毁阶段异常
		}

		_connection = null;
		_clientHostPeer = null;
		_isConnected = false;
		_disposed = true;
	}

	private void StartHost()
	{
		_connection = new ENetConnection();
		Error error = _connection.CreateHostBound(Config.Host, Config.Port, Config.MaxClients);
		if (error != Error.Ok)
		{
			throw new InvalidOperationException($"Failed to start ENet host at {Config.Host}:{Config.Port}. Error={error}");
		}

		_isConnected = true;
		GD.Print($"[ReForge.Network] ENet host started at {Config.Host}:{Config.Port}, maxClients={Config.MaxClients}.");
	}

	private void StartClient(bool isReconnect)
	{
		DisconnectClientNow();

		_connection = new ENetConnection();
		_connection.CreateHost();
		_clientHostPeer = _connection.ConnectToHost(Config.Host, Config.Port);
		_clientConnectStartedAtMs = GetTicksMs();
		_isConnected = false;
		_clientAwaitingHandshake = false;

		if (!isReconnect)
		{
			_clientReconnectAttempt = 0;
			_clientReconnectScheduled = false;
		}

		string modeText = isReconnect ? "reconnecting" : "connecting";
		GD.Print($"[ReForge.Network] ENet client {modeText} to {Config.Host}:{Config.Port}. localPeerId={Config.LocalPeerId}, attempt={_clientReconnectAttempt}.");
	}

	private void OnPeerConnected(ENetPacketPeer peer)
	{
		if (Config.Role == ReForgeENetRole.Client)
		{
			if (_clientHostPeer != null && peer != _clientHostPeer)
			{
				return;
			}

			SendHandshakeRequest(peer);
			return;
		}

		// Host 端等待客户端发送握手请求后再建立映射。
	}

	private void OnPeerDisconnected(ENetPacketPeer peer, string reason)
	{
		if (Config.Role == ReForgeENetRole.Host)
		{
			ulong peerObjectId = peer.GetInstanceId();
			if (_hostNetIdByPeerObjectId.TryGetValue(peerObjectId, out ulong netId))
			{
				_hostNetIdByPeerObjectId.Remove(peerObjectId);
				_hostPeerByNetId.Remove(netId);
				RecordHostDisconnect(netId, reason);
			}
			else
			{
				RecordHostDisconnect(0, reason);
			}
			return;
		}

		OnClientConnectionLost(reason);
	}

	private void OnPacketReceived(ReForgeENetServiceData serviceData)
	{
		byte[] packet = serviceData.PacketData;
		if (packet.Length == 0)
		{
			return;
		}

		ReForgeENetPacketType packetType = (ReForgeENetPacketType)packet[0];
		switch (packetType)
		{
			case ReForgeENetPacketType.HandshakeRequest:
				if (Config.Role == ReForgeENetRole.Host)
				{
					HandleHandshakeRequest(serviceData.Peer, packet);
				}
				break;
			case ReForgeENetPacketType.HandshakeResponse:
				if (Config.Role == ReForgeENetRole.Client)
				{
					HandleHandshakeResponse(packet);
				}
				break;
			case ReForgeENetPacketType.Disconnection:
				if (Config.Role == ReForgeENetRole.Client)
				{
					OnClientConnectionLost("received disconnection packet");
				}
				else
				{
					string reason = packet.Length > 1
						? $"received disconnection packet reasonCode={packet[1]}"
						: "received disconnection packet";
					OnPeerDisconnected(serviceData.Peer, reason);
				}
				break;
			case ReForgeENetPacketType.ApplicationMessage:
				HandleApplicationMessage(serviceData, packet);
				break;
			default:
				GD.PrintErr($"[ReForge.Network] ENet received unknown packet type: {(byte)packetType}.");
				break;
		}
	}

	private void HandleHandshakeRequest(ENetPacketPeer peer, byte[] packet)
	{
		if (packet.Length < 9)
		{
			GD.PrintErr("[ReForge.Network] Invalid ENet handshake request packet length.");
			return;
		}

		ulong requestedNetId = BinaryPrimitives.ReadUInt64BigEndian(packet.AsSpan(1, 8));
		if (requestedNetId == 0)
		{
			SendHandshakeResponse(peer, ReForgeENetHandshakeStatus.Rejected, requestedNetId);
			peer.PeerDisconnectLater();
			return;
		}

		if (_hostPeerByNetId.ContainsKey(requestedNetId))
		{
			SendHandshakeResponse(peer, ReForgeENetHandshakeStatus.IdCollision, requestedNetId);
			peer.PeerDisconnectLater();
			return;
		}

		ulong peerObjectId = peer.GetInstanceId();
		_hostPeerByNetId[requestedNetId] = peer;
		_hostNetIdByPeerObjectId[peerObjectId] = requestedNetId;

		SendHandshakeResponse(peer, ReForgeENetHandshakeStatus.Success, requestedNetId);
	}

	private void HandleHandshakeResponse(byte[] packet)
	{
		if (packet.Length < 10)
		{
			GD.PrintErr("[ReForge.Network] Invalid ENet handshake response packet length.");
			OnClientConnectionLost("invalid handshake response packet");
			return;
		}

		ReForgeENetHandshakeStatus status = (ReForgeENetHandshakeStatus)packet[1];
		ulong assignedNetId = BinaryPrimitives.ReadUInt64BigEndian(packet.AsSpan(2, 8));
		if (status != ReForgeENetHandshakeStatus.Success)
		{
			GD.PrintErr($"[ReForge.Network] ENet handshake rejected. status={status}, localPeerId={Config.LocalPeerId}.");
			OnClientConnectionLost($"handshake rejected ({status})");
			return;
		}

		if (assignedNetId != Config.LocalPeerId)
		{
			GD.PrintErr($"[ReForge.Network] ENet handshake netId mismatch. local={Config.LocalPeerId}, assigned={assignedNetId}.");
			OnClientConnectionLost("handshake netId mismatch");
			return;
		}

		_isConnected = true;
		_clientAwaitingHandshake = false;
		_clientReconnectScheduled = false;
		_clientReconnectAttempt = 0;
		_clientLastDisconnectReason = string.Empty;
	}

	private void HandleApplicationMessage(ReForgeENetServiceData serviceData, byte[] packet)
	{
		if (packet.Length <= 1)
		{
			return;
		}

		byte[] payload = new byte[packet.Length - 1];
		Array.Copy(packet, 1, payload, 0, payload.Length);

		ulong senderId;
		if (Config.Role == ReForgeENetRole.Host)
		{
			ulong peerObjectId = serviceData.Peer.GetInstanceId();
			if (!_hostNetIdByPeerObjectId.TryGetValue(peerObjectId, out senderId))
			{
				GD.PrintErr("[ReForge.Network] Received application packet before handshake completion.");
				return;
			}
		}
		else
		{
			if (!_isConnected)
			{
				return;
			}

			senderId = HostNetId;
		}

		PacketReceived?.Invoke(senderId, payload, serviceData.Mode, serviceData.Channel);
	}

	private void SendHandshakeRequest(ENetPacketPeer peer)
	{
		byte[] packet = new byte[9];
		packet[0] = (byte)ReForgeENetPacketType.HandshakeRequest;
		BinaryPrimitives.WriteUInt64BigEndian(packet.AsSpan(1, 8), Config.LocalPeerId);
		peer.Send(0, packet, FlagsFromMode(ReForgeNetTransferMode.Reliable));
		_clientAwaitingHandshake = true;
		_clientHandshakeSentAtMs = GetTicksMs();
	}

	private void SendHandshakeResponse(ENetPacketPeer peer, ReForgeENetHandshakeStatus status, ulong netId)
	{
		byte[] packet = new byte[10];
		packet[0] = (byte)ReForgeENetPacketType.HandshakeResponse;
		packet[1] = (byte)status;
		BinaryPrimitives.WriteUInt64BigEndian(packet.AsSpan(2, 8), netId);
		peer.Send(0, packet, FlagsFromMode(ReForgeNetTransferMode.Reliable));
	}

	private void SendAppPacket(ENetPacketPeer peer, byte[] payload, int length, ReForgeNetTransferMode mode, int channel)
	{
		if (length < 0 || length > payload.Length)
		{
			throw new ArgumentOutOfRangeException(nameof(length));
		}

		byte[] packet = new byte[length + 1];
		packet[0] = (byte)ReForgeENetPacketType.ApplicationMessage;
		Array.Copy(payload, 0, packet, 1, length);
		peer.Send(channel, packet, FlagsFromMode(mode));
	}

	private void OnClientConnectionLost(string reason)
	{
		if (Config.Role != ReForgeENetRole.Client)
		{
			return;
		}

		_clientLastDisconnectReason = reason;
		_isConnected = false;
		_clientAwaitingHandshake = false;
		DisconnectClientNow();

		if (_clientReconnectSuppressed || !Config.EnableAutoReconnect)
		{
			return;
		}

		ScheduleClientReconnect(GetTicksMs(), reason);
	}

	private void ScheduleClientReconnect(long nowMs, string reason)
	{
		if (Config.MaxReconnectAttempts > 0 && _clientReconnectAttempt >= Config.MaxReconnectAttempts)
		{
			_clientReconnectScheduled = false;
			GD.PrintErr($"[ReForge.Network] ENet reconnect stopped after {Config.MaxReconnectAttempts} attempts. lastReason='{reason}'.");
			return;
		}

		int baseDelay = Math.Max(1, Config.ReconnectInitialDelayMs);
		int maxDelay = Math.Max(baseDelay, Config.ReconnectMaxDelayMs);
		int exponent = Math.Clamp(_clientReconnectAttempt, 0, 10);
		long delay = Math.Min((long)baseDelay * (1L << exponent), maxDelay);

		_clientReconnectAttempt++;
		_clientNextReconnectAtMs = nowMs + delay;
		_clientReconnectScheduled = true;

		GD.Print($"[ReForge.Network] ENet reconnect scheduled. attempt={_clientReconnectAttempt}, delayMs={delay}, reason='{reason}'.");
	}

	private void TryRunScheduledReconnect(long nowMs)
	{
		if (!_clientReconnectScheduled || _clientReconnectSuppressed)
		{
			return;
		}

		if (nowMs < _clientNextReconnectAtMs)
		{
			return;
		}

		_clientReconnectScheduled = false;
		StartClient(isReconnect: true);
	}

	private void RecordHostDisconnect(ulong peerId, string reason)
	{
		_hostHasDisconnectRecord = true;
		_hostLastDisconnectedPeerId = peerId;
		_hostLastDisconnectReason = reason;
		_hostLastDisconnectAtMs = GetTicksMs();
	}

	private void DisconnectClientNow()
	{
		try
		{
			_clientHostPeer?.PeerDisconnectNow();
		}
		catch
		{
			// 忽略销毁阶段异常
		}

		try
		{
			_connection?.Flush();
			_connection?.Destroy();
		}
		catch
		{
			// 忽略销毁阶段异常
		}

		_connection = null;
		_clientHostPeer = null;
		_isConnected = false;
		_clientAwaitingHandshake = false;
	}

	private static bool TryService(ENetConnection connection, out ReForgeENetServiceData output)
	{
		output = default;
		Godot.Collections.Array raw = connection.Service();
		if (raw == null || raw.Count == 0)
		{
			return false;
		}

		ENetConnection.EventType type = raw[0].As<ENetConnection.EventType>();
		if (type == ENetConnection.EventType.None)
		{
			return false;
		}

		ENetPacketPeer peer = raw[1].As<ENetPacketPeer>();
		int channel = 0;
		ReForgeNetTransferMode mode = ReForgeNetTransferMode.None;
		byte[] packetData = Array.Empty<byte>();
		Error error = Error.Ok;

		if (type == ENetConnection.EventType.Receive)
		{
			channel = raw.Count > 3 ? raw[3].As<int>() : 0;
			packetData = peer.GetPacket();
			error = peer.GetPacketError();
			mode = ModeFromChannel(channel);
		}

		output = new ReForgeENetServiceData(type, peer, channel, mode, packetData, error);
		return true;
	}

	private static ReForgeNetTransferMode ModeFromChannel(int channel)
	{
		return channel switch
		{
			0 => ReForgeNetTransferMode.Reliable,
			_ => ReForgeNetTransferMode.Unreliable,
		};
	}

	private static int FlagsFromMode(ReForgeNetTransferMode mode)
	{
		return mode switch
		{
			ReForgeNetTransferMode.Reliable => 1,
			ReForgeNetTransferMode.Unreliable => 8,
			_ => 1,
		};
	}

	private static long GetTicksMs()
	{
		return (long)Time.GetTicksMsec();
	}

	private void ThrowIfDisposed()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
	}
}
