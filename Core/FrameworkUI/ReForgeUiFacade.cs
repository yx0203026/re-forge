#nullable enable

using System.Collections.Generic;
using ReForgeFramework.UI.Localization;
using ReForgeFramework.UI.Runtime;
using ReForgeFramework.UI.SystemAreas;

namespace ReForgeFramework.UI;

public sealed class ReForgeUiFacade
{
	private readonly SystemUiAreaHost _mainMenuHost = new(SystemUiArea.MainMenuButtonPanel);
	private readonly SettingTabPanelHost _settingTabHost = new();

	public string CurrentLocale => UiLocalization.CurrentLocale;

	public void Initialize()
	{
		UiRuntimeNode.Ensure();
		UiApiLifecyclePatcher.EnsurePatched(this);
	}

	public SystemUiAreaHost GetMainMenuButtonPanel()
	{
		return _mainMenuHost;
	}

	public SettingTabPanelHost GetSettingTabPanel()
	{
		return _settingTabHost;
	}

	public void ReinjectSystemAreas()
	{
		_mainMenuHost.RemountAll();
		_settingTabHost.RemountAll();
	}

	public void ReinjectArea(SystemUiArea area)
	{
		switch (area)
		{
			case SystemUiArea.MainMenuButtonPanel:
				_mainMenuHost.RemountAll();
				break;
			case SystemUiArea.SettingTabPanel:
				_settingTabHost.RemountAll();
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
