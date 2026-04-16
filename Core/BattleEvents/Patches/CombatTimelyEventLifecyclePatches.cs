#nullable enable

using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace ReForgeFramework.BattleEvents.Patches;

/// <summary>
/// 战斗及时事件生命周期补丁。
/// 
/// 进入：基于 Hook.BeforeCombatStart。
/// 离开：覆盖胜利结束、失败结束与兜底重置。
/// </summary>
internal static class CombatTimelyEventLifecyclePatches
{
	[HarmonyPatch(typeof(Hook), nameof(Hook.BeforeCombatStart))]
	private static class BeforeCombatStartPatch
	{
		[HarmonyPostfix]
		private static void Postfix(IRunState runState, CombatState? combatState)
		{
			global::ReForge.BattleEvents.OnCombatEnter(runState, combatState);
		}
	}

	[HarmonyPatch(typeof(CombatManager), nameof(CombatManager.EndCombatInternal))]
	private static class EndCombatInternalPatch
	{
		[HarmonyPrefix]
		private static void Prefix(CombatManager __instance)
		{
			CombatState? combatState = __instance.DebugOnlyGetState();
			global::ReForge.BattleEvents.OnCombatLeave(combatState?.RunState, combatState);
		}
	}

	[HarmonyPatch(typeof(CombatManager), "ProcessPendingLoss")]
	private static class ProcessPendingLossPatch
	{
		[HarmonyPrefix]
		private static void Prefix(CombatManager __instance)
		{
			CombatState? combatState = __instance.DebugOnlyGetState();
			global::ReForge.BattleEvents.OnCombatLeave(combatState?.RunState, combatState);
		}
	}

	[HarmonyPatch(typeof(CombatManager), nameof(CombatManager.Reset))]
	private static class CombatResetPatch
	{
		[HarmonyPrefix]
		private static void Prefix(CombatManager __instance)
		{
			CombatState? combatState = __instance.DebugOnlyGetState();
			if (combatState == null)
			{
				return;
			}

			global::ReForge.BattleEvents.OnCombatLeave(combatState.RunState, combatState);
		}
	}
}
