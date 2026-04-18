#nullable enable

using System;
using Godot;

namespace ReForgeFramework.Networking;

internal sealed class ReForgeNetService : IReForgeNetService, IDisposable
{
	private readonly ReForgeNetMessageRegistry _registry = new();
	private readonly ReForgeNetMessageBus _messageBus;
	private IReForgeNetTransport _transport;
	private ReForgeNetProtocolInteropMode _interopMode;
	private bool _disposed;

	public ReForgeNetService(
		IReForgeNetTransport transport,
		ReForgeNetProtocolInteropMode interopMode = ReForgeNetProtocolInteropMode.Hybrid)
	{
		ArgumentNullException.ThrowIfNull(transport);
		_messageBus = new ReForgeNetMessageBus(_registry);
		_transport = transport;
		_interopMode = interopMode;
		_transport.PacketReceived += OnPacketReceived;
	}

	public ulong LocalPeerId => _transport.LocalPeerId;

	public bool IsConnected => _transport.IsConnected;

	internal IReForgeNetTransport Transport => _transport;

	internal ReForgeNetProtocolInteropMode InteropMode => _interopMode;

	public void RegisterMessage<T>(byte id) where T : IReForgeNetMessage, new()
	{
		ThrowIfDisposed();
		_registry.RegisterMessage<T>(id);
	}

	public void RegisterMessageHandler<T>(ReForgeMessageHandlerDelegate<T> handler) where T : IReForgeNetMessage
	{
		ThrowIfDisposed();
		_messageBus.RegisterMessageHandler(handler);
	}

	public void UnregisterMessageHandler<T>(ReForgeMessageHandlerDelegate<T> handler) where T : IReForgeNetMessage
	{
		ThrowIfDisposed();
		_messageBus.UnregisterMessageHandler(handler);
	}

	public void Send<T>(T message) where T : IReForgeNetMessage
	{
		ThrowIfDisposed();
		ArgumentNullException.ThrowIfNull(message);

		if (!IsConnected)
		{
			GD.PrintErr($"[ReForge.Network] Attempted to send message '{message.GetType().Name}' while transport is not connected.");
			return;
		}

		bool includeSenderHeader = _interopMode == ReForgeNetProtocolInteropMode.ReForgeOnly;
		byte[] bytes = _messageBus.SerializeMessage(LocalPeerId, message, includeSenderHeader, out int length);
		_transport.SendToAll(bytes, length, message.Mode, message.Mode.ToChannelId());
	}

	public void SendTo<T>(ulong peerId, T message) where T : IReForgeNetMessage
	{
		ThrowIfDisposed();
		ArgumentNullException.ThrowIfNull(message);

		if (!IsConnected)
		{
			GD.PrintErr($"[ReForge.Network] Attempted to send message '{message.GetType().Name}' while transport is not connected.");
			return;
		}

		bool includeSenderHeader = _interopMode == ReForgeNetProtocolInteropMode.ReForgeOnly;
		byte[] bytes = _messageBus.SerializeMessage(LocalPeerId, message, includeSenderHeader, out int length);
		_transport.SendToPeer(peerId, bytes, length, message.Mode, message.Mode.ToChannelId());
	}

	public void SetProtocolInteropMode(ReForgeNetProtocolInteropMode interopMode)
	{
		ThrowIfDisposed();
		_interopMode = interopMode;
	}

	public void Update()
	{
		ThrowIfDisposed();
		_transport.Update();
	}

	public void SetTransport(IReForgeNetTransport transport)
	{
		ThrowIfDisposed();
		ArgumentNullException.ThrowIfNull(transport);

		if (ReferenceEquals(_transport, transport))
		{
			return;
		}

		_transport.PacketReceived -= OnPacketReceived;
		_transport.Dispose();

		_transport = transport;
		_transport.PacketReceived += OnPacketReceived;
	}

	private void OnPacketReceived(ulong senderId, byte[] packetBytes, ReForgeNetTransferMode mode, int channel)
	{
		if (!_messageBus.TryDeserializeMessage(packetBytes, _interopMode, out IReForgeNetMessage? message, out ulong overrideSenderId))
		{
			GD.PrintErr($"[ReForge.Network] Failed to parse incoming packet, size={packetBytes.Length}, mode={mode}, channel={channel}.");
			return;
		}

		if (message == null)
		{
			GD.PrintErr("[ReForge.Network] Deserialization returned null message instance.");
			return;
		}

		ulong resolvedSenderId = overrideSenderId != 0 ? overrideSenderId : senderId;

		if (message.ShouldBroadcast)
		{
			_transport.SendToAll(packetBytes, packetBytes.Length, mode, channel, excludePeerId: resolvedSenderId);
		}

		_messageBus.DispatchMessage(message, resolvedSenderId);
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_transport.PacketReceived -= OnPacketReceived;
		_transport.Dispose();
		_disposed = true;
	}

	private void ThrowIfDisposed()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
	}
}
