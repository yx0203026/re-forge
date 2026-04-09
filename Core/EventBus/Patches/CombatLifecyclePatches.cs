#nullable enable

using System;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using ReForgeFramework.EventBus;

namespace ReForgeFramework.EventBus.Patches;

/// <summary>
/// 战斗生命周期补丁：将卡牌打出、回合阶段、受伤事件统一发布到 EventBus。
/// </summary>
internal static class CombatLifecyclePatches
{
	#region 战斗开始/结束事件

	[HarmonyPatch(typeof(Hook), nameof(Hook.BeforeCombatStart))]
	private static class BeforeCombatStartPatch
	{
		[HarmonyPrefix]
		private static void Prefix(IRunState runState, CombatState? combatState)
		{
			SafePublish(
				CombatLifecycleEventIds.CombatStartBefore,
				new CombatStartBeforeEvent(runState, combatState)
			);
		}
	}

	[HarmonyPatch(typeof(Hook), nameof(Hook.AfterCombatEnd))]
	private static class AfterCombatEndPatch
	{
		[HarmonyPrefix]
		private static void Prefix(IRunState runState, CombatState? combatState, CombatRoom room)
		{
			SafePublish(
				CombatLifecycleEventIds.CombatEndAfter,
				new CombatEndAfterEvent(runState, combatState, room)
			);
		}
	}

	[HarmonyPatch(typeof(Hook), nameof(Hook.AfterCombatVictory))]
	private static class AfterCombatVictoryPatch
	{
		[HarmonyPrefix]
		private static void Prefix(IRunState runState, CombatState? combatState, CombatRoom room)
		{
			SafePublish(
				CombatLifecycleEventIds.CombatVictoryAfter,
				new CombatVictoryAfterEvent(runState, combatState, room)
			);
		}
	}

	#endregion

	#region 卡牌打出事件

	[HarmonyPatch(typeof(Hook), nameof(Hook.BeforeCardAutoPlayed))]
	private static class BeforeCardAutoPlayedPatch
	{
		[HarmonyPrefix]
		private static void Prefix(CombatState combatState, CardModel card, Creature? target, AutoPlayType type)
		{
			SafePublish(
				CombatLifecycleEventIds.CardAutoPlayBefore,
				new CardAutoPlayBeforeEvent(combatState, card, target, type)
			);
		}
	}

	[HarmonyPatch(typeof(Hook), nameof(Hook.BeforeCardPlayed))]
	private static class BeforeCardPlayedPatch
	{
		[HarmonyPrefix]
		private static void Prefix(CombatState combatState, CardPlay cardPlay)
		{
			SafePublish(
				CombatLifecycleEventIds.CardPlayBefore,
				new CardPlayBeforeEvent(combatState, cardPlay)
			);
		}
	}

	[HarmonyPatch(typeof(Hook), nameof(Hook.AfterCardPlayed))]
	private static class AfterCardPlayedPatch
	{
		[HarmonyPrefix]
		private static void Prefix(CombatState combatState, CardPlay cardPlay)
		{
			SafePublish(
				CombatLifecycleEventIds.CardPlayAfter,
				new CardPlayAfterEvent(combatState, cardPlay)
			);
		}
	}

	#endregion

	#region 回合开始/结束事件

	[HarmonyPatch(typeof(Hook), nameof(Hook.BeforeSideTurnStart))]
	private static class BeforeSideTurnStartPatch
	{
		[HarmonyPrefix]
		private static void Prefix(CombatState combatState, CombatSide side)
		{
			SafePublish(
				CombatLifecycleEventIds.TurnStartBefore,
				new TurnPhaseEvent(combatState, side)
			);
		}
	}

	[HarmonyPatch(typeof(Hook), nameof(Hook.AfterSideTurnStart))]
	private static class AfterSideTurnStartPatch
	{
		[HarmonyPrefix]
		private static void Prefix(CombatState combatState, CombatSide side)
		{
			SafePublish(
				CombatLifecycleEventIds.TurnStartAfter,
				new TurnPhaseEvent(combatState, side)
			);
		}
	}

	[HarmonyPatch(typeof(Hook), nameof(Hook.BeforeTurnEnd))]
	private static class BeforeTurnEndPatch
	{
		[HarmonyPrefix]
		private static void Prefix(CombatState combatState, CombatSide side)
		{
			SafePublish(
				CombatLifecycleEventIds.TurnEndBefore,
				new TurnPhaseEvent(combatState, side)
			);
		}
	}

	[HarmonyPatch(typeof(Hook), nameof(Hook.AfterTurnEnd))]
	private static class AfterTurnEndPatch
	{
		[HarmonyPrefix]
		private static void Prefix(CombatState combatState, CombatSide side)
		{
			SafePublish(
				CombatLifecycleEventIds.TurnEndAfter,
				new TurnPhaseEvent(combatState, side)
			);
		}
	}

	#endregion

	#region 玩家/怪物受伤事件

	[HarmonyPatch(typeof(Hook), nameof(Hook.BeforeDamageReceived))]
	private static class BeforeDamageReceivedPatch
	{
		[HarmonyPrefix]
		private static void Prefix(
			CombatState? combatState,
			Creature target,
			decimal amount,
			ValueProp props,
			Creature? dealer,
			CardModel? cardSource)
		{
			if (target.IsPlayer && target.Player != null)
			{
				SafePublish(
					CombatLifecycleEventIds.PlayerDamageBefore,
					new PlayerDamageBeforeEvent(combatState, target, target.Player, dealer, cardSource, amount, props)
				);
				return;
			}

			if (target.IsMonster && target.Monster != null)
			{
				SafePublish(
					CombatLifecycleEventIds.MonsterDamageBefore,
					new MonsterDamageBeforeEvent(combatState, target, target.Monster, dealer, cardSource, amount, props)
				);
			}
		}
	}

	[HarmonyPatch(typeof(Hook), nameof(Hook.AfterDamageReceived))]
	private static class AfterDamageReceivedPatch
	{
		[HarmonyPrefix]
		private static void Prefix(
			CombatState? combatState,
			Creature target,
			DamageResult result,
			ValueProp props,
			Creature? dealer,
			CardModel? cardSource)
		{
			if (target.IsPlayer && target.Player != null)
			{
				SafePublish(
					CombatLifecycleEventIds.PlayerDamageAfter,
					new PlayerDamageAfterEvent(combatState, target, target.Player, dealer, cardSource, result, props)
				);
				return;
			}

			if (target.IsMonster && target.Monster != null)
			{
				SafePublish(
					CombatLifecycleEventIds.MonsterDamageAfter,
					new MonsterDamageAfterEvent(combatState, target, target.Monster, dealer, cardSource, result, props)
				);
			}
		}
	}

	#endregion

	private static void SafePublish<TEvent>(string eventId, TEvent eventArg) where TEvent : IEventArg
	{
		try
		{
			global::ReForge.EventBus.Publish(eventId, eventArg);
		}
		catch (Exception exception)
		{
			GD.PrintErr($"[ReForge.EventBus] combat publish failed. eventId='{eventId}'. {exception}");
		}
	}
}
