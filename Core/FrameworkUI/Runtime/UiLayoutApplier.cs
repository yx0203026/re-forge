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

	public static void Apply(Control control, UiLayoutOptions? options)
	{
		ApplyInternal(control, options, persistMetadata: true);
	}

	public static void ReapplyFromMetadata(Control control)
	{
		if (!control.HasMeta(MetaHeight) && !control.HasMeta(MetaMinHeight) && !control.HasMeta(MetaMaxHeight) && !control.HasMeta(MetaAnchorPreset))
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
				: null
		};

		ApplyInternal(control, options, persistMetadata: false);
	}

	private static void ApplyInternal(Control control, UiLayoutOptions? options, bool persistMetadata)
	{
		UiLayoutOptions normalized = UiLayoutValidator.Normalize(options, control.Name.ToString());

		if (normalized.AnchorPreset.HasValue)
		{
			control.SetAnchorsAndOffsetsPreset(MapPreset(normalized.AnchorPreset.Value));
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
