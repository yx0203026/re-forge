namespace ReForgeFramework.UI.SystemAreas;

/// <summary>
/// 设置屏幕宿主：集中承载设置区域与设置 Tab 相关入口。
/// </summary>
public sealed class SettingsScreenHost : SystemUiAreaHost
{
	private readonly SettingTabPanelHost _settingTabPanelHost = new();

	internal SettingsScreenHost()
		: base(SystemUiArea.SettingScreen)
	{
	}

	/// <summary>
	/// 获取设置屏幕的默认 Panel。
	/// </summary>
	public SettingTabPanelHost GetDefaultPanel()
	{
		return _settingTabPanelHost;
	}

	/// <summary>
	/// 获取设置页 Tab 区域宿主（用于官方屏幕路由）。
	/// </summary>
	public SettingTabPanelHost GetSettingTabPanelArea()
	{
		return GetDefaultPanel();
	}

	/// <summary>
	/// 获取设置页 Tab Panel 的宿主。
	/// </summary>
	public SettingTabPanelHost GetSettingTabPanel()
	{
		return _settingTabPanelHost;
	}

	/// <summary>
	/// 统一重挂载设置屏幕能力（基础区域 + Tab Panel）。
	/// </summary>
	internal void RemountSettings()
	{
		RemountAll();
		_settingTabPanelHost.RemountAll();
	}
}
