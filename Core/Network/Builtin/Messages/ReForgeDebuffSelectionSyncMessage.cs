#nullable enable

using System;
using System.Collections.Generic;

namespace ReForgeFramework.Networking;

/// <summary>
/// Debuff 选择结果同步消息：用于把“本地已确认的选择”广播给所有 Peer，并在远端执行同样的 Power Apply。
/// </summary>
internal sealed class ReForgeDebuffSelectionSyncMessage : IReForgeNetMessage
{
	public ulong TargetPlayerNetId { get; set; }

	public ulong ApplierPlayerNetId { get; set; }

	public bool Silent { get; set; }

	public List<DebuffSelectionSyncItem> Items { get; } = new();

	public bool ShouldBroadcast => false;

	public ReForgeNetTransferMode Mode => ReForgeNetTransferMode.Reliable;

	public ReForgeNetLogLevel LogLevel => ReForgeNetLogLevel.Debug;

	public void Serialize(ReForgePacketWriter writer)
	{
		writer.WriteULong(TargetPlayerNetId);
		writer.WriteULong(ApplierPlayerNetId);
		writer.WriteBool(Silent);

		writer.WriteInt(Items.Count);
		for (int i = 0; i < Items.Count; i++)
		{
			DebuffSelectionSyncItem item = Items[i];
			writer.WriteString(item.PowerCategory ?? string.Empty);
			writer.WriteString(item.PowerEntry ?? string.Empty);
			writer.WriteInt(item.Amount);
		}
	}

	public void Deserialize(ReForgePacketReader reader)
	{
		TargetPlayerNetId = reader.ReadULong();
		ApplierPlayerNetId = reader.ReadULong();
		Silent = reader.ReadBool();

		Items.Clear();
		int count = Math.Max(0, reader.ReadInt());
		for (int i = 0; i < count; i++)
		{
			Items.Add(new DebuffSelectionSyncItem
			{
				PowerCategory = reader.ReadString(),
				PowerEntry = reader.ReadString(),
				Amount = reader.ReadInt()
			});
		}
	}
}

internal sealed class DebuffSelectionSyncItem
{
	public string PowerCategory { get; set; } = string.Empty;

	public string PowerEntry { get; set; } = string.Empty;

	public int Amount { get; set; }
}