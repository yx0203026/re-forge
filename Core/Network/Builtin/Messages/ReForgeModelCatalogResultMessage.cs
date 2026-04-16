#nullable enable

namespace ReForgeFramework.Networking;

internal sealed class ReForgeModelCatalogResultMessage : IReForgeNetMessage
{
	public bool Accepted { get; set; }

	public uint LocalHash { get; set; }

	public uint RemoteHash { get; set; }

	public int LocalCategoryCount { get; set; }

	public int LocalEntryCount { get; set; }

	public string Reason { get; set; } = string.Empty;

	public bool ShouldBroadcast => false;

	public ReForgeNetTransferMode Mode => ReForgeNetTransferMode.Reliable;

	public ReForgeNetLogLevel LogLevel => ReForgeNetLogLevel.Warn;

	public void Serialize(ReForgePacketWriter writer)
	{
		writer.WriteBool(Accepted);
		writer.WriteUInt(LocalHash);
		writer.WriteUInt(RemoteHash);
		writer.WriteInt(LocalCategoryCount);
		writer.WriteInt(LocalEntryCount);
		writer.WriteString(Reason);
	}

	public void Deserialize(ReForgePacketReader reader)
	{
		Accepted = reader.ReadBool();
		LocalHash = reader.ReadUInt();
		RemoteHash = reader.ReadUInt();
		LocalCategoryCount = reader.ReadInt();
		LocalEntryCount = reader.ReadInt();
		Reason = reader.ReadString();
	}
}
