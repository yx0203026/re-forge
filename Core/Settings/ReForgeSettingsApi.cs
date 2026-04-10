#nullable enable

using System;
using System.Collections.Generic;
using ReForgeFramework.Settings.Controls;
using ReForgeFramework.Settings.Localization;
using ReForgeFramework.Settings.Runtime;
using ReForgeFramework.Settings.SystemAreas;

namespace ReForgeFramework.Settings;

/// <summary>
/// ReForge 设置系统门面：负责设置页组织、本地化与生命周期重注入。
/// </summary>
public sealed class ReForgeSettingsApi
{
	private readonly SettingsScreenHost _settingsScreenHost = new();
	private bool _initialized;

	public string CurrentLocale => UiLocalization.CurrentLocale;

	public void Initialize()
	{
		if (_initialized)
		{
			return;
		}

		_initialized = true;
		SettingsLifecycleBridge.EnsurePatched(this);
	}

	public SettingsScreenHost GetSettingsScreen()
	{
		return _settingsScreenHost;
	}

	public SettingTabPanelHost GetSettingTabPanel()
	{
		return _settingsScreenHost.GetSettingTabPanel();
	}

	/// <summary>
	/// 快速定义一个设置页，并返回链式构建器。
	/// </summary>
	public SettingPageBuilder Page(string screenKey, string title, bool selected = false, float minHeight = 72f)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(screenKey);
		title ??= screenKey;

		SettingTabPanelHost tabHost = GetSettingTabPanel();
		SettingTab tab = tabHost.GetSettingTab(screenKey)
			?? new SettingTab(title, selected: selected, screenKey: screenKey);

		tab.WithText(title)
			.WithSelected(selected)
			.WithMinHeight(minHeight);

		tabHost.AddChild(tab);
		return new SettingPageBuilder(tab);
	}

	public void RegisterLocale(string locale, IReadOnlyDictionary<string, string> entries)
	{
		UiLocalization.RegisterLocale(locale, entries);
	}

	public void SetLocale(string locale)
	{
		UiLocalization.SetLocale(locale);
	}

	public void SetLanguage(string languageCode)
	{
		UiLocalization.SetLocale(languageCode);
	}

	public string T(string key, string? fallback = null)
	{
		return UiLocalization.GetText(key, fallback);
	}

	public string T(string table, string key, string? fallback)
	{
		return UiLocalization.GetText(null, fallback, table, key);
	}

	internal void ReinjectSettings()
	{
		_settingsScreenHost.RemountSettings();
	}
}
