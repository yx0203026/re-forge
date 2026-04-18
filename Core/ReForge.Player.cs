#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.Audio.Debug;

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
		private const int DefaultHandLimit = 10;
		private const int MinHandLimit = 1;

		private static readonly object SyncRoot = new();
		private static readonly Dictionary<ulong, int> HandLimitOverrides = new();
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

			GD.Print("[ReForge.Player] initialized. Player sync over ReForgeNet is temporarily disabled.");
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

		/// <summary>
		/// 获取玩家当前有效手牌上限（默认 10）。
		/// </summary>
		public static int GetHandLimit(MegaCrit.Sts2.Core.Entities.Players.Player player)
		{
			ArgumentNullException.ThrowIfNull(player);

			lock (SyncRoot)
			{
				return ResolveHandLimit(player);
			}
		}

		/// <summary>
		/// 设置玩家手牌上限。最小值为 1。
		/// </summary>
		public static void SetHandLimit(MegaCrit.Sts2.Core.Entities.Players.Player player, int handLimit, bool syncNetwork = true)
		{
			ArgumentNullException.ThrowIfNull(player);

			int normalized = Math.Max(MinHandLimit, handLimit);
			lock (SyncRoot)
			{
				if (normalized == DefaultHandLimit)
				{
					HandLimitOverrides.Remove(player.NetId);
				}
				else
				{
					HandLimitOverrides[player.NetId] = normalized;
				}
			}

			GD.Print($"[ReForge.Player] Set hand limit. player='{player.NetId}', handLimit={normalized}.");
		}

		/// <summary>
		/// 按增量修改玩家手牌上限，并返回修改后的上限值。
		/// </summary>
		public static int AddHandLimit(MegaCrit.Sts2.Core.Entities.Players.Player player, int delta, bool syncNetwork = true)
		{
			ArgumentNullException.ThrowIfNull(player);

			int updated;
			lock (SyncRoot)
			{
				int current = ResolveHandLimit(player);
				updated = Math.Max(MinHandLimit, current + delta);
				if (updated == DefaultHandLimit)
				{
					HandLimitOverrides.Remove(player.NetId);
				}
				else
				{
					HandLimitOverrides[player.NetId] = updated;
				}
			}

			GD.Print($"[ReForge.Player] Add hand limit. player='{player.NetId}', delta={delta}, handLimit={updated}.");
			return updated;
		}

		/// <summary>
		/// 重置玩家手牌上限为默认值（10）。
		/// </summary>
		public static void ResetHandLimit(MegaCrit.Sts2.Core.Entities.Players.Player player, bool syncNetwork = true)
		{
			ArgumentNullException.ThrowIfNull(player);
			lock (SyncRoot)
			{
				HandLimitOverrides.Remove(player.NetId);
			}
			GD.Print($"[ReForge.Player] Reset hand limit. player='{player.NetId}', handLimit={DefaultHandLimit}.");
		}

		public static void SetGold(MegaCrit.Sts2.Core.Entities.Players.Player player, int gold, bool syncNetwork = true)
		{
			ArgumentNullException.ThrowIfNull(player);
			player.Gold = gold;
		}

		public static void AddGold(MegaCrit.Sts2.Core.Entities.Players.Player player, int delta, bool syncNetwork = true)
		{
			ArgumentNullException.ThrowIfNull(player);
			player.Gold += delta;
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

			return obtainedRelic;
		}

		public static async Task RemoveRelicAsync(MegaCrit.Sts2.Core.Entities.Players.Player player, RelicModel relic, bool syncNetwork = true)
		{
			ArgumentNullException.ThrowIfNull(player);
			ArgumentNullException.ThrowIfNull(relic);

			await RelicCmd.Remove(relic);
		}

		public static void MeltRelic(MegaCrit.Sts2.Core.Entities.Players.Player player, RelicModel relic, bool syncNetwork = true)
		{
			ArgumentNullException.ThrowIfNull(player);
			ArgumentNullException.ThrowIfNull(relic);

			player.MeltRelicInternal(relic);
		}

		public static void AddPotion(MegaCrit.Sts2.Core.Entities.Players.Player player, PotionModel potion, bool syncNetwork = true)
		{
			ArgumentNullException.ThrowIfNull(player);
			ArgumentNullException.ThrowIfNull(potion);

			player.AddPotionInternal(potion);
		}

		public static void RemovePotion(MegaCrit.Sts2.Core.Entities.Players.Player player, PotionModel potion, bool syncNetwork = true)
		{
			ArgumentNullException.ThrowIfNull(player);
			ArgumentNullException.ThrowIfNull(potion);

			player.DiscardPotionInternal(potion);
		}

		public static async Task SetMaxAndCurrentHpAsync(MegaCrit.Sts2.Core.Entities.Players.Player player, decimal maxHp, bool syncNetwork = true)
		{
			ArgumentNullException.ThrowIfNull(player);

			await CreatureCmd.SetMaxAndCurrentHp(player.Creature, maxHp);
		}

		public static async Task HealAsync(MegaCrit.Sts2.Core.Entities.Players.Player player, decimal amount, bool syncNetwork = true)
		{
			ArgumentNullException.ThrowIfNull(player);

			await CreatureCmd.Heal(player.Creature, amount);
		}

		public static SerializablePlayer CaptureSnapshot(MegaCrit.Sts2.Core.Entities.Players.Player player)
		{
			ArgumentNullException.ThrowIfNull(player);
			return player.ToSerializable();
		}

		public static void ApplySnapshot(MegaCrit.Sts2.Core.Entities.Players.Player player, SerializablePlayer snapshot)
		{
			ArgumentNullException.ThrowIfNull(player);
			player.SyncWithSerializedPlayer(snapshot);
		}

		public static bool SyncSnapshot(MegaCrit.Sts2.Core.Entities.Players.Player player)
		{
			ArgumentNullException.ThrowIfNull(player);
			return false;
		}

		public static bool SyncSnapshotToAll(MegaCrit.Sts2.Core.Entities.Players.Player player)
		{
			ArgumentNullException.ThrowIfNull(player);
			return false;
		}

		public static bool SyncSnapshotToAuthority(MegaCrit.Sts2.Core.Entities.Players.Player player)
		{
			ArgumentNullException.ThrowIfNull(player);
			return false;
		}

		public static bool SyncSnapshotToPeer(MegaCrit.Sts2.Core.Entities.Players.Player player, ulong peerId)
		{
			ArgumentNullException.ThrowIfNull(player);
			return false;
		}

		private static int ResolveHandLimit(MegaCrit.Sts2.Core.Entities.Players.Player player)
		{
			if (HandLimitOverrides.TryGetValue(player.NetId, out int limit))
			{
				return Math.Max(MinHandLimit, limit);
			}

			return DefaultHandLimit;
		}

		private static bool CheckIfDrawIsPossibleAndShowThoughtBubbleIfNot(MegaCrit.Sts2.Core.Entities.Players.Player player, int handLimit)
		{
			if (PileType.Draw.GetPile(player).Cards.Count + PileType.Discard.GetPile(player).Cards.Count == 0)
			{
				ThinkCmd.Play(new LocString("combat_messages", "NO_DRAW"), player.Creature, 2.0);
				return false;
			}

			if (PileType.Hand.GetPile(player).Cards.Count >= handLimit)
			{
				ThinkCmd.Play(new LocString("combat_messages", "HAND_FULL"), player.Creature, 2.0);
				return false;
			}

			return true;
		}

		private static async Task<IEnumerable<CardModel>> DrawWithCustomHandLimitAsync(PlayerChoiceContext choiceContext, decimal count, MegaCrit.Sts2.Core.Entities.Players.Player player, bool fromHandDraw, int handLimit)
		{
			if (CombatManager.Instance.IsOverOrEnding)
			{
				return Array.Empty<CardModel>();
			}

			CombatState? combatState = player.Creature.CombatState;
			if (combatState == null)
			{
				return Array.Empty<CardModel>();
			}

			if (!Hook.ShouldDraw(combatState, player, fromHandDraw, out AbstractModel? modifier))
			{
				if (modifier != null)
				{
					await Hook.AfterPreventingDraw(combatState, modifier);
				}
				return Array.Empty<CardModel>();
			}

			List<CardModel> result = new();
			CardPile hand = PileType.Hand.GetPile(player);
			CardPile drawPile = PileType.Draw.GetPile(player);

			int drawsRequested = count > 0m ? (int)Math.Ceiling(count) : 0;
			if (drawsRequested == 0)
			{
				return result;
			}

			int availableSlots = Math.Max(0, handLimit - hand.Cards.Count);
			if (availableSlots == 0)
			{
				CheckIfDrawIsPossibleAndShowThoughtBubbleIfNot(player, handLimit);
				return result;
			}

			for (int i = 0; i < drawsRequested; i++)
			{
				if (availableSlots <= 0)
				{
					break;
				}

				if (!CheckIfDrawIsPossibleAndShowThoughtBubbleIfNot(player, handLimit))
				{
					break;
				}

				await CardPileCmd.ShuffleIfNecessary(choiceContext, player);
				if (!CheckIfDrawIsPossibleAndShowThoughtBubbleIfNot(player, handLimit))
				{
					break;
				}

				CardModel? card = drawPile.Cards.FirstOrDefault();
				if (card == null || hand.Cards.Count >= handLimit)
				{
					break;
				}

				result.Add(card);
				await CardPileCmd.Add(card, hand);
				CombatManager.Instance.History.CardDrawn(combatState, card, fromHandDraw);
				await Hook.AfterCardDrawn(combatState, choiceContext, card, fromHandDraw);
				card.InvokeDrawn();
				NDebugAudioManager.Instance?.Play("card_deal.mp3", 0.25f, PitchVariance.Small);

				availableSlots = Math.Max(0, handLimit - hand.Cards.Count);
			}

			return result;
		}

		[HarmonyPatch(typeof(CardPileCmd), nameof(CardPileCmd.Draw), new[] { typeof(PlayerChoiceContext), typeof(decimal), typeof(MegaCrit.Sts2.Core.Entities.Players.Player), typeof(bool) })]
		private static class CardPileCmdDrawHandLimitPatch
		{
			[HarmonyPrefix]
			private static bool Prefix(PlayerChoiceContext choiceContext, decimal count, MegaCrit.Sts2.Core.Entities.Players.Player player, bool fromHandDraw, ref Task<IEnumerable<CardModel>> __result)
			{
				int handLimit;
				lock (SyncRoot)
				{
					handLimit = ResolveHandLimit(player);
				}

				if (handLimit == DefaultHandLimit)
				{
					return true;
				}

				__result = DrawWithCustomHandLimitAsync(choiceContext, count, player, fromHandDraw, handLimit);
				return false;
			}
		}
	}
}