#nullable enable

using System;
using Godot;
using ReForgeFramework.UI.Runtime;

namespace ReForgeFramework.UI.Abstractions;

public abstract class UiElement : IUiElement
{
	private Control? _cachedControl;
	private readonly UiLayoutOptions _layoutOptions = new();
	private readonly UiInteractionOptions _interactionOptions = new();
	private readonly UiVisualOptions _visualOptions = new();

	protected Control? BuiltControl => _cachedControl;

	public UiElement WithHeight(float height)
	{
		_layoutOptions.Height = height;
		ReapplyIfBuilt();
		return this;
	}

	public UiElement WithMinHeight(float minHeight)
	{
		_layoutOptions.MinHeight = minHeight;
		ReapplyIfBuilt();
		return this;
	}

	public UiElement WithMaxHeight(float maxHeight)
	{
		_layoutOptions.MaxHeight = maxHeight;
		ReapplyIfBuilt();
		return this;
	}

	public UiElement WithAnchor(UiAnchorPreset preset)
	{
		_layoutOptions.AnchorPreset = preset;
		ReapplyIfBuilt();
		return this;
	}

	public UiElement WithPositionOffset(float x, float y)
	{
		return WithPositionOffset(new Vector2(x, y));
	}

	public UiElement WithPositionOffset(Vector2 offset)
	{
		_layoutOptions.PositionOffset = offset;
		ReapplyIfBuilt();
		return this;
	}

	public UiElement WithPadding(float all)
	{
		return WithPadding(UiSpacing.All(all));
	}

	public UiElement WithPadding(float horizontal, float vertical)
	{
		return WithPadding(UiSpacing.Axis(horizontal, vertical));
	}

	public UiElement WithPadding(float left, float top, float right, float bottom)
	{
		return WithPadding(new UiSpacing(left, top, right, bottom));
	}

	public UiElement WithPadding(UiSpacing spacing)
	{
		_layoutOptions.Padding = spacing;
		ReapplyIfBuilt();
		return this;
	}

	public UiElement WithMargin(float all)
	{
		return WithMargin(UiSpacing.All(all));
	}

	public UiElement WithMargin(float horizontal, float vertical)
	{
		return WithMargin(UiSpacing.Axis(horizontal, vertical));
	}

	public UiElement WithMargin(float left, float top, float right, float bottom)
	{
		return WithMargin(new UiSpacing(left, top, right, bottom));
	}

	public UiElement WithMargin(UiSpacing spacing)
	{
		_layoutOptions.Margin = spacing;
		ReapplyIfBuilt();
		return this;
	}

	public UiElement OnHoverEnter(Action<Control> handler)
	{
		ArgumentNullException.ThrowIfNull(handler);
		_interactionOptions.HoverEnterHandlers.Add(handler);
		ReapplyIfBuilt();
		return this;
	}

	public UiElement OnHoverExit(Action<Control> handler)
	{
		ArgumentNullException.ThrowIfNull(handler);
		_interactionOptions.HoverExitHandlers.Add(handler);
		ReapplyIfBuilt();
		return this;
	}

	public UiElement OnLeftMouseDown(Action<Control, InputEventMouseButton> handler)
	{
		ArgumentNullException.ThrowIfNull(handler);
		_interactionOptions.LeftMouseDownHandlers.Add(handler);
		ReapplyIfBuilt();
		return this;
	}

	public UiElement OnRightMouseDown(Action<Control, InputEventMouseButton> handler)
	{
		ArgumentNullException.ThrowIfNull(handler);
		_interactionOptions.RightMouseDownHandlers.Add(handler);
		ReapplyIfBuilt();
		return this;
	}

	public UiElement OnDrag(Action<Control, InputEventMouseMotion> handler)
	{
		ArgumentNullException.ThrowIfNull(handler);
		_interactionOptions.DragHandlers.Add(handler);
		ReapplyIfBuilt();
		return this;
	}

	public UiElement WithScale(float uniformScale)
	{
		return WithScale(new Vector2(uniformScale, uniformScale));
	}

