#nullable enable

using Godot;
using ReForgeFramework.UI.Abstractions;

namespace ReForgeFramework.UI.Runtime;

/// <summary>
/// 将抽象布局参数映射并应用到 Godot Control。
/// </summary>
internal static class UiLayoutApplier
{
	private static readonly StringName MetaHeight = "__reforge_layout_height";
	private static readonly StringName MetaMinHeight = "__reforge_layout_min_height";
	private static readonly StringName MetaMaxHeight = "__reforge_layout_max_height";
	private static readonly StringName MetaAnchorPreset = "__reforge_layout_anchor_preset";
	private static readonly StringName MetaPositionOffset = "__reforge_layout_position_offset";
	private static readonly StringName MetaAppliedPositionOffset = "__reforge_layout_applied_position_offset";
	private static readonly StringName MetaPadding = "__reforge_layout_padding";
	private static readonly StringName MetaMargin = "__reforge_layout_margin";
	private static readonly StringName MetaAppliedMargin = "__reforge_layout_applied_margin";

	/// <summary>
	/// 应用布局配置并写入元数据。
	/// </summary>
	/// <param name="control">目标控件。</param>
	/// <param name="options">布局配置。</param>
	public static void Apply(Control control, UiLayoutOptions? options)
	{
		ApplyInternal(control, options, persistMetadata: true);
	}

	/// <summary>
	/// 从控件元数据回放布局配置。
	/// </summary>
	/// <param name="control">目标控件。</param>
	public static void ReapplyFromMetadata(Control control)
	{
		if (!control.HasMeta(MetaHeight)
			&& !control.HasMeta(MetaMinHeight)
			&& !control.HasMeta(MetaMaxHeight)
			&& !control.HasMeta(MetaAnchorPreset)
			&& !control.HasMeta(MetaPositionOffset)
			&& !control.HasMeta(MetaPadding)
			&& !control.HasMeta(MetaMargin))
		{
			return;
		}

		UiLayoutOptions options = new UiLayoutOptions
		{
			Height = control.HasMeta(MetaHeight) ? (float?)control.GetMeta(MetaHeight) : null,
			MinHeight = control.HasMeta(MetaMinHeight) ? (float?)control.GetMeta(MetaMinHeight) : null,
			MaxHeight = control.HasMeta(MetaMaxHeight) ? (float?)control.GetMeta(MetaMaxHeight) : null,
			AnchorPreset = control.HasMeta(MetaAnchorPreset)
				? (UiAnchorPreset?)(int)control.GetMeta(MetaAnchorPreset)
				: null,
			PositionOffset = control.HasMeta(MetaPositionOffset)
				? (Vector2?)((Vector2)control.GetMeta(MetaPositionOffset))
				: null,
			Padding = control.HasMeta(MetaPadding)
				? (UiSpacing?)FromVector4((Vector4)control.GetMeta(MetaPadding))
				: null,
			Margin = control.HasMeta(MetaMargin)
				? (UiSpacing?)FromVector4((Vector4)control.GetMeta(MetaMargin))
				: null
		};

		ApplyInternal(control, options, persistMetadata: false);
	}

	private static void ApplyInternal(Control control, UiLayoutOptions? options, bool persistMetadata)
	{
		UiLayoutOptions normalized = UiLayoutValidator.Normalize(options, control.Name.ToString());
		bool anchorPresetApplied = false;

		if (normalized.AnchorPreset is UiAnchorPreset anchorPreset)
		{
			anchorPresetApplied = true;
			control.SetAnchorsAndOffsetsPreset(MapPreset(anchorPreset));
		}

		Vector2 minSize = control.CustomMinimumSize;
		if (normalized.MinHeight.HasValue)
		{
			minSize.Y = normalized.MinHeight.Value;
		}

		if (normalized.Height.HasValue)
		{
			minSize.Y = normalized.Height.Value;
			control.Size = new Vector2(control.Size.X, normalized.Height.Value);
		}

		control.CustomMinimumSize = minSize;

		if (normalized.MaxHeight.HasValue && control.Size.Y > normalized.MaxHeight.Value)
		{
			control.Size = new Vector2(control.Size.X, normalized.MaxHeight.Value);
		}

		ApplyMargin(control, normalized.Margin, anchorPresetApplied);
		ApplyPositionOffset(control, normalized.PositionOffset, anchorPresetApplied);
		ApplyPadding(control, normalized.Padding);

		if (!persistMetadata)
		{
			return;
		}

		PersistMetadata(control, normalized);
	}

