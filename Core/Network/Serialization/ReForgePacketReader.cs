#nullable enable

namespace ReForgeFramework.Networking;

/// <summary>
/// ReForge 基础包读取器。
/// 直接继承 STS2 官方 PacketReader，以便复用官方 IPacketSerializable 协议。
/// </summary>
public sealed class ReForgePacketReader : MegaCrit.Sts2.Core.Multiplayer.Serialization.PacketReader
{
}
