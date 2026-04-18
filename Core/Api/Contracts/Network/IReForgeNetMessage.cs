#nullable enable

using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;

namespace ReForgeFramework.Networking;

/// <summary>
/// 兼容层：历史 ReForge 消息接口。
/// 通过默认接口实现桥接到官方 INetMessage。
/// </summary>
public interface IReForgeNetMessage : IReForgePacketSerializable, INetMessage
{
	new bool ShouldBroadcast { get; }

	new ReForgeNetTransferMode Mode { get; }

	new ReForgeNetLogLevel LogLevel { get; }

	NetTransferMode INetMessage.Mode => Mode.ToOfficial();

	LogLevel INetMessage.LogLevel => LogLevel.ToOfficial();

	void IPacketSerializable.Serialize(PacketWriter writer)
	{
		Serialize(new ReForgePacketWriter(writer));
	}

	void IPacketSerializable.Deserialize(PacketReader reader)
	{
		Deserialize(new ReForgePacketReader(reader));
	}
}
