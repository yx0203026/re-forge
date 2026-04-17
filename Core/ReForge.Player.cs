#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Saves.Runs;
using ReForgeFramework.Networking;

public static partial class ReForge
{
	/// <summary>
	/// Player 快捷封装入口。
	///
	/// 目标：
	/// 1. 让模组侧更方便地读写 Player 常用状态；
	/// 2. 把遗物、金币、药水等常见操作统一收口；
	/// 3. 提供基于官方 SerializablePlayer 的网络快照同步。
	/// </summary>
	public static class Player
	{
		private static readonly object SyncRoot = new();
		private static bool _initialized;

		public static bool IsInitialized => _initialized;

		public static void Initialize()
		{
			lock (SyncRoot)
			{
				if (_initialized)
				{
					return;
				}

				_initialized = true;
			}

			ReForgePlayerSyncRuntime.Initialize();
			GD.Print("[ReForge.Player] initialized.");
		}

		public static ulong GetNetId(MegaCrit.Sts2.Core.Entities.Players.Player player)
		{
			ArgumentNullException.ThrowIfNull(player);
			return player.NetId;
		}

		public static Creature GetCreature(MegaCrit.Sts2.Core.Entities.Players.Player player)
		{
			ArgumentNullException.ThrowIfNull(player);
			return player.Creature;
		}

		public static IReadOnlyList<RelicModel> GetRelics(MegaCrit.Sts2.Core.Entities.Players.Player player)
		{
			ArgumentNullException.ThrowIfNull(player);
			return player.Relics;
		}

		public static IReadOnlyList<PotionModel?> GetPotionSlots(MegaCrit.Sts2.Core.Entities.Players.Player player)
		{
			ArgumentNullException.ThrowIfNull(player);
			return player.PotionSlots;
		}

		public static IEnumerable<PotionModel> GetPotions(MegaCrit.Sts2.Core.Entities.Players.Player player)
		{
			ArgumentNullException.ThrowIfNull(player);
			return player.Potions;
		}

		public static IReadOnlyList<CardModel> GetDeckCards(MegaCrit.Sts2.Core.Entities.Players.Player player)
		{
			ArgumentNullException.ThrowIfNull(player);
			return player.Deck.Cards;
		}

		public static int GetGold(MegaCrit.Sts2.Core.Entities.Players.Player player)
		{
			ArgumentNullException.ThrowIfNull(player);
			return player.Gold;
		}

		public static void SetGold(MegaCrit.Sts2.Core.Entities.Players.Player player, int gold, bool syncNetwork = true)
		{
			ArgumentNullException.ThrowIfNull(player);
			player.Gold = gold;
			if (syncNetwork)
			{
				SyncSnapshot(player);
			}
		}

		public static void AddGold(MegaCrit.Sts2.Core.Entities.Players.Player player, int delta, bool syncNetwork = true)
		{
			ArgumentNullException.ThrowIfNull(player);
			player.Gold += delta;
			if (syncNetwork)
			{
				SyncSnapshot(player);
			}
		}

		public static T? GetRelic<T>(MegaCrit.Sts2.Core.Entities.Players.Player player) where T : RelicModel
		{
			ArgumentNullException.ThrowIfNull(player);
			return player.GetRelic<T>();
		}

		public static RelicModel? GetRelicById(MegaCrit.Sts2.Core.Entities.Players.Player player, ModelId id)
		{
			ArgumentNullException.ThrowIfNull(player);
			return player.GetRelicById(id);
		}

		public static bool HasRelic<T>(MegaCrit.Sts2.Core.Entities.Players.Player player) where T : RelicModel
		{
			return GetRelic<T>(player) != null;
		}

		public static bool HasRelic(MegaCrit.Sts2.Core.Entities.Players.Player player, ModelId relicId)
		{
			return GetRelicById(player, relicId) != null;
		}

		public static async Task<RelicModel> ObtainRelicAsync(MegaCrit.Sts2.Core.Entities.Players.Player player, RelicModel relic, bool syncNetwork = true)
		{
			ArgumentNullException.ThrowIfNull(player);
			ArgumentNullException.ThrowIfNull(relic);

			RelicModel obtainedRelic = await RelicCmd.Obtain(relic, player);
			if (syncNetwork)
			{
				SyncSnapshot(player);
			}

			return obtainedRelic;
		}

