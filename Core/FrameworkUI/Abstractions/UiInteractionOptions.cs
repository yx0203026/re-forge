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
	public List<Action<Control>> HoverEnterHandlers { get; } = new();

	public List<Action<Control>> HoverExitHandlers { get; } = new();

	public List<Action<Control, InputEventMouseButton>> LeftMouseDownHandlers { get; } = new();

	public List<Action<Control, InputEventMouseButton>> RightMouseDownHandlers { get; } = new();

	public List<Action<Control, InputEventMouseMotion>> DragHandlers { get; } = new();

	public bool HasAnyHandlers =>
		HoverEnterHandlers.Count > 0
		|| HoverExitHandlers.Count > 0
		|| LeftMouseDownHandlers.Count > 0
		|| RightMouseDownHandlers.Count > 0
		|| DragHandlers.Count > 0;
}
