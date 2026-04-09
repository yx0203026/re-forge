#nullable enable

using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes;
using ReForgeFramework.ModResources;

namespace ReForgeFramework.UI.Localization;

/// <summary>
/// 轻量本地化中心：负责语言包注册、语言切换与文本查询。
/// </summary>
public static class UiLocalization
{
	private const string DefaultLocale = "eng";

	private static readonly Dictionary<string, Dictionary<string, string>> Catalog =
		new(StringComparer.OrdinalIgnoreCase);

	private static readonly Dictionary<string, string> LocaleAliases = new(StringComparer.OrdinalIgnoreCase)
	{
		["en"] = "eng",
		["en-us"] = "eng",
		["en_us"] = "eng",
		["zh"] = "zhs",
		["zh-cn"] = "zhs",
		["zh_cn"] = "zhs",
		["zh-hans"] = "zhs",
		["zh_hans"] = "zhs",
		["zh-tw"] = "zht",
		["zh_tw"] = "zht",
		["zh-hant"] = "zht",
		["zh_hant"] = "zht"
	};

	private static string _currentLocale = DefaultLocale;
	private static bool _officialHooked;

	/// <summary>
	/// 语言切换事件。
	/// </summary>
	public static event Action? LocaleChanged;

	/// <summary>
	/// 当前语言代码。
	/// </summary>
	public static string CurrentLocale
	{
		get
		{
			EnsureOfficialBridge();
			return _currentLocale;
		}
	}

	/// <summary>
	/// 注册某个语言的词条集合。
	/// </summary>
	/// <param name="locale">语言代码。</param>
	/// <param name="entries">词条集合。</param>
	public static void RegisterLocale(string locale, IReadOnlyDictionary<string, string> entries)
	{
		if (string.IsNullOrWhiteSpace(locale) || entries.Count == 0)
		{
			return;
		}

		locale = NormalizeLocale(locale);

		if (!Catalog.TryGetValue(locale, out Dictionary<string, string>? target))
		{
			target = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			Catalog[locale] = target;
		}

		foreach ((string key, string value) in entries)
		{
			if (string.IsNullOrWhiteSpace(key))
			{
				continue;
			}

			target[key] = value;
		}
	}

	/// <summary>
	/// 切换当前语言。
	/// </summary>
	/// <param name="locale">目标语言代码。</param>
	public static void SetLocale(string locale)
	{
		if (string.IsNullOrWhiteSpace(locale))
		{
			return;
		}

		string normalized = NormalizeLocale(locale);
		if (TrySetOfficialLocale(normalized))
		{
			return;
		}

		if (string.Equals(_currentLocale, normalized, StringComparison.OrdinalIgnoreCase))
		{
			return;
		}

		_currentLocale = normalized;
		LocalizationResourceBridge.RefreshCurrentLanguage();
		LocaleChanged?.Invoke();
	}

	/// <summary>
	/// 查询本地化文本。
	/// </summary>
	/// <param name="key">UI 本地化 key（可选）。</param>
	/// <param name="fallbackText">未命中时的回退文本。</param>
	/// <param name="locTable">官方本地化表名（可选）。</param>
	/// <param name="locEntryKey">官方本地化词条键（可选）。</param>
	/// <returns>解析后的文本。</returns>
	public static string GetText(string? key, string? fallbackText = null, string? locTable = null, string? locEntryKey = null)
	{
		EnsureOfficialBridge();

		if (TryResolveOfficial(locTable, locEntryKey, key, out string officialText))
		{
			return officialText;
		}

		if (TryResolveEmbeddedLocalization(locTable, locEntryKey, key, out string embeddedText))
		{
			return embeddedText;
		}

		if (string.IsNullOrWhiteSpace(key))
		{
			return fallbackText ?? string.Empty;
		}

		if (TryResolve(_currentLocale, key, out string localized))
		{
			return localized;
		}

		if (!string.Equals(_currentLocale, DefaultLocale, StringComparison.OrdinalIgnoreCase)
			&& TryResolve(DefaultLocale, key, out localized))
		{
			return localized;
		}

		return fallbackText ?? key;
	}

