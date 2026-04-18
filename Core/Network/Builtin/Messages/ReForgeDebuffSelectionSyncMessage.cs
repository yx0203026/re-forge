#nullable enable

using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;

namespace ReForgeFramework.Networking;

/// <summary>
/// Debuff 选择同步消息：
/// 1) 客户端发送请求给主机（IsAuthoritativeBroadcast=false）；
/// 2) 主机权威应用后广播（IsAuthoritativeBroadcast=true）。
/// </summary>
internal sealed class ReForgeDebuffSelectionSyncMessage : INetMessage
{
	public ulong TargetPlayerNetId { get; set; }

	public ulong ApplierPlayerNetId { get; set; }

	public bool Silent { get; set; }

	public bool IsAuthoritativeBroadcast { get; set; }

	public List<ReForgeDebuffSelectionSyncItem> Items { get; } = new();

	public bool ShouldBroadcast => false;

	public NetTransferMode Mode => NetTransferMode.Reliable;

	public LogLevel LogLevel => LogLevel.Debug;

	public void Serialize(PacketWriter writer)
	{
		writer.WriteULong(TargetPlayerNetId);
		writer.WriteULong(ApplierPlayerNetId);
		writer.WriteBool(Silent);
		writer.WriteBool(IsAuthoritativeBroadcast);

		writer.WriteInt(Items.Count);
		for (int i = 0; i < Items.Count; i++)
		{
			ReForgeDebuffSelectionSyncItem item = Items[i];
			writer.WriteString(item.PowerCategory ?? string.Empty);
			writer.WriteString(item.PowerEntry ?? string.Empty);
			writer.WriteInt(item.Amount);
		}
	}

	public void Deserialize(PacketReader reader)
	{
		TargetPlayerNetId = reader.ReadULong();
		ApplierPlayerNetId = reader.ReadULong();
		Silent = reader.ReadBool();
		IsAuthoritativeBroadcast = reader.ReadBool();

		Items.Clear();
		int count = Math.Max(0, reader.ReadInt());
		for (int i = 0; i < count; i++)
		{
			Items.Add(new ReForgeDebuffSelectionSyncItem
			{
				PowerCategory = reader.ReadString(),
				PowerEntry = reader.ReadString(),
				Amount = reader.ReadInt()
			});
		}
	}

	public ReForgeDebuffSelectionSyncMessage CloneForBroadcast()
	{
		ReForgeDebuffSelectionSyncMessage clone = new()
		{
			TargetPlayerNetId = TargetPlayerNetId,
			ApplierPlayerNetId = ApplierPlayerNetId,
			Silent = Silent,
			IsAuthoritativeBroadcast = true
		};

		for (int i = 0; i < Items.Count; i++)
		{
			ReForgeDebuffSelectionSyncItem item = Items[i];
			clone.Items.Add(new ReForgeDebuffSelectionSyncItem
			{
				PowerCategory = item.PowerCategory,
				PowerEntry = item.PowerEntry,
				Amount = item.Amount
			});
		}

		return clone;
	}
}

internal sealed class ReForgeDebuffSelectionSyncItem
{
	public string PowerCategory { get; set; } = string.Empty;

	public string PowerEntry { get; set; } = string.Empty;

	public int Amount { get; set; }
}
