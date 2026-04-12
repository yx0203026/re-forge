#nullable enable

using System.Collections.Generic;
using HarmonyLib;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Models;
using ReForgeFramework.Api.Events;

namespace ReForgeFramework.EventWheel.Patches;

[HarmonyPatch(typeof(AncientEventModel), "GenerateInitialOptionsWrapper")]
internal static class AncientEventOptionPatches
{
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
