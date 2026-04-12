#nullable enable

namespace ReForgeFramework.Networking;

/// <summary>
/// 数据包传输模式。
/// </summary>
public enum ReForgeNetTransferMode
{
	None = 0,
	Unreliable = 1,
	Reliable = 2,
}