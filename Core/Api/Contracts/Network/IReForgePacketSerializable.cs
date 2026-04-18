#nullable enable

namespace ReForgeFramework.Networking;

/// <summary>
/// 兼容层：历史 ReForge 包序列化接口。
/// </summary>
public interface IReForgePacketSerializable
{
	void Serialize(ReForgePacketWriter writer);

	void Deserialize(ReForgePacketReader reader);
}
