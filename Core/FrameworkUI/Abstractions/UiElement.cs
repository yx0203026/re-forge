#nullable enable

using System;
using Godot;
using ReForgeFramework.UI.Runtime;

namespace ReForgeFramework.UI.Abstractions;

/// <summary>
/// 基础 UI 元素构建基类，提供链式调用的布局、交互与视觉外观配置接口。
/// </summary>
public abstract class UiElement : IUiElement
{
	private Control? _cachedControl;
	private readonly UiLayoutOptions _layoutOptions = new();
	private readonly UiInteractionOptions _interactionOptions = new();
	private readonly UiVisualOptions _visualOptions = new();

	protected Control? BuiltControl => _cachedControl;

	/// <summary>
/// 设置 UI 元素的高度。
/// </summary>
/// <param name="height">目标高度值。</param>
/// <returns>当前 UI 元素的链式实例。</returns>
public UiElement WithHeight(float height)
	{
		_layoutOptions.Height = height;
		ReapplyIfBuilt();
		return this;
	}

	/// <summary>
/// 设置 UI 元素的最小高度。
/// </summary>
/// <param name="minHeight">最小高度值。</param>
/// <returns>当前 UI 元素的链式实例。</returns>
public UiElement WithMinHeight(float minHeight)
	{
		_layoutOptions.MinHeight = minHeight;
		ReapplyIfBuilt();
		return this;
	}

	/// <summary>
/// 设置 UI 元素的最大高度。
/// </summary>
/// <param name="maxHeight">最大高度值。</param>
/// <returns>当前 UI 元素的链式实例。</returns>
public UiElement WithMaxHeight(float maxHeight)
	{
		_layoutOptions.MaxHeight = maxHeight;
		ReapplyIfBuilt();
		return this;
	}

	/// <summary>
/// 设置 UI 元素的锚点预设类型。
/// </summary>
/// <param name="preset">锚点预设类型（例如居中、填满等）。</param>
/// <returns>当前 UI 元素的链式实例。</returns>
public UiElement WithAnchor(UiAnchorPreset preset)
	{
		_layoutOptions.AnchorPreset = preset;
		ReapplyIfBuilt();
		return this;
	}

	/// <summary>
/// 设置 UI 元素的坐标偏移。
/// </summary>
/// <param name="x">X轴偏移量。</param>
/// <param name="y">Y轴偏移量。</param>
/// <returns>当前 UI 元素的链式实例。</returns>
public UiElement WithPositionOffset(float x, float y)
	{
		return WithPositionOffset(new Vector2(x, y));
	}

	/// <summary>
/// 根据给定的二维向量设置 UI 元素的坐标偏移。
/// </summary>
/// <param name="offset">二维向量表示的偏移量。</param>
/// <returns>当前 UI 元素的链式实例。</returns>
public UiElement WithPositionOffset(Vector2 offset)
	{
		_layoutOptions.PositionOffset = offset;
		ReapplyIfBuilt();
		return this;
	}

	/// <summary>
/// 为所有边缘设置相同的内边距。
/// </summary>
/// <param name="all">四周内边距值。</param>
/// <returns>当前 UI 元素的链式实例。</returns>
public UiElement WithPadding(float all)
	{
		return WithPadding(UiSpacing.All(all));
	}

	/// <summary>
/// 分别为水平和垂直方向设置内边距。
/// </summary>
/// <param name="horizontal">水平端内边距。</param>
/// <param name="vertical">垂直端内边距。</param>
/// <returns>当前 UI 元素的链式实例。</returns>
public UiElement WithPadding(float horizontal, float vertical)
	{
		return WithPadding(UiSpacing.Axis(horizontal, vertical));
	}

