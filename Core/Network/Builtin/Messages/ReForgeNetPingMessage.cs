#nullable enable

namespace ReForgeFramework.Networking;

internal sealed class ReForgeNetPingMessage : IReForgeNetMessage
{
	public uint Sequence { get; set; }

	public long SentUtcMs { get; set; }

	public bool ShouldBroadcast => false;

	public ReForgeNetTransferMode Mode => ReForgeNetTransferMode.Unreliable;

	public ReForgeNetLogLevel LogLevel => ReForgeNetLogLevel.Trace;

	public void Serialize(ReForgePacketWriter writer)
	{
		writer.WriteUInt(Sequence);
		writer.WriteLong(SentUtcMs);
	}

	public void Deserialize(ReForgePacketReader reader)
	{
		Sequence = reader.ReadUInt();
		SentUtcMs = reader.ReadLong();
	}
}