		public static async Task RemoveRelicAsync(MegaCrit.Sts2.Core.Entities.Players.Player player, RelicModel relic, bool syncNetwork = true)
		{
			ArgumentNullException.ThrowIfNull(player);
			ArgumentNullException.ThrowIfNull(relic);

			await RelicCmd.Remove(relic);
			if (syncNetwork)
			{
				SyncSnapshot(player);
			}
		}

		public static void MeltRelic(MegaCrit.Sts2.Core.Entities.Players.Player player, RelicModel relic, bool syncNetwork = true)
		{
			ArgumentNullException.ThrowIfNull(player);
			ArgumentNullException.ThrowIfNull(relic);

			player.MeltRelicInternal(relic);
			if (syncNetwork)
			{
				SyncSnapshot(player);
			}
		}

		public static void AddPotion(MegaCrit.Sts2.Core.Entities.Players.Player player, PotionModel potion, bool syncNetwork = true)
		{
			ArgumentNullException.ThrowIfNull(player);
			ArgumentNullException.ThrowIfNull(potion);

			player.AddPotionInternal(potion);
			if (syncNetwork)
			{
				SyncSnapshot(player);
			}
		}

		public static void RemovePotion(MegaCrit.Sts2.Core.Entities.Players.Player player, PotionModel potion, bool syncNetwork = true)
		{
			ArgumentNullException.ThrowIfNull(player);
			ArgumentNullException.ThrowIfNull(potion);

			player.DiscardPotionInternal(potion);
			if (syncNetwork)
			{
				SyncSnapshot(player);
			}
		}

		public static async Task SetMaxAndCurrentHpAsync(MegaCrit.Sts2.Core.Entities.Players.Player player, decimal maxHp, bool syncNetwork = true)
		{
			ArgumentNullException.ThrowIfNull(player);

			await CreatureCmd.SetMaxAndCurrentHp(player.Creature, maxHp);
			if (syncNetwork)
			{
				SyncSnapshot(player);
			}
		}

		public static async Task HealAsync(MegaCrit.Sts2.Core.Entities.Players.Player player, decimal amount, bool syncNetwork = true)
		{
			ArgumentNullException.ThrowIfNull(player);

			await CreatureCmd.Heal(player.Creature, amount);
			if (syncNetwork)
			{
				SyncSnapshot(player);
			}
		}

		public static SerializablePlayer CaptureSnapshot(MegaCrit.Sts2.Core.Entities.Players.Player player)
		{
			ArgumentNullException.ThrowIfNull(player);
			return ReForgePlayerSyncRuntime.CaptureSnapshot(player);
		}

		public static void ApplySnapshot(MegaCrit.Sts2.Core.Entities.Players.Player player, SerializablePlayer snapshot)
		{
			ArgumentNullException.ThrowIfNull(player);
			ReForgePlayerSyncRuntime.ApplySnapshot(player, snapshot);
		}

		public static bool SyncSnapshot(MegaCrit.Sts2.Core.Entities.Players.Player player)
		{
			ArgumentNullException.ThrowIfNull(player);
			return ReForgePlayerSyncRuntime.SyncSnapshot(player);
		}

		public static bool SyncSnapshotToAll(MegaCrit.Sts2.Core.Entities.Players.Player player)
		{
			ArgumentNullException.ThrowIfNull(player);
			return ReForgePlayerSyncRuntime.SyncSnapshotToAll(player);
		}

		public static bool SyncSnapshotToAuthority(MegaCrit.Sts2.Core.Entities.Players.Player player)
		{
			ArgumentNullException.ThrowIfNull(player);
			return ReForgePlayerSyncRuntime.SyncSnapshotToAuthority(player);
		}

		public static bool SyncSnapshotToPeer(MegaCrit.Sts2.Core.Entities.Players.Player player, ulong peerId)
		{
			ArgumentNullException.ThrowIfNull(player);
			return ReForgePlayerSyncRuntime.SyncSnapshotToPeer(player, peerId);
		}
	}
}