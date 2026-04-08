#nullable enable

using Godot;

namespace ReForgeFramework.UI.Abstractions;

/// <summary>
/// 统一布局参数容器，避免尺寸与锚点逻辑散落在具体控件中。
/// </summary>
public sealed class UiLayoutOptions
{
	public float? Height { get; set; }

	public float? MinHeight { get; set; }

	public float? MaxHeight { get; set; }

	public UiAnchorPreset? AnchorPreset { get; set; }

	public Vector2? PositionOffset { get; set; }

	public UiSpacing? Padding { get; set; }

	public UiSpacing? Margin { get; set; }

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
