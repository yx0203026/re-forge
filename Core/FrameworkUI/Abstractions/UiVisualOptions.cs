#nullable enable

using Godot;

namespace ReForgeFramework.UI.Abstractions;

/// <summary>
/// 视觉参数统一入口：缩放、纹理、颜色等可复用属性。
/// </summary>
public sealed class UiVisualOptions
{
	/// <summary>
	/// 控件缩放值。
	/// </summary>
	public Vector2? Scale { get; set; }

	/// <summary>
	/// 控件调制颜色。
	/// </summary>
	public Color? SelfModulate { get; set; }

	/// <summary>
	/// 层级优先级。
	/// </summary>
	public int? LayerPriority { get; set; }

	/// <summary>
	/// 是否相对父节点层级。
	/// </summary>
	public bool LayerPriorityRelative { get; set; } = true;

	/// <summary>
	/// 可见性作用域。
	/// </summary>
	public UiVisibilityScope VisibilityScope { get; set; } = UiVisibilityScope.Always;

	/// <summary>
	/// 是否启用中心枢轴。
	/// </summary>
	public bool CenterPivot { get; set; }

	/// <summary>
	/// 附加纹理。
	/// </summary>
	public Texture2D? Texture { get; set; }

	/// <summary>
	/// 纹理拉伸模式。
	/// </summary>
	public TextureRect.StretchModeEnum TextureStretchMode { get; set; } = TextureRect.StretchModeEnum.KeepAspectCentered;

	/// <summary>
	/// 纹理是否显示在父控件后方。
	/// </summary>
	public bool TextureShowBehindParent { get; set; } = true;
}
