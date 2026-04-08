#nullable enable

namespace ReForgeFramework.UI.Abstractions;

/// <summary>
/// 四边距参数：用于统一表达 Padding 与 Margin。
/// </summary>
public readonly record struct UiSpacing(float Left, float Top, float Right, float Bottom)
{
	public static UiSpacing All(float value)
	{
		return new UiSpacing(value, value, value, value);
	}

	public static UiSpacing Axis(float horizontal, float vertical)
	{
		return new UiSpacing(horizontal, vertical, horizontal, vertical);
	}
}