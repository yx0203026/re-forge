#nullable enable

namespace ReForgeFramework.Networking;

/// <summary>
/// 网络载荷互通模式：
/// - ReForgeOnly：仅使用 ReForge 私有载荷格式（messageId + senderId + payload）。
/// - OfficialOnly：仅使用官方原生载荷格式（messageId + payload，sender 由传输层提供）。
/// - Hybrid：接收端同时兼容两种格式；发送端优先使用官方原生格式以提高互通性。
/// </summary>
public enum ReForgeNetProtocolInteropMode
{
	ReForgeOnly = 0,
	OfficialOnly = 1,
	Hybrid = 2,
}
