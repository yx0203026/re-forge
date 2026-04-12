#nullable enable

namespace ReForgeFramework.Networking;

/// <summary>
/// 可序列化为网络数据包的数据契约。
/// </summary>
public interface IReForgePacketSerializable
{
	void Serialize(ReForgePacketWriter writer);

	void Deserialize(ReForgePacketReader reader);
}