	/// <summary>
/// 分别为四个边缘设置内边距。
/// </summary>
/// <param name="left">左侧内边距。</param>
/// <param name="top">顶部内边距。</param>
/// <param name="right">右侧内边距。</param>
/// <param name="bottom">底部内边距。</param>
/// <returns>当前 UI 元素的链式实例。</returns>
public UiElement WithPadding(float left, float top, float right, float bottom)
	{
		return WithPadding(new UiSpacing(left, top, right, bottom));
	}

	/// <summary>
/// 根据边距对象设置内边距。
/// </summary>
/// <param name="spacing">表示内边距属性的对象。</param>
/// <returns>当前 UI 元素的链式实例。</returns>
public UiElement WithPadding(UiSpacing spacing)
	{
		_layoutOptions.Padding = spacing;
		ReapplyIfBuilt();
		return this;
	}

	/// <summary>
/// 为所有边缘设置相同的外边距。
/// </summary>
/// <param name="all">四周外边距值。</param>
/// <returns>当前 UI 元素的链式实例。</returns>
public UiElement WithMargin(float all)
	{
		return WithMargin(UiSpacing.All(all));
	}

	/// <summary>
	/// 分别设置水平方向与垂直方向外边距。
	/// </summary>
	/// <param name="horizontal">水平外边距。</param>
	/// <param name="vertical">垂直外边距。</param>
	/// <returns>当前 UI 元素的链式实例。</returns>
	public UiElement WithMargin(float horizontal, float vertical)
	{
		return WithMargin(UiSpacing.Axis(horizontal, vertical));
	}

	/// <summary>
	/// 分别设置四个方向外边距。
	/// </summary>
	/// <param name="left">左外边距。</param>
	/// <param name="top">上外边距。</param>
	/// <param name="right">右外边距。</param>
	/// <param name="bottom">下外边距。</param>
	/// <returns>当前 UI 元素的链式实例。</returns>
	public UiElement WithMargin(float left, float top, float right, float bottom)
	{
		return WithMargin(new UiSpacing(left, top, right, bottom));
	}

	/// <summary>
	/// 使用边距对象设置外边距。
	/// </summary>
	/// <param name="spacing">外边距对象。</param>
	/// <returns>当前 UI 元素的链式实例。</returns>
	public UiElement WithMargin(UiSpacing spacing)
	{
		_layoutOptions.Margin = spacing;
		ReapplyIfBuilt();
		return this;
	}

	/// <summary>
/// 附加鼠标悬浮进入事件处理器。
/// </summary>
/// <param name="handler">包含当前绑定控件作为参数的回调操作。</param>
/// <returns>当前 UI 元素的链式实例。</returns>
public UiElement OnHoverEnter(Action<Control> handler)
	{
		ArgumentNullException.ThrowIfNull(handler);
		_interactionOptions.HoverEnterHandlers.Add(handler);
		ReapplyIfBuilt();
		return this;
	}

	/// <summary>
/// 附加鼠标悬浮离开事件处理器。
/// </summary>
/// <param name="handler">包含当前绑定控件作为参数的回调操作。</param>
/// <returns>当前 UI 元素的链式实例。</returns>
public UiElement OnHoverExit(Action<Control> handler)
	{
		ArgumentNullException.ThrowIfNull(handler);
		_interactionOptions.HoverExitHandlers.Add(handler);
		ReapplyIfBuilt();
		return this;
	}

	/// <summary>
	/// 绑定左键按下回调。
	/// </summary>
	/// <param name="handler">左键按下处理器。</param>
	/// <returns>当前 UI 元素的链式实例。</returns>
	public UiElement OnLeftMouseDown(Action<Control, InputEventMouseButton> handler)
	{
		ArgumentNullException.ThrowIfNull(handler);
		_interactionOptions.LeftMouseDownHandlers.Add(handler);
		ReapplyIfBuilt();
		return this;
	}

