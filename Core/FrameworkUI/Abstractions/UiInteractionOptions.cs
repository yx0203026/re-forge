#nullable enable

using System;
using System.Collections.Generic;
using Godot;

namespace ReForgeFramework.UI.Abstractions;

/// <summary>
/// 统一收口 UI 交互事件，便于在 UiElement 基类一次性绑定。
/// </summary>
public sealed class UiInteractionOptions
{
	/// <summary>
	/// 鼠标进入控件区域时触发的处理器集合。
	/// </summary>
	public List<Action<Control>> HoverEnterHandlers { get; } = new();

	/// <summary>
	/// 鼠标离开控件区域时触发的处理器集合。
	/// </summary>
	public List<Action<Control>> HoverExitHandlers { get; } = new();

	/// <summary>
	/// 鼠标左键按下时触发的处理器集合。
	/// </summary>
	public List<Action<Control, InputEventMouseButton>> LeftMouseDownHandlers { get; } = new();

	/// <summary>
	/// 鼠标右键按下时触发的处理器集合。
	/// </summary>
	public List<Action<Control, InputEventMouseButton>> RightMouseDownHandlers { get; } = new();

	/// <summary>
	/// 鼠标拖拽时触发的处理器集合。
	/// </summary>
	public List<Action<Control, InputEventMouseMotion>> DragHandlers { get; } = new();

	/// <summary>
	/// 是否存在任意交互处理器。
	/// </summary>
	public bool HasAnyHandlers =>
		HoverEnterHandlers.Count > 0
		|| HoverExitHandlers.Count > 0
		|| LeftMouseDownHandlers.Count > 0
		|| RightMouseDownHandlers.Count > 0
		|| DragHandlers.Count > 0;
}
