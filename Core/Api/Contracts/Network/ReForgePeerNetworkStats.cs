#nullable enable

namespace ReForgeFramework.Networking;

public readonly record struct ReForgePeerNetworkStats(ulong PeerId, long LastRttMs, long LastPacketLossPermille, long LastUpdatedAtMs, bool IsConnected);
