#nullable enable

using System;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;

namespace ReForgeFramework.Networking;

/// <summary>
/// 战斗及时事件网络触发消息。
/// 由权威端发送 eventId，客户端收到后在本地执行同名事件。
/// </summary>
internal sealed class ReForgeCombatTimelyEventSyncMessage : INetMessage
{
	public long Sequence { get; set; }

	public long UtcNowMs { get; set; }

	public string EventId { get; set; } = string.Empty;

	public bool ShouldBroadcast => false;

	public NetTransferMode Mode => NetTransferMode.Unreliable;

	public LogLevel LogLevel => LogLevel.VeryDebug;

	public void Serialize(PacketWriter writer)
	{
		writer.WriteLong(Sequence);
		writer.WriteLong(UtcNowMs);
		writer.WriteString(EventId ?? string.Empty);
	}

	public void Deserialize(PacketReader reader)
	{
		Sequence = reader.ReadLong();
		UtcNowMs = reader.ReadLong();
		EventId = reader.ReadString();
	}
}
