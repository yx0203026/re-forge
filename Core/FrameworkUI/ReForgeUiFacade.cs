#nullable enable

using System;
using System.Collections.Generic;
using ReForgeFramework.UI.Localization;
using ReForgeFramework.UI.Runtime;
using ReForgeFramework.UI.SystemAreas;

namespace ReForgeFramework.UI;

public sealed class ReForgeUiFacade
{
	private readonly MainMenuScreenHost _mainMenuHost = new();
	private readonly SettingsScreenHost _settingsScreenHost = new();

	public string CurrentLocale => UiLocalization.CurrentLocale;

	public void Initialize()
	{
		UiRuntimeNode.Ensure();
		UiApiLifecyclePatcher.EnsurePatched(this);
	}

	public MainMenuScreenHost GetMainMenuScreen()
	{
		return _mainMenuHost;
	}

	public SettingsScreenHost GetSettingsScreen()
	{
		return _settingsScreenHost;
	}

	public SystemUiAreaHost GetMainMenuButtonPanel()
	{
		return _mainMenuHost.GetMainMenuButtonPanel();
	}

	public SettingTabPanelHost GetSettingTabPanel()
	{
		return _settingsScreenHost.GetSettingTabPanel();
	}

	public SystemUiAreaHost GetScreen(OfficialScreenEnum screen)
	{
		return screen switch
		{
			OfficialScreenEnum.MainMenu => _mainMenuHost,
			OfficialScreenEnum.Settings => _settingsScreenHost,
			_ => throw new ArgumentOutOfRangeException(nameof(screen), screen, "Unsupported official screen enum.")
		};
	}

	public void ReinjectSystemAreas()
	{
		_mainMenuHost.RemountMainMenu();
		_settingsScreenHost.RemountSettings();
	}

	public void ReinjectArea(SystemUiArea area)
	{
		switch (area)
		{
			case SystemUiArea.MainMenuButtonPanel:
				_mainMenuHost.RemountMainMenu();
				break;
			case SystemUiArea.SettingTabPanel:
				_settingsScreenHost.RemountSettings();
				break;
		}
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
}
