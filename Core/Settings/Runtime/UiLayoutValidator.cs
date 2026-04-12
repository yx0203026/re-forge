#nullable enable

using Godot;
using ReForgeFramework.Settings.Abstractions;

namespace ReForgeFramework.Settings.Runtime;

/// <summary>
/// 璐熻矗甯冨眬鍙傛暟鍚堟硶鎬ф牎楠屼笌褰掍竴鍖栵紝閬垮厤鏃犳晥杈撳叆涓柇 UI 鍒濆鍖栥€?
/// </summary>
internal static class UiLayoutValidator
{
	/// <summary>
	/// 鏍￠獙骞跺綊涓€鍖栧竷灞€鍙傛暟銆?
	/// </summary>
	/// <param name="options">鍘熷甯冨眬閰嶇疆銆?/param>
	/// <param name="controlName">鎺т欢鍚嶇О锛堢敤浜庢棩蹇楋級銆?/param>
	/// <returns>褰掍竴鍖栧悗鐨勫竷灞€閰嶇疆銆?/returns>
	public static UiLayoutOptions Normalize(UiLayoutOptions? options, string controlName)
	{
		if (options == null)
		{
			return new UiLayoutOptions();
		}

		float? width = NormalizeSizeValue(options.Width, "Width", controlName);
		float? minWidth = NormalizeSizeValue(options.MinWidth, "MinWidth", controlName);
		float? maxWidth = NormalizeSizeValue(options.MaxWidth, "MaxWidth", controlName);
		float? height = NormalizeSizeValue(options.Height, "Height", controlName);
		float? minHeight = NormalizeSizeValue(options.MinHeight, "MinHeight", controlName);
		float? maxHeight = NormalizeSizeValue(options.MaxHeight, "MaxHeight", controlName);
		Vector2? positionOffset = NormalizePositionOffset(options.PositionOffset, controlName);
		UiSpacing? padding = NormalizeSpacing(options.Padding, "Padding", controlName);
		UiSpacing? margin = NormalizeSpacing(options.Margin, "Margin", controlName);

		if (minWidth.HasValue && maxWidth.HasValue && minWidth.Value > maxWidth.Value)
		{
			(minWidth, maxWidth) = (maxWidth, minWidth);
			GD.Print($"[ReForge.UI] Swapped MinWidth/MaxWidth on '{controlName}' because MinWidth was greater than MaxWidth.");
		}

		if (width.HasValue)
		{
			if (minWidth.HasValue && width.Value < minWidth.Value)
			{
				width = minWidth.Value;
				GD.Print($"[ReForge.UI] Width clamped to MinWidth for '{controlName}'.");
			}

			if (maxWidth.HasValue && width.Value > maxWidth.Value)
			{
				width = maxWidth.Value;
				GD.Print($"[ReForge.UI] Width clamped to MaxWidth for '{controlName}'.");
			}
		}

		if (minHeight.HasValue && maxHeight.HasValue && minHeight.Value > maxHeight.Value)
		{
			(minHeight, maxHeight) = (maxHeight, minHeight);
			GD.Print($"[ReForge.UI] Swapped MinHeight/MaxHeight on '{controlName}' because MinHeight was greater than MaxHeight.");
		}

		if (height.HasValue)
		{
			if (minHeight.HasValue && height.Value < minHeight.Value)
			{
				height = minHeight.Value;
				GD.Print($"[ReForge.UI] Height clamped to MinHeight for '{controlName}'.");
			}

			if (maxHeight.HasValue && height.Value > maxHeight.Value)
			{
				height = maxHeight.Value;
				GD.Print($"[ReForge.UI] Height clamped to MaxHeight for '{controlName}'.");
			}
		}

		return new UiLayoutOptions
		{
			Width = width,
			MinWidth = minWidth,
			MaxWidth = maxWidth,
			Height = height,
			MinHeight = minHeight,
			MaxHeight = maxHeight,
			AnchorPreset = options.AnchorPreset,
			PositionOffset = positionOffset,
			PositionAnchorPreset = options.PositionAnchorPreset,
			Padding = padding,
			Margin = margin
		};
	}

	private static float? NormalizeSizeValue(float? value, string fieldName, string controlName)
	{
		if (!value.HasValue)
		{
			return null;
		}

		float raw = value.Value;
		if (!float.IsFinite(raw) || raw < 0f)
		{
			GD.Print($"[ReForge.UI] Invalid {fieldName} '{raw}' on '{controlName}', value ignored.");
			return null;
		}

		return raw;
	}

	private static Vector2? NormalizePositionOffset(Vector2? value, string controlName)
	{
		if (!value.HasValue)
		{
			return null;
		}

		Vector2 raw = value.Value;
		if (!float.IsFinite(raw.X) || !float.IsFinite(raw.Y))
		{
			GD.Print($"[ReForge.UI] Invalid PositionOffset '{raw}' on '{controlName}', value ignored.");
			return null;
		}

		return raw;
	}

	private static UiSpacing? NormalizeSpacing(UiSpacing? value, string fieldName, string controlName)
	{
		if (!value.HasValue)
		{
			return null;
		}

		UiSpacing raw = value.Value;
		if (!float.IsFinite(raw.Left) || raw.Left < 0f
			|| !float.IsFinite(raw.Top) || raw.Top < 0f
			|| !float.IsFinite(raw.Right) || raw.Right < 0f
			|| !float.IsFinite(raw.Bottom) || raw.Bottom < 0f)
		{
			GD.Print($"[ReForge.UI] Invalid {fieldName} '{raw}' on '{controlName}', value ignored.");
			return null;
		}

		return raw;
	}
}

