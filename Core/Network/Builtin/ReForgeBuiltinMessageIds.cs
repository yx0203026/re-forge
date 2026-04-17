#nullable enable

namespace ReForgeFramework.Networking;

/// <summary>
/// ReForge 内置网络消息 ID 保留段。
/// 避免与业务消息冲突，统一放在高位区间。
/// </summary>
internal static class ReForgeBuiltinMessageIds
{
	public const byte Ping = 240;
	public const byte Pong = 241;
	public const byte Heartbeat = 242;
	public const byte ModelCatalogHello = 243;
	public const byte ModelCatalogResult = 244;
	public const byte CombatTimelyEventSync = 245;
	public const byte PlayerSync = 246;
	public const byte RoomSync = 247;
}
