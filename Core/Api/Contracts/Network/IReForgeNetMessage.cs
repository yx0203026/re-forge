#nullable enable

namespace ReForgeFramework.Networking;

/// <summary>
/// ReForge 网络消息契约。
/// </summary>
public interface IReForgeNetMessage : IReForgePacketSerializable
{
	/// <summary>
	/// 是否在接收端继续向其他 Peer 广播。
	/// </summary>
	bool ShouldBroadcast { get; }

	/// <summary>
	/// 传输模式（可靠/不可靠）。
	/// </summary>
	ReForgeNetTransferMode Mode { get; }

	/// <summary>
	/// 消息分发过程的建议日志级别。
	/// </summary>
	ReForgeNetLogLevel LogLevel { get; }
}