	/// <summary>
	/// 绑定右键按下回调。
	/// </summary>
	/// <param name="handler">右键按下处理器。</param>
	/// <returns>当前 UI 元素的链式实例。</returns>
	public UiElement OnRightMouseDown(Action<Control, InputEventMouseButton> handler)
	{
		ArgumentNullException.ThrowIfNull(handler);
		_interactionOptions.RightMouseDownHandlers.Add(handler);
		ReapplyIfBuilt();
		return this;
	}

	/// <summary>
	/// 绑定鼠标拖拽回调。
	/// </summary>
	/// <param name="handler">拖拽事件处理器。</param>
	/// <returns>当前 UI 元素的链式实例。</returns>
	public UiElement OnDrag(Action<Control, InputEventMouseMotion> handler)
	{
		ArgumentNullException.ThrowIfNull(handler);
		_interactionOptions.DragHandlers.Add(handler);
		ReapplyIfBuilt();
		return this;
	}

	/// <summary>
	/// 以统一倍率设置控件缩放。
	/// </summary>
	/// <param name="uniformScale">统一缩放倍率。</param>
	/// <returns>当前 UI 元素的链式实例。</returns>
	public UiElement WithScale(float uniformScale)
	{
		return WithScale(new Vector2(uniformScale, uniformScale));
	}

	/// <summary>
	/// 按二维向量设置控件缩放。
	/// </summary>
	/// <param name="scale">缩放向量。</param>
	/// <returns>当前 UI 元素的链式实例。</returns>
	public UiElement WithScale(Vector2 scale)
	{
		_visualOptions.Scale = scale;
		ReapplyIfBuilt();
		return this;
	}

	/// <summary>
	/// 设置控件调制颜色。
	/// </summary>
	/// <param name="color">调制颜色。</param>
	/// <returns>当前 UI 元素的链式实例。</returns>
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

	/// <summary>
	/// 启用或关闭中心点枢轴。
	/// </summary>
	/// <param name="enabled">是否启用。</param>
	/// <returns>当前 UI 元素的链式实例。</returns>
	public UiElement WithCenterPivot(bool enabled = true)
	{
		_visualOptions.CenterPivot = enabled;
		ReapplyIfBuilt();
		return this;
	}

	/// <summary>
	/// 设置纹理及其显示参数。
	/// </summary>
	/// <param name="texture">纹理对象。</param>
	/// <param name="stretchMode">纹理拉伸模式。</param>
	/// <param name="showBehindParent">是否绘制在父控件后方。</param>
	/// <returns>当前 UI 元素的链式实例。</returns>
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

	/// <summary>
	/// 通过资源路径加载并设置纹理。
	/// </summary>
	/// <param name="texturePath">纹理资源路径。</param>
	/// <param name="stretchMode">纹理拉伸模式。</param>
	/// <param name="showBehindParent">是否绘制在父控件后方。</param>
	/// <returns>当前 UI 元素的链式实例。</returns>
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

	/// <summary>
	/// 清除当前控件纹理配置。
	/// </summary>
	/// <returns>当前 UI 元素的链式实例。</returns>
	public UiElement ClearTexture()
	{
		_visualOptions.Texture = null;
		ReapplyIfBuilt();
		return this;
	}

	// 参考官方主菜单按钮的缩放反馈，快速给任意控件附加悬浮缩放动画。
	/// <summary>
/// 为当前控件添加鼠标悬浮时的缩放动画反馈。
/// </summary>
/// <param name="hoverScale">悬浮时的缩放倍率，默认为 1.05f。</param>
/// <param name="hoverDuration">悬浮动画的过渡时间，默认为 0.05 秒。</param>
/// <param name="unhoverDuration">离开悬浮时恢复动画的过渡时间，默认为 0.5 秒。</param>
/// <returns>当前 UI 元素的链式实例。</returns>
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

	/// <summary>
/// 构建并应用对应的 Godot 控件实例。<br/>
/// 如果控件已经建立过了，则只应用之前链式调用所带来的配置变更。
/// </summary>
/// <returns>构建后的目标 Control 节点。</returns>
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
