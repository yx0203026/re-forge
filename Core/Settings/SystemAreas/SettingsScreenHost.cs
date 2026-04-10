namespace ReForgeFramework.Settings.SystemAreas;

/// <summary>
/// 设置屏幕宿主：聚合设置页注入能力。
/// </summary>
public sealed class SettingsScreenHost
{
	private readonly SettingTabPanelHost _settingTabPanelHost = new();

	/// <summary>
	/// 获取设置页 Tab Panel 的宿主。
	/// </summary>
	public SettingTabPanelHost GetSettingTabPanel()
	{
		return _settingTabPanelHost;
	}

	internal void RemountSettings()
	{
		_settingTabPanelHost.RemountAll();
	}
}

