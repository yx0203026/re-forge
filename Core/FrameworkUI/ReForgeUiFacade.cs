#nullable enable

using System;
using System.Collections.Generic;
using ReForgeFramework.UI.Localization;
using ReForgeFramework.UI.Runtime;
using ReForgeFramework.UI.SystemAreas;

namespace ReForgeFramework.UI;

/// <summary>
/// ReForge UI 门面入口，负责初始化运行时、获取系统区域宿主与本地化快捷调用。
/// </summary>
public sealed class ReForgeUiFacade
{
	private readonly MainMenuScreenHost _mainMenuHost = new();
	private readonly SettingsScreenHost _settingsScreenHost = new();

	/// <summary>
	/// 获取当前 UI 本地化语言代码。
	/// </summary>
	public string CurrentLocale => UiLocalization.CurrentLocale;

	/// <summary>
	/// 初始化 UI 运行时并挂接生命周期补丁。
	/// </summary>
	public void Initialize()
	{
		UiRuntimeNode.Ensure();
		UiApiLifecyclePatcher.EnsurePatched(this);
	}

	/// <summary>
	/// 获取主菜单屏幕宿主。
	/// </summary>
	public MainMenuScreenHost GetMainMenuScreen()
	{
		return _mainMenuHost;
	}

	/// <summary>
	/// 获取设置屏幕宿主。
	/// </summary>
	public SettingsScreenHost GetSettingsScreen()
	{
		return _settingsScreenHost;
	}

	/// <summary>
	/// 获取主菜单按钮区宿主。
	/// </summary>
	public SystemUiAreaHost GetMainMenuButtonPanel()
	{
		return _mainMenuHost.GetMainMenuButtonPanel();
	}

	/// <summary>
	/// 获取设置页标签区宿主。
	/// </summary>
	public SettingTabPanelHost GetSettingTabPanel()
	{
		return _settingsScreenHost.GetSettingTabPanel();
	}

	/// <summary>
	/// 根据官方屏幕枚举获取对应系统区域宿主。
	/// </summary>
	/// <param name="screen">目标官方屏幕枚举。</param>
	/// <returns>对应屏幕的系统区域宿主。</returns>
	public SystemUiAreaHost GetScreen(OfficialScreenEnum screen)
	{
		return screen switch
		{
			OfficialScreenEnum.MainMenu => _mainMenuHost,
			OfficialScreenEnum.Settings => _settingsScreenHost,
			_ => throw new ArgumentOutOfRangeException(nameof(screen), screen, "Unsupported official screen enum.")
		};
	}

	/// <summary>
	/// 对所有系统 UI 区域执行重注入。
	/// </summary>
	public void ReinjectSystemAreas()
	{
		_mainMenuHost.RemountMainMenu();
		_settingsScreenHost.RemountSettings();
	}

	/// <summary>
	/// 对指定系统 UI 区域执行重注入。
	/// </summary>
	/// <param name="area">需要重注入的系统区域。</param>
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

	/// <summary>
	/// 注册指定语言的本地化词条。
	/// </summary>
	/// <param name="locale">语言代码。</param>
	/// <param name="entries">词条集合。</param>
	public void RegisterLocale(string locale, IReadOnlyDictionary<string, string> entries)
	{
		UiLocalization.RegisterLocale(locale, entries);
	}

	/// <summary>
	/// 切换当前 UI 语言。
	/// </summary>
	/// <param name="locale">语言代码。</param>
	public void SetLocale(string locale)
	{
		UiLocalization.SetLocale(locale);
	}

	/// <summary>
	/// 以语言代码别名方式切换当前 UI 语言。
	/// </summary>
	/// <param name="languageCode">语言代码。</param>
	public void SetLanguage(string languageCode)
	{
		UiLocalization.SetLocale(languageCode);
	}

	/// <summary>
	/// 按 key 查询本地化文本。
	/// </summary>
	/// <param name="key">本地化键。</param>
	/// <param name="fallback">未命中时的回退文本。</param>
	/// <returns>本地化结果文本。</returns>
	public string T(string key, string? fallback = null)
	{
		return UiLocalization.GetText(key, fallback);
	}

	/// <summary>
	/// 按表名和键查询本地化文本。
	/// </summary>
	/// <param name="table">官方本地化表名。</param>
	/// <param name="key">表内词条键。</param>
	/// <param name="fallback">未命中时的回退文本。</param>
	/// <returns>本地化结果文本。</returns>
	public string T(string table, string key, string? fallback)
	{
		return UiLocalization.GetText(null, fallback, table, key);
	}
}
