#nullable enable

namespace ReForgeFramework.UI.Abstractions;

/// <summary>
/// 四边距参数：用于统一表达 Padding 与 Margin。
/// </summary>
public readonly record struct UiSpacing(float Left, float Top, float Right, float Bottom)
{
	/// <summary>
	/// 以统一值创建四边间距。
	/// </summary>
	/// <param name="value">四边统一值。</param>
	/// <returns>间距对象。</returns>
	public static UiSpacing All(float value)
	{
		return new UiSpacing(value, value, value, value);
	}

	/// <summary>
	/// 以水平和垂直值创建间距。
	/// </summary>
	/// <param name="horizontal">水平值。</param>
	/// <param name="vertical">垂直值。</param>
	/// <returns>间距对象。</returns>
	public static UiSpacing Axis(float horizontal, float vertical)
	{
		return new UiSpacing(horizontal, vertical, horizontal, vertical);
	}
}