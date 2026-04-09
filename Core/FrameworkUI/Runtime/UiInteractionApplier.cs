#nullable enable

using Godot;
using ReForgeFramework.UI.Abstractions;

namespace ReForgeFramework.UI.Runtime;

/// <summary>
/// 将 UiElement 上声明的交互行为绑定到 Control 信号。
/// </summary>
internal static class UiInteractionApplier
{
	private static readonly StringName MetaInteractionBound = "__reforge_interaction_bound";

	private sealed class InteractionState
	{
		/// <summary>
		/// 左键是否按下。
		/// </summary>
		public bool LeftPressed { get; set; }

		/// <summary>
		/// 右键是否按下。
		/// </summary>
		public bool RightPressed { get; set; }

		/// <summary>
		/// 是否处于拖拽状态。
		/// </summary>
		public bool IsDragging => LeftPressed || RightPressed;
	}

	/// <summary>
	/// 将交互选项绑定到目标控件。
	/// </summary>
	/// <param name="control">目标控件。</param>
	/// <param name="options">交互配置。</param>
	public static void Apply(Control control, UiInteractionOptions options)
	{
		if (!options.HasAnyHandlers)
		{
			return;
		}

		if (control.MouseFilter == Control.MouseFilterEnum.Ignore)
		{
			control.MouseFilter = Control.MouseFilterEnum.Pass;
		}

		if (control.HasMeta(MetaInteractionBound))
		{
			return;
		}

		InteractionState state = new();

		control.Connect(Control.SignalName.MouseEntered, Callable.From(() => InvokeHoverEnter(control, options)));
		control.Connect(Control.SignalName.MouseExited, Callable.From(() => InvokeHoverExit(control, options)));
		control.Connect(Control.SignalName.GuiInput, Callable.From<InputEvent>(inputEvent => HandleGuiInput(control, options, state, inputEvent)));

		control.SetMeta(MetaInteractionBound, true);
	}

	private static void HandleGuiInput(Control control, UiInteractionOptions options, InteractionState state, InputEvent inputEvent)
	{
		if (inputEvent is InputEventMouseButton mouseButton)
		{
			HandleMouseButton(control, options, state, mouseButton);
			return;
		}

		if (inputEvent is InputEventMouseMotion mouseMotion && state.IsDragging)
		{
			foreach (var handler in options.DragHandlers)
			{
				handler(control, mouseMotion);
			}
		}
	}

	private static void HandleMouseButton(Control control, UiInteractionOptions options, InteractionState state, InputEventMouseButton mouseButton)
	{
		bool isPressed = mouseButton.IsPressed();
		switch (mouseButton.ButtonIndex)
		{
			case MouseButton.Left:
				state.LeftPressed = isPressed;
				if (isPressed)
				{
					foreach (var handler in options.LeftMouseDownHandlers)
					{
						handler(control, mouseButton);
					}
				}
				break;
			case MouseButton.Right:
				state.RightPressed = isPressed;
				if (isPressed)
				{
					foreach (var handler in options.RightMouseDownHandlers)
					{
						handler(control, mouseButton);
					}
				}
				break;
		}
	}

	private static void InvokeHoverEnter(Control control, UiInteractionOptions options)
	{
		foreach (var handler in options.HoverEnterHandlers)
		{
			handler(control);
		}
	}

	private static void InvokeHoverExit(Control control, UiInteractionOptions options)
	{
		foreach (var handler in options.HoverExitHandlers)
		{
			handler(control);
		}
	}
}
