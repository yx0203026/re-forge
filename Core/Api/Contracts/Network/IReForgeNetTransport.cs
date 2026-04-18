#nullable enable

using System;

namespace ReForgeFramework.Networking;

/// <summary>
/// 兼容层占位接口：新架构已不再使用自定义传输。
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
