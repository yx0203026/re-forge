#nullable enable

using System.Collections.Generic;
using Godot;
using ReForgeFramework.Settings.Abstractions;

namespace ReForgeFramework.Settings.Runtime;

/// <summary>
/// 灏嗘娊璞″竷灞€鍙傛暟鏄犲皠骞跺簲鐢ㄥ埌 Godot Control銆?
/// </summary>
internal static class UiLayoutApplier
{
	private static readonly Dictionary<ulong, UiLayoutState> LayoutStates = new();

	/// <summary>
	/// 搴旂敤甯冨眬閰嶇疆骞跺啓鍏ュ唴閮ㄧ姸鎬併€?
	/// </summary>
	/// <param name="control">鐩爣鎺т欢銆?/param>
	/// <param name="options">甯冨眬閰嶇疆銆?/param>
	public static void Apply(Control control, UiLayoutOptions? options)
	{
		UiLayoutState state = EnsureState(control);
		state.Options = UiLayoutValidator.Normalize(options, control.Name.ToString()).Clone();
		ApplyFromState(control, state, bindResizeWatcher: true);
	}

	/// <summary>
	/// 浠庢帶浠跺唴閮ㄧ姸鎬佸洖鏀惧竷灞€閰嶇疆銆?
	/// </summary>
	/// <param name="control">鐩爣鎺т欢銆?/param>
	public static void ReapplyFromMetadata(Control control)
	{
		UiLayoutState? state = GetState(control);
		if (state == null)
		{
			return;
		}

		if (state.Options == null)
		{
			return;
		}

		ApplyFromState(control, state, bindResizeWatcher: false);
	}

	/// <summary>
	/// 澶嶅埗鎺у埗鐨勫竷灞€鍩虹嚎涓庡父瑙勫竷灞€灞炴€с€?
	/// </summary>
	/// <param name="source">婧愭帶浠躲€?/param>
	/// <param name="target">鐩爣鎺т欢銆?/param>
	internal static void CopyLayout(Control source, Control target)
	{
		ControlLayoutSnapshot.Capture(source).ApplyTo(target);
	}

	private static UiLayoutState EnsureState(Control control)
	{
		UiLayoutState? state = GetState(control);
		if (state != null)
		{
			return state;
		}

		ulong instanceId = control.GetInstanceId();
		state = new UiLayoutState
		{
			BaseLayout = ControlLayoutSnapshot.Capture(control)
		};
		LayoutStates[instanceId] = state;
		control.TreeExiting += () => LayoutStates.Remove(instanceId);
		return state;
	}

	private static UiLayoutState? GetState(Control control)
	{
		return LayoutStates.TryGetValue(control.GetInstanceId(), out UiLayoutState? state)
			? state
			: null;
	}

	private static void ApplyFromState(Control control, UiLayoutState state, bool bindResizeWatcher)
	{
		if (state.IsApplying)
		{
			return;
		}

		try
		{
			state.IsApplying = true;

			if (state.BaseLayout is not ControlLayoutSnapshot baseLayout)
			{
				baseLayout = ControlLayoutSnapshot.Capture(control);
				state.BaseLayout = baseLayout;
			}

			baseLayout.ApplyTo(control);

			UiLayoutOptions options = state.Options ?? new UiLayoutOptions();
			if (options.AnchorPreset is UiAnchorPreset anchorPreset)
			{
				control.SetAnchorsAndOffsetsPreset(MapPreset(anchorPreset));
			}

			ApplySize(control, options);
			ApplyMargin(control, options.Margin);
			ApplyPositionOffset(control, options.PositionOffset);
			ApplyPositionAnchorShift(control, options.PositionAnchorPreset);
			ApplyPadding(control, options.Padding);

			if (bindResizeWatcher && !state.ResizeRefreshBound && RequiresResizeRefresh(options))
			{
				state.ResizeRefreshBound = true;
				control.Resized += () => RefreshLayoutOnResize(control);
			}
		}
		finally
		{
			state.IsApplying = false;
		}
	}

	private static void RefreshLayoutOnResize(Control control)
	{
		if (!GodotObject.IsInstanceValid(control))
		{
			return;
		}

		UiLayoutState? state = GetState(control);
		if (state == null)
		{
			return;
		}

		if (state.Options == null)
		{
			return;
		}

		ApplyFromState(control, state, bindResizeWatcher: false);
	}

	private static bool RequiresResizeRefresh(UiLayoutOptions options)
	{
		return options.Width.HasValue
			|| options.MinWidth.HasValue
			|| options.MaxWidth.HasValue
			|| options.Height.HasValue
			|| options.MinHeight.HasValue
			|| options.MaxHeight.HasValue
			|| options.PositionAnchorPreset.HasValue;
	}

	private static void ApplySize(Control control, UiLayoutOptions options)
	{
		Vector2 minSize = control.CustomMinimumSize;

		if (options.MinWidth.HasValue)
		{
			minSize.X = options.MinWidth.Value;
		}

		if (options.Width.HasValue)
		{
			minSize.X = options.Width.Value;
			control.Size = new Vector2(options.Width.Value, control.Size.Y);
		}

		if (options.MaxWidth.HasValue && minSize.X > options.MaxWidth.Value)
		{
			minSize.X = options.MaxWidth.Value;
		}

		if (options.MinHeight.HasValue)
		{
			minSize.Y = options.MinHeight.Value;
		}

		if (options.Height.HasValue)
		{
			minSize.Y = options.Height.Value;
			control.Size = new Vector2(control.Size.X, options.Height.Value);
		}

		if (options.MaxHeight.HasValue && minSize.Y > options.MaxHeight.Value)
		{
			minSize.Y = options.MaxHeight.Value;
		}

		control.CustomMinimumSize = minSize;

		if (options.MaxWidth.HasValue && control.Size.X > options.MaxWidth.Value)
		{
			control.Size = new Vector2(options.MaxWidth.Value, control.Size.Y);
		}

		if (options.MaxHeight.HasValue && control.Size.Y > options.MaxHeight.Value)
		{
			control.Size = new Vector2(control.Size.X, options.MaxHeight.Value);
		}
	}

