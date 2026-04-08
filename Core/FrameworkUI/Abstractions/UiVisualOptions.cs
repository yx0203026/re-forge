#nullable enable

using Godot;

namespace ReForgeFramework.UI.Abstractions;

/// <summary>
/// 视觉参数统一入口：缩放、纹理、颜色等可复用属性。
/// </summary>
public sealed class UiVisualOptions
{
	public Vector2? Scale { get; set; }

	public Color? SelfModulate { get; set; }

	public int? LayerPriority { get; set; }

	public bool LayerPriorityRelative { get; set; } = true;

	public UiVisibilityScope VisibilityScope { get; set; } = UiVisibilityScope.Always;

	public bool CenterPivot { get; set; }

	public Texture2D? Texture { get; set; }

	public TextureRect.StretchModeEnum TextureStretchMode { get; set; } = TextureRect.StretchModeEnum.KeepAspectCentered;

	public bool TextureShowBehindParent { get; set; } = true;
}
