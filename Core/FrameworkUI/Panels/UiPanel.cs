using System.Collections.Generic;
using Godot;
using ReForgeFramework.UI.Abstractions;
using ReForgeFramework.UI.Runtime;
using ReForgeFramework.UI.SystemAreas;

namespace ReForgeFramework.UI.Panels;

public abstract class UiPanel : UiElement
{
	private readonly List<IUiElement> _children = new();

	public virtual UiPanel AddChild(IUiElement child)
	{
		_children.Add(child);

		if (BuiltControl != null)
		{
			AddChildToContainer(BuiltControl, child.Build());
		}

		return this;
	}

	public void Show()
	{
		UiRuntimeNode.Ensure().MountGlobal(Build());
	}

	public void Show(SystemUiAreaHost host)
	{
		host.AddChild(this);
	}

	protected override Control CreateControl()
	{
		Control container = CreatePanelControl();
		foreach (IUiElement child in _children)
		{
			AddChildToContainer(container, child.Build());
		}

		return container;
	}

	protected IReadOnlyList<IUiElement> Children => _children;

	protected abstract Control CreatePanelControl();

	protected virtual void AddChildToContainer(Control container, Control childControl)
	{
		AttachControl(container, childControl);
	}

	protected static void AttachControl(Control container, Control childControl)
	{
		if (childControl.GetParent() == container)
		{
			UiLayoutApplier.ReapplyFromMetadata(childControl);
			return;
		}

		if (childControl.GetParent() == null)
		{
			container.AddChild(childControl);
			UiLayoutApplier.ReapplyFromMetadata(childControl);
			return;
		}

		childControl.Reparent(container, keepGlobalTransform: true);
		UiLayoutApplier.ReapplyFromMetadata(childControl);
	}
}
