#nullable enable

using System;
using System.Collections.Generic;

namespace ReForgeFramework.Networking;

internal sealed class ReForgeNetworkDiagnosticsRuntime
{
	private const int PingIntervalMs = 1500;
	private const int HeartbeatIntervalMs = 3000;
	private const int HeartbeatTimeoutMs = 10000;

	private readonly IReForgeNetService _service;
	private readonly Func<long> _utcNowMs;
	private readonly Dictionary<ulong, PeerState> _peerStates = new();
	private long _nextPingAtMs;
	private long _nextHeartbeatAtMs;
	private uint _sequence;

	private sealed class PeerState
	{
		public int LastRttMs;
		public int SmoothedRttMs;
		public long LastHeartbeatUtcMs;
	}

	public ReForgeNetworkDiagnosticsRuntime(IReForgeNetService service, Func<long> utcNowMs)
	{
		_service = service;
		_utcNowMs = utcNowMs;
	}

	public void Initialize()
	{
		_service.RegisterMessageHandler<ReForgeNetPingMessage>(OnPing);
		_service.RegisterMessageHandler<ReForgeNetPongMessage>(OnPong);
		_service.RegisterMessageHandler<ReForgeNetHeartbeatMessage>(OnHeartbeat);

		long now = _utcNowMs();
		_nextPingAtMs = now + PingIntervalMs;
		_nextHeartbeatAtMs = now + HeartbeatIntervalMs;
	}

	public void Update()
	{
		if (!_service.IsConnected)
		{
			return;
		}

		long now = _utcNowMs();

		if (now >= _nextPingAtMs)
		{
			_sequence++;
			_service.Send(new ReForgeNetPingMessage
			{
				Sequence = _sequence,
				SentUtcMs = now,
			});
			_nextPingAtMs = now + PingIntervalMs;
		}

		if (now >= _nextHeartbeatAtMs)
		{
			_service.Send(new ReForgeNetHeartbeatMessage
			{
				SentUtcMs = now,
			});
			_nextHeartbeatAtMs = now + HeartbeatIntervalMs;
		}
	}

	public bool TryGetPeerStats(ulong peerId, out ReForgePeerNetworkStats stats)
	{
		long now = _utcNowMs();
		if (_peerStates.TryGetValue(peerId, out PeerState? state))
		{
			stats = new ReForgePeerNetworkStats(
				peerId,
				state.LastRttMs,
				state.SmoothedRttMs,
				state.LastHeartbeatUtcMs,
				IsHeartbeatTimeout(state.LastHeartbeatUtcMs, now)
			);

			return true;
		}

		stats = new ReForgePeerNetworkStats(peerId, 0, 0, 0, false);
		return false;
	}

	private void OnPing(ReForgeNetPingMessage message, ulong senderId)
	{
		_service.SendTo(senderId, new ReForgeNetPongMessage
		{
			Sequence = message.Sequence,
			PingSentUtcMs = message.SentUtcMs,
			PongSentUtcMs = _utcNowMs(),
		});
	}

	private void OnPong(ReForgeNetPongMessage message, ulong senderId)
	{
		long now = _utcNowMs();
		int rtt = (int)Math.Max(0, now - message.PingSentUtcMs);

		PeerState state = GetOrCreatePeerState(senderId);
		state.LastRttMs = rtt;
		if (state.SmoothedRttMs == 0)
		{
			state.SmoothedRttMs = rtt;
		}
		else
		{
			state.SmoothedRttMs = (state.SmoothedRttMs * 7 + rtt * 3) / 10;
		}
	}

	private void OnHeartbeat(ReForgeNetHeartbeatMessage _message, ulong senderId)
	{
		PeerState state = GetOrCreatePeerState(senderId);
		state.LastHeartbeatUtcMs = _utcNowMs();
	}

	private PeerState GetOrCreatePeerState(ulong peerId)
	{
		if (!_peerStates.TryGetValue(peerId, out PeerState? state))
		{
			state = new PeerState();
			_peerStates[peerId] = state;
		}

		return state;
	}

	private static bool IsHeartbeatTimeout(long lastHeartbeatUtcMs, long nowUtcMs)
	{
		if (lastHeartbeatUtcMs <= 0)
		{
			return false;
		}

		return nowUtcMs - lastHeartbeatUtcMs > HeartbeatTimeoutMs;
	}
}
