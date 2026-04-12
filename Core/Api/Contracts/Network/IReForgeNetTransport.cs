#nullable enable

using System;

namespace ReForgeFramework.Networking;

/// <summary>
/// 传输层抽象。可由 ENet、Steam 或内存回环实现。
/// </summary>
public interface IReForgeNetTransport : IDisposable
{
	ulong LocalPeerId { get; }

	bool IsConnected { get; }

	event Action<ulong, byte[], ReForgeNetTransferMode, int>? PacketReceived;

	void SendToPeer(ulong peerId, byte[] packetBytes, int length, ReForgeNetTransferMode mode, int channel);

	void SendToAll(byte[] packetBytes, int length, ReForgeNetTransferMode mode, int channel, ulong? excludePeerId = null);

	void Update();
}
