using Godot;

namespace ReForgeFramework.UI.Panels;

public class Panel : UiPanel
{
	private readonly int _spacing;

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
