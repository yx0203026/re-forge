#nullable enable

namespace ReForgeFramework.Networking;

/// <summary>
/// 兼容层：历史 ReForge 网络传输模式。
/// </summary>
public enum ReForgeNetTransferMode
{
	None = 0,
	Reliable = 1,
	Unreliable = 2
}
