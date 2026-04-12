#nullable enable

namespace ReForgeFramework.Networking;

internal sealed class ReForgeModelCatalogHelloMessage : IReForgeNetMessage
{
	public uint Hash { get; set; }

	public int CategoryCount { get; set; }

	public int EntryCount { get; set; }

	public long SentUtcMs { get; set; }

	public bool ShouldBroadcast => false;

	public ReForgeNetTransferMode Mode => ReForgeNetTransferMode.Reliable;

	public ReForgeNetLogLevel LogLevel => ReForgeNetLogLevel.Info;

	public void Serialize(ReForgePacketWriter writer)
	{
		writer.WriteUInt(Hash);
		writer.WriteInt(CategoryCount);
		writer.WriteInt(EntryCount);
		writer.WriteLong(SentUtcMs);
	}

	public void Deserialize(ReForgePacketReader reader)
	{
		Hash = reader.ReadUInt();
		CategoryCount = reader.ReadInt();
		EntryCount = reader.ReadInt();
		SentUtcMs = reader.ReadLong();
	}
}
