using System.Collections.Generic;
using Godot;
using ReForgeFramework.UI.Abstractions;
using ReForgeFramework.UI.Runtime;
using ReForgeFramework.UI.SystemAreas;

namespace ReForgeFramework.UI.Panels;

/// <summary>
/// 面板基类，管理子元素集合与挂载行为。
/// </summary>
public abstract class UiPanel : UiElement
{
	private readonly List<IUiElement> _children = new();

	/// <summary>
	/// 添加一个子元素。
	/// </summary>
	/// <param name="child">子元素。</param>
	/// <returns>当前面板实例。</returns>
	public virtual UiPanel AddChild(IUiElement child)
	{
		_children.Add(child);

		if (BuiltControl != null)
		{
			AddChildToContainer(BuiltControl, child.Build());
		}

		return this;
	}

	/// <summary>
	/// 将面板挂载到全局 UI 层。
	/// </summary>
	public void Show()
	{
		UiRuntimeNode.Ensure().MountGlobal(Build());
	}

	/// <summary>
	/// 将面板挂载到指定系统区域宿主。
	/// </summary>
	/// <param name="host">系统区域宿主。</param>
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
