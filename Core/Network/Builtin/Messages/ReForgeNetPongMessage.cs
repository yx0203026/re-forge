#nullable enable

namespace ReForgeFramework.Networking;

internal sealed class ReForgeNetPongMessage : IReForgeNetMessage
{
	public uint Sequence { get; set; }

	public long PingSentUtcMs { get; set; }

	public long PongSentUtcMs { get; set; }

	public bool ShouldBroadcast => false;

	public ReForgeNetTransferMode Mode => ReForgeNetTransferMode.Unreliable;

	public ReForgeNetLogLevel LogLevel => ReForgeNetLogLevel.Trace;

	public void Serialize(ReForgePacketWriter writer)
	{
		writer.WriteUInt(Sequence);
		writer.WriteLong(PingSentUtcMs);
		writer.WriteLong(PongSentUtcMs);
	}

	public void Deserialize(ReForgePacketReader reader)
	{
		Sequence = reader.ReadUInt();
		PingSentUtcMs = reader.ReadLong();
		PongSentUtcMs = reader.ReadLong();
	}
}
