namespace ReForgeFramework.UI.SystemAreas;

/// <summary>
/// 主菜单按钮区宿主：封装 MainMenuButtonPanel 区域的挂载能力。
/// </summary>
public sealed class MainMenuButtonPanelHost : SystemUiAreaHost
{
	internal MainMenuButtonPanelHost()
		: base(SystemUiArea.MainMenuButtonPanel)
	{
	}
}