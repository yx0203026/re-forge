#nullable enable

using Godot;

namespace ReForgeFramework.UI.Abstractions;

/// <summary>
/// 统一布局参数容器，避免尺寸与锚点逻辑散落在具体控件中。
/// </summary>
public sealed class UiLayoutOptions
{
	/// <summary>
	/// 期望高度。
	/// </summary>
	public float? Height { get; set; }

	/// <summary>
	/// 最小高度。
	/// </summary>
	public float? MinHeight { get; set; }

	/// <summary>
	/// 最大高度。
	/// </summary>
	public float? MaxHeight { get; set; }

	/// <summary>
	/// 锚点预设。
	/// </summary>
	public UiAnchorPreset? AnchorPreset { get; set; }

	/// <summary>
	/// 偏移量。
	/// </summary>
	public Vector2? PositionOffset { get; set; }

	/// <summary>
	/// 内边距。
	/// </summary>
	public UiSpacing? Padding { get; set; }

	/// <summary>
	/// 外边距。
	/// </summary>
	public UiSpacing? Margin { get; set; }

	/// <summary>
	/// 复制当前布局配置。
	/// </summary>
	/// <returns>新的布局配置实例。</returns>
	public UiLayoutOptions Clone()
	{
		return new UiLayoutOptions
		{
			Height = Height,
			MinHeight = MinHeight,
			MaxHeight = MaxHeight,
			AnchorPreset = AnchorPreset,
			PositionOffset = PositionOffset,
			Padding = Padding,
			Margin = Margin
		};
	}
}
