using Godot;

namespace ReForgeFramework.UI.Panels;

/// <summary>
/// 堆叠面板，支持横向或纵向布局。
/// </summary>
public class StackPanel : UiPanel
{
	private readonly bool _horizontal;
	private readonly int _spacing;

	/// <summary>
	/// 初始化堆叠面板。
	/// </summary>
	/// <param name="horizontal">是否横向布局。</param>
	/// <param name="spacing">子元素间距。</param>
	public StackPanel(bool horizontal = false, int spacing = 8)
	{
		_horizontal = horizontal;
		_spacing = spacing;
	}

	protected override Control CreatePanelControl()
	{
		BoxContainer container = _horizontal ? new HBoxContainer() : new VBoxContainer();
		container.Name = "ReForgeStackPanel";
		container.AddThemeConstantOverride("separation", _spacing);
		return container;
	}
}