	private static bool TryResolveOfficial(string? locTable, string? locEntryKey, string? keyPath, out string value)
	{
		value = string.Empty;
		if (!TryBuildOfficialLocString(locTable, locEntryKey, keyPath, out LocString locString))
		{
			return false;
		}

		try
		{
			if (!locString.Exists())
			{
				return false;
			}

			string text = locString.GetFormattedText();
			if (string.IsNullOrWhiteSpace(text))
			{
				return false;
			}

			value = text;
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static bool TryResolveEmbeddedLocalization(string? locTable, string? locEntryKey, string? keyPath, out string value)
	{
		value = string.Empty;
		if (!string.IsNullOrWhiteSpace(locTable) && !string.IsNullOrWhiteSpace(locEntryKey)
			&& LocalizationResourceBridge.TryGetText(locTable, locEntryKey, _currentLocale, out string directText))
		{
			value = directText;
			return true;
		}

		if (string.IsNullOrWhiteSpace(keyPath))
		{
			return false;
		}

		int splitIndex = keyPath.IndexOf('/');
		if (splitIndex <= 0 || splitIndex >= keyPath.Length - 1)
		{
			return false;
		}

		string table = keyPath[..splitIndex];
		string entry = keyPath[(splitIndex + 1)..];
		if (!LocalizationResourceBridge.TryGetText(table, entry, _currentLocale, out string keyPathText))
		{
			return false;
		}

		value = keyPathText;
		return true;
	}

	private static bool TryBuildOfficialLocString(string? locTable, string? locEntryKey, string? keyPath, out LocString locString)
	{
		locString = null!;

		if (!string.IsNullOrWhiteSpace(locTable) && !string.IsNullOrWhiteSpace(locEntryKey))
		{
			locString = new LocString(locTable, locEntryKey);
			return true;
		}

		if (string.IsNullOrWhiteSpace(keyPath))
		{
			return false;
		}

		int splitIndex = keyPath.IndexOf('/');
		if (splitIndex <= 0 || splitIndex >= keyPath.Length - 1)
		{
			return false;
		}

		string table = keyPath[..splitIndex];
		string entry = keyPath[(splitIndex + 1)..];
		locString = new LocString(table, entry);
		return true;
	}

	private static bool TrySetOfficialLocale(string locale)
	{
		try
		{
			EnsureOfficialBridge();
			if (!_officialHooked)
			{
				return false;
			}

			if (string.Equals(LocManager.Instance.Language, locale, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}

			LocManager.Instance.SetLanguage(locale);
			if (NGame.Instance != null)
			{
				NGame.Instance.Relocalize();
			}
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static void EnsureOfficialBridge()
	{
		if (_officialHooked)
		{
			return;
		}

		try
		{
			if (LocManager.Instance == null)
			{
				return;
			}

			_currentLocale = NormalizeLocale(LocManager.Instance.Language);
			LocString.SubscribeToLocaleChange(OnOfficialLocaleChanged);
			_officialHooked = true;
		}
		catch
		{
			// LocManager 尚未初始化时保持静默，后续调用会再次尝试桥接。
		}
	}

	private static void OnOfficialLocaleChanged()
	{
		try
		{
			_currentLocale = NormalizeLocale(LocManager.Instance.Language);
			LocalizationResourceBridge.RefreshCurrentLanguage();
		}
		catch
		{
			// 若官方状态暂不可读，保留现有 locale。
		}

		LocaleChanged?.Invoke();
	}

	private static string NormalizeLocale(string locale)
	{
		string normalized = locale.Trim();
		if (LocaleAliases.TryGetValue(normalized, out string? mapped) && !string.IsNullOrWhiteSpace(mapped))
		{
			return mapped;
		}

		return normalized.ToLowerInvariant();
	}

	private static bool TryResolve(string locale, string key, out string value)
	{
		value = string.Empty;
		if (!Catalog.TryGetValue(locale, out Dictionary<string, string>? entries))
		{
			return false;
		}

		if (!entries.TryGetValue(key, out string? resolved) || string.IsNullOrWhiteSpace(resolved))
		{
			return false;
		}

		value = resolved;
		return true;
	}
}
