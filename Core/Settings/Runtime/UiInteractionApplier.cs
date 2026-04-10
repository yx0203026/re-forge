#nullable enable

using Godot;
using ReForgeFramework.Settings.Abstractions;

namespace ReForgeFramework.Settings.Runtime;

/// <summary>
/// 灏?UiElement 涓婂０鏄庣殑浜や簰琛屼负缁戝畾鍒?Control 淇″彿銆?
/// </summary>
internal static class UiInteractionApplier
{
	private static readonly StringName MetaInteractionBound = "__reforge_interaction_bound";

	private sealed class InteractionState
	{
		/// <summary>
		/// 宸﹂敭鏄惁鎸変笅銆?
		/// </summary>
		public bool LeftPressed { get; set; }

		/// <summary>
		/// 鍙抽敭鏄惁鎸変笅銆?
		/// </summary>
		public bool RightPressed { get; set; }

		/// <summary>
		/// 鏄惁澶勪簬鎷栨嫿鐘舵€併€?
		/// </summary>
		public bool IsDragging => LeftPressed || RightPressed;
	}

	/// <summary>
	/// 灏嗕氦浜掗€夐」缁戝畾鍒扮洰鏍囨帶浠躲€?
	/// </summary>
	/// <param name="control">鐩爣鎺т欢銆?/param>
	/// <param name="options">浜や簰閰嶇疆銆?/param>
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

