#nullable enable

using System.Collections.Generic;
using HarmonyLib;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Models;
using ReForgeFramework.Api.Events;

namespace ReForgeFramework.EventWheel.Patches;

/// <summary>
/// 远古事件选项补丁。
/// 在官方初始选项生成后接入 EventWheel 变更流程。
/// </summary>
[HarmonyPatch(typeof(AncientEventModel), "GenerateInitialOptionsWrapper")]
internal static class AncientEventOptionPatches
{
	/// <summary>
	/// Postfix：应用远古事件选项变更。
	/// </summary>
	[HarmonyPostfix]
	private static void Postfix(AncientEventModel __instance, ref IReadOnlyList<EventOption> __result)
	{
		EventWheelOptionPatchRuntime.TryApplyMutation(
			model: __instance,
			options: ref __result,
			expectedKind: EventKind.Ancient,
			patchId: "ancient.generate_initial_options");
	}
}
