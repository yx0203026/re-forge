using Godot;

namespace ReForgeFramework.UI.Panels;

public class StackPanel : UiPanel
{
	private readonly bool _horizontal;
	private readonly int _spacing;

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
