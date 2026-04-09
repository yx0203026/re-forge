#nullable enable

using Godot;
using ReForgeFramework.UI.Abstractions;

namespace ReForgeFramework.UI.Runtime;

/// <summary>
/// 负责布局参数合法性校验与归一化，避免无效输入中断 UI 初始化。
/// </summary>
internal static class UiLayoutValidator
{
	/// <summary>
	/// 校验并归一化布局参数。
	/// </summary>
	/// <param name="options">原始布局配置。</param>
	/// <param name="controlName">控件名称（用于日志）。</param>
	/// <returns>归一化后的布局配置。</returns>
	public static UiLayoutOptions Normalize(UiLayoutOptions? options, string controlName)
	{
		if (options == null)
		{
			return new UiLayoutOptions();
		}

		float? height = NormalizeHeightValue(options.Height, "Height", controlName);
		float? minHeight = NormalizeHeightValue(options.MinHeight, "MinHeight", controlName);
		float? maxHeight = NormalizeHeightValue(options.MaxHeight, "MaxHeight", controlName);
		Vector2? positionOffset = NormalizePositionOffset(options.PositionOffset, controlName);
		UiSpacing? padding = NormalizeSpacing(options.Padding, "Padding", controlName);
		UiSpacing? margin = NormalizeSpacing(options.Margin, "Margin", controlName);

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
			Height = height,
			MinHeight = minHeight,
			MaxHeight = maxHeight,
			AnchorPreset = options.AnchorPreset,
			PositionOffset = positionOffset,
			Padding = padding,
			Margin = margin
		};
	}

	private static float? NormalizeHeightValue(float? value, string fieldName, string controlName)
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
