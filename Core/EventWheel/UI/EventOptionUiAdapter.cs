#nullable enable

using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Events;

namespace ReForgeFramework.EventWheel.UI;

/// <summary>
/// 事件选项 UI 适配入口：负责挂接 STS2 布局生命周期，并提供分页配置入口。
/// </summary>
public static class EventOptionUiAdapter
{
	/// <summary>
	/// 配置普通事件与远古事件每页显示数量。
	/// 传入 null 表示保持当前配置；默认值为每页 4 项。
	/// </summary>
	public static void ConfigurePaging(int? normalOptionsPerPage = null, int? ancientOptionsPerPage = null)
	{
		EventOptionPagerRuntime.Configure(normalOptionsPerPage, ancientOptionsPerPage);
	}
}

[HarmonyPatch(typeof(NEventLayout), "AddOptions")]
/// <summary>
/// 普通事件布局补丁：在 AddOptions 后刷新分页布局。
/// </summary>
internal static class EventOptionUiLayoutPatches
{
	[HarmonyPostfix]
	private static void AddOptionsPostfix(NEventLayout __instance)
	{
		EventOptionPagerRuntime.EnsureForLayout(__instance, "neventlayout.add_options");
	}
}

[HarmonyPatch(typeof(NAncientEventLayout), "OnSetupComplete")]
/// <summary>
/// 远古事件初始化完成补丁：建立分页条。
/// </summary>
internal static class EventOptionUiAncientSetupPatches
{
	[HarmonyPostfix]
	private static void OnSetupCompletePostfix(NAncientEventLayout __instance)
	{
		EventOptionPagerRuntime.EnsureForLayout(__instance, "nancienteventlayout.setup_complete");
	}
}

[HarmonyPatch(typeof(NAncientEventLayout), "SetDialogueLineAndAnimate")]
/// <summary>
/// 远古事件对话更新补丁：保证动态刷新后分页仍正确。
/// </summary>
internal static class EventOptionUiAncientDialoguePatches
{
	[HarmonyPostfix]
	private static void SetDialogueLineAndAnimatePostfix(NAncientEventLayout __instance)
	{
		EventOptionPagerRuntime.EnsureForLayout(__instance, "nancienteventlayout.dialogue_line");
	}
}
