#nullable enable

using System;

namespace ReForgeFramework.Networking;

/// <summary>
/// 战斗及时事件网络触发消息。
/// 由权威端发送 eventId，客户端收到后在本地执行同名事件。
/// </summary>
internal sealed class ReForgeCombatTimelyEventSyncMessage : IReForgeNetMessage
{
	public long Sequence { get; set; }

	public long UtcNowMs { get; set; }

	public string EventId { get; set; } = string.Empty;

	public bool ShouldBroadcast => false;

	public ReForgeNetTransferMode Mode => ReForgeNetTransferMode.Unreliable;

	public ReForgeNetLogLevel LogLevel => ReForgeNetLogLevel.Trace;

	public void Serialize(ReForgePacketWriter writer)
	{
		writer.WriteLong(Sequence);
		writer.WriteLong(UtcNowMs);
		writer.WriteString(EventId ?? string.Empty);
	}

	public void Deserialize(ReForgePacketReader reader)
	{
		Sequence = reader.ReadLong();
		UtcNowMs = reader.ReadLong();
		EventId = reader.ReadString();
	}
}
