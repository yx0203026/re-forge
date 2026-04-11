#nullable enable

using System;
using System.Collections.Generic;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Localization;

namespace ReForgeFramework.ModResources.Patches;

/// <summary>
/// 给官方本地化读取增加嵌入资源兜底：
/// 1. HasEntry 缺失时可识别 ReForge 嵌入本地化；
/// 2. GetRawText 抛 LocException 时回退到嵌入本地化文本。
/// </summary>
[HarmonyPatch]
public static class OfficialLocalizationFallbackPatches
{
	private static readonly FieldInfo LocTableNameField = AccessTools.Field(typeof(LocTable), "_name");
	private static readonly object LogSync = new();
	private static readonly HashSet<string> LoggedFallbackMisses = new(StringComparer.Ordinal);

	[HarmonyPatch(typeof(LocTable), nameof(LocTable.HasEntry))]
	[HarmonyPostfix]
	private static void HasEntryPostfix(LocTable __instance, string key, ref bool __result)
	{
		if (__result)
		{
			return;
		}

		if (TryResolveEmbeddedText(__instance, key, out _))
		{
			__result = true;
		}
	}

	[HarmonyPatch(typeof(LocTable), nameof(LocTable.GetRawText))]
	[HarmonyFinalizer]
	private static Exception? GetRawTextFinalizer(
		LocTable __instance,
		string key,
		ref string __result,
		Exception? __exception)
	{
		if (__exception is not LocException)
		{
			return __exception;
		}

		if (!TryResolveEmbeddedText(__instance, key, out string value))
		{
			return __exception;
		}

		__result = value;
		return null;
	}

	private static bool TryResolveEmbeddedText(LocTable table, string key, out string value)
	{
		value = string.Empty;
		if (string.IsNullOrWhiteSpace(key) || LocManager.Instance == null)
		{
			return false;
		}

		string? tableName = LocTableNameField.GetValue(table) as string;
		if (string.IsNullOrWhiteSpace(tableName))
		{
			return false;
		}

		string language = LocManager.Instance.Language;
		bool resolved = LocalizationResourceBridge.TryGetText(tableName, key, language, out value);
		LogFallbackProbe(tableName, key, language, resolved);
		return resolved;
	}

	private static void LogFallbackProbe(string table, string key, string language, bool resolved)
	{
		if (resolved)
		{
			return;
		}

		string fingerprint = string.Concat(language, "|", table, "|", key);
		lock (LogSync)
		{
			if (!LoggedFallbackMisses.Add(fingerprint))
			{
				return;
			}
		}

		GD.Print($"[ReForge.LocalizationFallback] MISS {language} {table}.{key}");
	}
}
