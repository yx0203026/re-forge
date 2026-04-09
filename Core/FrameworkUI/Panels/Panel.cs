using Godot;

namespace ReForgeFramework.UI.Panels;

/// <summary>
/// 垂直面板（VBox）封装。
/// </summary>
public class Panel : UiPanel
{
	private readonly int _spacing;

	/// <summary>
	/// 初始化垂直面板。
	/// </summary>
	/// <param name="spacing">子元素间距。</param>
	public Panel(int spacing = 8)
	{
		_spacing = spacing;
	}

	protected override Control CreatePanelControl()
	{
		VBoxContainer container = new VBoxContainer
		{
			Name = "ReForgePanel"
		};

		container.AddThemeConstantOverride("separation", _spacing);
		return container;
	}
}
