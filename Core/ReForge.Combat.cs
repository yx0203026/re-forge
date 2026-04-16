#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Models;

public static partial class ReForge
{
	/// <summary>
	/// ReForge 战斗工具入口：封装常见的净化/清理流程，减少模组重复代码。
	/// </summary>
	public static partial class Combat
	{
		private static readonly PileType[] DefaultCombatPileTypes =
		[
			PileType.Hand,
			PileType.Draw,
			PileType.Discard
		];

		/// <summary>
		/// 判断 Power 当前表现是否属于负面效果（Debuff）。
		/// </summary>
		public static bool IsNegativePower(PowerModel power)
		{
			ArgumentNullException.ThrowIfNull(power);
			return power.TypeForCurrentAmount == PowerType.Debuff;
		}

		/// <summary>
		/// 移除目标生物身上的所有负面 Power。
		/// </summary>
		public static async Task<int> ClearNegativePowers(Creature creature)
		{
			ArgumentNullException.ThrowIfNull(creature);
			List<PowerModel> toRemove = creature.Powers.Where(IsNegativePower).ToList();
			foreach (PowerModel power in toRemove)
			{
				await PowerCmd.Remove(power);
			}

			return toRemove.Count;
		}

		/// <summary>
		/// 对玩家集合批量移除负面 Power。
		/// </summary>
		public static async Task<int> ClearNegativePowersForPlayers(IEnumerable<Player> players)
		{
			ArgumentNullException.ThrowIfNull(players);
			int total = 0;
			foreach (Player player in players)
			{
				if (player.Creature.IsDead)
				{
					continue;
				}

				total += await ClearNegativePowers(player.Creature);
			}

			return total;
		}

		/// <summary>
		/// 从战斗牌堆（默认：手牌/抽牌堆/弃牌堆）中筛选卡牌并移动到消耗堆。
		/// </summary>
		public static async Task<int> ExhaustCardsFromCombatPiles(
			Player player,
			Func<CardModel, bool> predicate,
			AbstractModel? source = null,
			bool skipVisuals = true,
			IReadOnlyList<PileType>? pileTypes = null)
		{
			ArgumentNullException.ThrowIfNull(player);
			ArgumentNullException.ThrowIfNull(predicate);

			if (!CombatManager.Instance.IsInProgress || player.Creature.CombatState == null)
			{
				return 0;
			}

			IReadOnlyList<PileType> targetPileTypes = pileTypes ?? DefaultCombatPileTypes;
			List<CardModel> cards = new();
			foreach (PileType pileType in targetPileTypes)
			{
				CardPile pile = pileType.GetPile(player);
				cards.AddRange(pile.Cards.Where(predicate));
			}

			if (cards.Count == 0)
			{
				return 0;
			}

			IReadOnlyList<CardPileAddResult> results = await CardPileCmd.Add(cards, PileType.Exhaust, CardPilePosition.Bottom, source, skipVisuals);
			return results.Count(static r => r.success);
		}

		/// <summary>
		/// 若卡牌满足给定筛选器，且位于目标牌堆中，则立刻将其移动到消耗堆。
		/// </summary>
		public static async Task<bool> TryMoveCardToExhaustFromCombatPiles(
			CardModel card,
			Func<CardModel, bool> predicate,
			AbstractModel? source = null,
			IReadOnlySet<PileType>? allowedPiles = null)
		{
			ArgumentNullException.ThrowIfNull(card);
			ArgumentNullException.ThrowIfNull(predicate);

			if (!CombatManager.Instance.IsInProgress || !predicate(card))
			{
				return false;
			}

			PileType? pileType = card.Pile?.Type;
			if (pileType == null || pileType == PileType.Exhaust)
			{
				return false;
			}

			IReadOnlySet<PileType> targetPiles = allowedPiles
				?? new HashSet<PileType>
				{
					PileType.Hand,
					PileType.Draw,
					PileType.Discard
				};

			if (!targetPiles.Contains(pileType.Value))
			{
				return false;
			}

			CardPileAddResult result = await CardPileCmd.Add(card, PileType.Exhaust, CardPilePosition.Bottom, source, skipVisuals: true);
			return result.success;
		}
	}
}