	private static void ApplyPositionOffset(Control control, Vector2? positionOffset)
	{
		if (!positionOffset.HasValue)
		{
			return;
		}

		Vector2 offset = positionOffset.Value;
		control.OffsetLeft += offset.X;
		control.OffsetRight += offset.X;
		control.OffsetTop += offset.Y;
		control.OffsetBottom += offset.Y;
	}

	private static void ApplyPositionAnchorShift(Control control, UiPivotAnchorPreset? positionAnchorPreset)
	{
		if (!positionAnchorPreset.HasValue)
		{
			return;
		}

		Vector2 shift = CalculatePositionAnchorShift(ResolvePositionAnchorSize(control), positionAnchorPreset.Value);
		control.OffsetLeft += shift.X;
		control.OffsetRight += shift.X;
		control.OffsetTop += shift.Y;
		control.OffsetBottom += shift.Y;
	}

	private static void ApplyMargin(Control control, UiSpacing? margin)
	{
		if (!margin.HasValue)
		{
			return;
		}

		UiSpacing value = margin.Value;
		control.OffsetLeft += value.Left;
		control.OffsetTop += value.Top;
		control.OffsetRight -= value.Right;
		control.OffsetBottom -= value.Bottom;
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

		control.AddThemeConstantOverride("margin_left", left);
		control.AddThemeConstantOverride("margin_top", top);
		control.AddThemeConstantOverride("margin_right", right);
		control.AddThemeConstantOverride("margin_bottom", bottom);

		control.AddThemeConstantOverride("content_margin_left", left);
		control.AddThemeConstantOverride("content_margin_top", top);
		control.AddThemeConstantOverride("content_margin_right", right);
		control.AddThemeConstantOverride("content_margin_bottom", bottom);
	}

	private static Vector2 ResolvePositionAnchorSize(Control control)
	{
		float width = control.Size.X > 0f ? control.Size.X : control.CustomMinimumSize.X;
		float height = control.Size.Y > 0f ? control.Size.Y : control.CustomMinimumSize.Y;
		return new Vector2(width, height);
	}

	private static Vector2 CalculatePositionAnchorShift(Vector2 size, UiPivotAnchorPreset preset)
	{
		Vector2 factor = MapPivotFactor(preset);
		return new Vector2((0.5f - factor.X) * size.X, (0.5f - factor.Y) * size.Y);
	}

	private static Vector2 MapPivotFactor(UiPivotAnchorPreset preset)
	{
		return preset switch
		{
			UiPivotAnchorPreset.TopLeft => new Vector2(0f, 0f),
			UiPivotAnchorPreset.TopCenter => new Vector2(0.5f, 0f),
			UiPivotAnchorPreset.TopRight => new Vector2(1f, 0f),
			UiPivotAnchorPreset.CenterLeft => new Vector2(0f, 0.5f),
			UiPivotAnchorPreset.Center => new Vector2(0.5f, 0.5f),
			UiPivotAnchorPreset.CenterRight => new Vector2(1f, 0.5f),
			UiPivotAnchorPreset.BottomLeft => new Vector2(0f, 1f),
			UiPivotAnchorPreset.BottomCenter => new Vector2(0.5f, 1f),
			UiPivotAnchorPreset.BottomRight => new Vector2(1f, 1f),
			_ => new Vector2(0.5f, 0.5f)
		};
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

	private sealed class UiLayoutState
	{
		public UiLayoutOptions? Options { get; set; }
		public ControlLayoutSnapshot? BaseLayout { get; set; }
		public bool ResizeRefreshBound { get; set; }
		public bool IsApplying { get; set; }
	}

	private readonly record struct ControlLayoutSnapshot(
		float AnchorLeft,
		float AnchorTop,
		float AnchorRight,
		float AnchorBottom,
		float OffsetLeft,
		float OffsetTop,
		float OffsetRight,
		float OffsetBottom,
		Control.GrowDirection GrowHorizontal,
		Control.GrowDirection GrowVertical,
		Control.SizeFlags SizeFlagsHorizontal,
		Control.SizeFlags SizeFlagsVertical,
		Vector2 CustomMinimumSize)
	{
		public static ControlLayoutSnapshot Capture(Control control)
		{
			return new ControlLayoutSnapshot(
				control.AnchorLeft,
				control.AnchorTop,
				control.AnchorRight,
				control.AnchorBottom,
				control.OffsetLeft,
				control.OffsetTop,
				control.OffsetRight,
				control.OffsetBottom,
				control.GrowHorizontal,
				control.GrowVertical,
				control.SizeFlagsHorizontal,
				control.SizeFlagsVertical,
				control.CustomMinimumSize);
		}

		public void ApplyTo(Control control)
		{
			control.AnchorLeft = AnchorLeft;
			control.AnchorTop = AnchorTop;
			control.AnchorRight = AnchorRight;
			control.AnchorBottom = AnchorBottom;
			control.OffsetLeft = OffsetLeft;
			control.OffsetTop = OffsetTop;
			control.OffsetRight = OffsetRight;
			control.OffsetBottom = OffsetBottom;
			control.GrowHorizontal = GrowHorizontal;
			control.GrowVertical = GrowVertical;
			control.SizeFlagsHorizontal = SizeFlagsHorizontal;
			control.SizeFlagsVertical = SizeFlagsVertical;
			control.CustomMinimumSize = CustomMinimumSize;
		}
	}
}

