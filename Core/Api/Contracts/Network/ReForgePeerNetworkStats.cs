#nullable enable

namespace ReForgeFramework.Networking;

/// <summary>
/// Peer 网络质量快照。
/// </summary>
public readonly record struct ReForgePeerNetworkStats(
	ulong PeerId,
	int LastRttMs,
	int SmoothedRttMs,
	long LastHeartbeatUtcMs,
	bool IsHeartbeatTimeout
);