	private static void PersistMetadata(Control control, UiLayoutOptions options)
	{
		if (options.Height.HasValue)
		{
			control.SetMeta(MetaHeight, options.Height.Value);
		}

		if (options.MinHeight.HasValue)
		{
			control.SetMeta(MetaMinHeight, options.MinHeight.Value);
		}

		if (options.MaxHeight.HasValue)
		{
			control.SetMeta(MetaMaxHeight, options.MaxHeight.Value);
		}

		if (options.AnchorPreset.HasValue)
		{
			control.SetMeta(MetaAnchorPreset, (int)options.AnchorPreset.Value);
		}

		if (options.PositionOffset.HasValue)
		{
			control.SetMeta(MetaPositionOffset, options.PositionOffset.Value);
		}

		if (options.Padding.HasValue)
		{
			control.SetMeta(MetaPadding, ToVector4(options.Padding.Value));
		}

		if (options.Margin.HasValue)
		{
			control.SetMeta(MetaMargin, ToVector4(options.Margin.Value));
		}
	}

	private static void ApplyPositionOffset(Control control, Vector2? positionOffset, bool anchorPresetApplied)
	{
		if (!positionOffset.HasValue)
		{
			return;
		}

		Vector2 previousApplied = Vector2.Zero;
		if (!anchorPresetApplied && control.HasMeta(MetaAppliedPositionOffset))
		{
			previousApplied = (Vector2)control.GetMeta(MetaAppliedPositionOffset);
		}

		Vector2 target = positionOffset.Value;
		Vector2 delta = target - previousApplied;
		if (delta != Vector2.Zero)
		{
			control.OffsetLeft += delta.X;
			control.OffsetRight += delta.X;
			control.OffsetTop += delta.Y;
			control.OffsetBottom += delta.Y;
		}

		control.SetMeta(MetaAppliedPositionOffset, target);
	}

	private static void ApplyMargin(Control control, UiSpacing? margin, bool anchorPresetApplied)
	{
		if (!margin.HasValue)
		{
			return;
		}

		Vector4 previousApplied = Vector4.Zero;
		if (!anchorPresetApplied && control.HasMeta(MetaAppliedMargin))
		{
			previousApplied = (Vector4)control.GetMeta(MetaAppliedMargin);
		}

		Vector4 target = ToVector4(margin.Value);
		Vector4 delta = target - previousApplied;
		if (delta != Vector4.Zero)
		{
			control.OffsetLeft += delta.X;
			control.OffsetTop += delta.Y;
			control.OffsetRight -= delta.Z;
			control.OffsetBottom -= delta.W;
		}

		control.SetMeta(MetaAppliedMargin, target);
	}

	private static void ApplyPadding(Control control, UiSpacing? padding)
	{
		if (!padding.HasValue)
		{
			return;
		}

		UiSpacing value = padding.Value;
		int left = Mathf.RoundToInt(value.Left);
		int top = Mathf.RoundToInt(value.Top);
		int right = Mathf.RoundToInt(value.Right);
		int bottom = Mathf.RoundToInt(value.Bottom);

		// 统一写入两组常见常量名，提高不同 Godot 控件的兼容命中率。
		control.AddThemeConstantOverride("margin_left", left);
		control.AddThemeConstantOverride("margin_top", top);
		control.AddThemeConstantOverride("margin_right", right);
		control.AddThemeConstantOverride("margin_bottom", bottom);

		control.AddThemeConstantOverride("content_margin_left", left);
		control.AddThemeConstantOverride("content_margin_top", top);
		control.AddThemeConstantOverride("content_margin_right", right);
		control.AddThemeConstantOverride("content_margin_bottom", bottom);
	}

	private static Vector4 ToVector4(UiSpacing spacing)
	{
		return new Vector4(spacing.Left, spacing.Top, spacing.Right, spacing.Bottom);
	}

	private static UiSpacing FromVector4(Vector4 spacing)
	{
		return new UiSpacing(spacing.X, spacing.Y, spacing.Z, spacing.W);
	}

	private static Control.LayoutPreset MapPreset(UiAnchorPreset preset)
	{
		return preset switch
		{
			UiAnchorPreset.TopLeft => Control.LayoutPreset.TopLeft,
			UiAnchorPreset.TopCenter => Control.LayoutPreset.CenterTop,
			UiAnchorPreset.TopRight => Control.LayoutPreset.TopRight,
			UiAnchorPreset.CenterLeft => Control.LayoutPreset.CenterLeft,
			UiAnchorPreset.Center => Control.LayoutPreset.Center,
			UiAnchorPreset.CenterRight => Control.LayoutPreset.CenterRight,
			UiAnchorPreset.BottomLeft => Control.LayoutPreset.BottomLeft,
			UiAnchorPreset.BottomCenter => Control.LayoutPreset.CenterBottom,
			UiAnchorPreset.BottomRight => Control.LayoutPreset.BottomRight,
			UiAnchorPreset.Stretch => Control.LayoutPreset.FullRect,
			_ => Control.LayoutPreset.TopLeft
		};
	}
}
