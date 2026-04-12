#nullable enable

namespace ReForgeFramework.Settings.Abstractions;

/// <summary>
/// 鍥涜竟璺濆弬鏁帮細鐢ㄤ簬缁熶竴琛ㄨ揪 Padding 涓?Margin銆?
/// </summary>
public readonly record struct UiSpacing(float Left, float Top, float Right, float Bottom)
{
	/// <summary>
	/// 浠ョ粺涓€鍊煎垱寤哄洓杈归棿璺濄€?
	/// </summary>
	/// <param name="value">鍥涜竟缁熶竴鍊笺€?/param>
	/// <returns>闂磋窛瀵硅薄銆?/returns>
	public static UiSpacing All(float value)
	{
		return new UiSpacing(value, value, value, value);
	}

	/// <summary>
	/// 浠ユ按骞冲拰鍨傜洿鍊煎垱寤洪棿璺濄€?
	/// </summary>
	/// <param name="horizontal">姘村钩鍊笺€?/param>
	/// <param name="vertical">鍨傜洿鍊笺€?/param>
	/// <returns>闂磋窛瀵硅薄銆?/returns>
	public static UiSpacing Axis(float horizontal, float vertical)
	{
		return new UiSpacing(horizontal, vertical, horizontal, vertical);
	}
}
