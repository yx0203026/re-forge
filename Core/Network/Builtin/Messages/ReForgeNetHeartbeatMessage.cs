#nullable enable

namespace ReForgeFramework.Networking;

internal sealed class ReForgeNetHeartbeatMessage : IReForgeNetMessage
{
	public long SentUtcMs { get; set; }

	public bool ShouldBroadcast => false;

	public ReForgeNetTransferMode Mode => ReForgeNetTransferMode.Unreliable;

	public ReForgeNetLogLevel LogLevel => ReForgeNetLogLevel.Trace;

	public void Serialize(ReForgePacketWriter writer)
	{
		writer.WriteLong(SentUtcMs);
	}

	public void Deserialize(ReForgePacketReader reader)
	{
		SentUtcMs = reader.ReadLong();
	}
}
