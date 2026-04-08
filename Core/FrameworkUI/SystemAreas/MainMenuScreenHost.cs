using ReForgeFramework.UI.Abstractions;

namespace ReForgeFramework.UI.SystemAreas;

/// <summary>
/// 主菜单屏幕宿主：集中承载主菜单按钮区相关入口。
/// </summary>
public sealed class MainMenuScreenHost : SystemUiAreaHost
{
	private readonly MainMenuButtonPanelHost _mainMenuButtonPanelHost = new();

	internal MainMenuScreenHost()
		: base(SystemUiArea.MainMenuButtonPanel)
	{
	}

	/// <summary>
	/// 获取主菜单屏幕的默认 Panel。
	/// </summary>
	public MainMenuButtonPanelHost GetDefaultPanel()
	{
		return _mainMenuButtonPanelHost;
	}

	/// <summary>
	/// 获取主菜单按钮区 Panel 的注入宿主。
	/// </summary>
	public MainMenuButtonPanelHost GetMainMenuButtonPanel()
	{
		return GetDefaultPanel();
	}

	/// <summary>
	/// 语义化别名，便于调用方按屏幕上下文理解 API。
	/// </summary>
	public void AddToMainMenu(IUiElement element)
	{
		_mainMenuButtonPanelHost.AddChild(element);
	}

	/// <summary>
	/// 统一重挂载主菜单按钮区。
	/// </summary>
	internal void RemountMainMenu()
	{
		RemountAll();
		_mainMenuButtonPanelHost.RemountAll();
	}
}