	public UiElement WithScale(Vector2 scale)
	{
		_visualOptions.Scale = scale;
		ReapplyIfBuilt();
		return this;
	}

	public UiElement WithModulate(Color color)
	{
		_visualOptions.SelfModulate = color;
		ReapplyIfBuilt();
		return this;
	}

	/// <summary>
	/// 设置控件层级优先级（映射到 Godot 的 ZIndex / ZAsRelative）。
	/// </summary>
	public UiElement WithLayerPriority(int priority, bool relativeToParent = true)
	{
		_visualOptions.LayerPriority = priority;
		_visualOptions.LayerPriorityRelative = relativeToParent;
		ReapplyIfBuilt();
		return this;
	}

	/// <summary>
	/// 设置控件显示作用域。
	/// </summary>
	public UiElement WithScope(UiVisibilityScope scope)
	{
		_visualOptions.VisibilityScope = scope;
		ReapplyIfBuilt();
		return this;
	}

	public UiElement WithCenterPivot(bool enabled = true)
	{
		_visualOptions.CenterPivot = enabled;
		ReapplyIfBuilt();
		return this;
	}

	public UiElement WithTexture(
		Texture2D texture,
		TextureRect.StretchModeEnum stretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
		bool showBehindParent = true)
	{
		ArgumentNullException.ThrowIfNull(texture);
		_visualOptions.Texture = texture;
		_visualOptions.TextureStretchMode = stretchMode;
		_visualOptions.TextureShowBehindParent = showBehindParent;
		ReapplyIfBuilt();
		return this;
	}

	public UiElement WithTexturePath(
		string texturePath,
		TextureRect.StretchModeEnum stretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
		bool showBehindParent = true)
	{
		ArgumentNullException.ThrowIfNull(texturePath);

		if (!ResourceLoader.Exists(texturePath))
		{
			GD.Print($"[ReForge.UI] Texture path does not exist: '{texturePath}'.");
			return this;
		}

		Texture2D? texture = ResourceLoader.Load<Texture2D>(texturePath);
		if (texture == null)
		{
			GD.Print($"[ReForge.UI] Failed to load texture from path: '{texturePath}'.");
			return this;
		}

		return WithTexture(texture, stretchMode, showBehindParent);
	}

	public UiElement ClearTexture()
	{
		_visualOptions.Texture = null;
		ReapplyIfBuilt();
		return this;
	}

	// 参考官方主菜单按钮的缩放反馈，快速给任意控件附加悬浮缩放动画。
	public UiElement WithHoverScaleAnimation(float hoverScale = 1.05f, float hoverDuration = 0.05f, float unhoverDuration = 0.5f)
	{
		Vector2? baseScale = _visualOptions.Scale;

		OnHoverEnter(control =>
		{
			baseScale ??= control.Scale;
			UiTweenAnimation.TweenScale(control, baseScale.Value * hoverScale, hoverDuration, Tween.TransitionType.Cubic, Tween.EaseType.Out);
		});

		OnHoverExit(control =>
		{
			Vector2 target = baseScale ?? _visualOptions.Scale ?? Vector2.One;
			UiTweenAnimation.TweenScale(control, target, unhoverDuration, Tween.TransitionType.Expo, Tween.EaseType.Out);
		});

		return this;
	}

	public Control Build()
	{
		if (_cachedControl != null && !GodotObject.IsInstanceValid(_cachedControl))
		{
			_cachedControl = null;
		}

		_cachedControl ??= CreateControl();
		UiLayoutApplier.Apply(_cachedControl, _layoutOptions);
		UiVisualApplier.Apply(_cachedControl, _visualOptions);
		UiInteractionApplier.Apply(_cachedControl, _interactionOptions);
		return _cachedControl;
	}

	protected abstract Control CreateControl();

	private void ReapplyIfBuilt()
	{
		if (_cachedControl == null)
		{
			return;
		}

		UiLayoutApplier.Apply(_cachedControl, _layoutOptions);
		UiVisualApplier.Apply(_cachedControl, _visualOptions);
		UiInteractionApplier.Apply(_cachedControl, _interactionOptions);
	}
}
