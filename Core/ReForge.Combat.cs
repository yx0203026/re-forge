#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using Sts2Player = MegaCrit.Sts2.Core.Entities.Players.Player;

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
		public static async Task<int> ClearNegativePowersForPlayers(IEnumerable<Sts2Player> players)
		{
			ArgumentNullException.ThrowIfNull(players);
			int total = 0;
			foreach (Sts2Player player in players)
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
			Sts2Player player,
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

		/// <summary>
		/// 战斗场景相关 API：统一封装战斗房间节点访问与背景挂载逻辑。
		/// </summary>
		public static class Scene
		{
			/// <summary>
			/// 尝试获取当前战斗房间的背景容器（%BgContainer）。
			/// </summary>
			public static bool TryGetBackgroundContainer(out Control? backgroundContainer)
			{
				backgroundContainer = null;

				NCombatRoom? room = NCombatRoom.Instance;
				if (room == null)
				{
					return false;
				}

				backgroundContainer = room.GetNodeOrNull<Control>("%BgContainer");
				return backgroundContainer != null;
			}

			/// <summary>
			/// 将节点挂载到战斗背景容器（%BgContainer）。
			/// 若节点已有父节点，会先重挂载到背景容器。
			/// </summary>
			public static bool TryMountNodeToBackground(Node node, out string reason)
			{
				ArgumentNullException.ThrowIfNull(node);

				if (!TryGetBackgroundContainer(out Control? backgroundContainer) || backgroundContainer == null)
				{
					reason = "Combat background container not found.";
					return false;
				}

				if (ReferenceEquals(node.GetParent(), backgroundContainer))
				{
					reason = "Node is already mounted to combat background container.";
					return true;
				}

				if (node.GetParent() != null)
				{
					node.Reparent(backgroundContainer);
				}
				else
				{
					backgroundContainer.AddChild(node);
				}

				reason = "Mounted to combat background container.";
				return true;
			}
		}
	}

	/// <summary>
	/// 奖励系统快捷封装入口。
	///
	/// 目标：
	/// 1. 统一“战后奖励池”写入逻辑，避免模组侧直接操作底层房间实现；
	/// 2. 提供稳定的遗物奖励入队 API，保证奖励在战后奖励界面可见；
	/// 3. 减少 API 割裂，保持模组调用风格与 ReForge 其它子模块一致。
	/// </summary>
	public static class Rewards
	{
		/// <summary>
		/// 向当前战斗房间的额外奖励池追加一条奖励。
		/// </summary>
		/// <param name="runState">当前运行状态。</param>
		/// <param name="player">奖励归属玩家。</param>
		/// <param name="reward">要追加的奖励实例（必须为可序列化的 Reward）。</param>
		/// <exception cref="InvalidOperationException">当当前房间不是 CombatRoom 时抛出。</exception>
		public static void AddExtraRewardToCurrentCombat(IRunState runState, Sts2Player player, Reward reward)
		{
			ArgumentNullException.ThrowIfNull(runState);
			ArgumentNullException.ThrowIfNull(player);
			ArgumentNullException.ThrowIfNull(reward);

			if (runState.CurrentRoom is not CombatRoom combatRoom)
			{
				throw new InvalidOperationException("Current room is not CombatRoom, cannot enqueue combat-end reward.");
			}

			combatRoom.AddExtraReward(player, reward);
		}

		/// <summary>
		/// 向当前战斗房间的额外奖励池追加一条遗物奖励（指定遗物）。
		/// </summary>
		/// <param name="runState">当前运行状态。</param>
		/// <param name="player">奖励归属玩家。</param>
		/// <param name="relic">预设遗物模型（可变实例）。</param>
		/// <returns>已入队的遗物奖励实例。</returns>
		public static RelicReward AddRelicRewardToCurrentCombat(IRunState runState, Sts2Player player, RelicModel relic)
		{
			ArgumentNullException.ThrowIfNull(relic);
			relic.AssertMutable();

			RelicReward reward = new RelicReward(relic, player);
			AddExtraRewardToCurrentCombat(runState, player, reward);
			return reward;
		}

		/// <summary>
		/// 向当前战斗房间的额外奖励池追加一条遗物奖励（按官方遗物池抽取）。
		/// </summary>
		/// <param name="runState">当前运行状态。</param>
		/// <param name="player">奖励归属玩家。</param>
		/// <returns>抽取并入队的遗物模型（可变实例）。</returns>
		public static RelicModel AddRandomRelicRewardToCurrentCombat(IRunState runState, Sts2Player player)
		{
			ArgumentNullException.ThrowIfNull(runState);
			ArgumentNullException.ThrowIfNull(player);

			RelicModel relic = RelicFactory.PullNextRelicFromFront(player).ToMutable();
			_ = AddRelicRewardToCurrentCombat(runState, player, relic);
			return relic;
		}
	}
}
