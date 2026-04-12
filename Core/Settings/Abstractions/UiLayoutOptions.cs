#nullable enable

using Godot;

namespace ReForgeFramework.Settings.Abstractions;

/// <summary>
/// 缁熶竴甯冨眬鍙傛暟瀹瑰櫒锛岄伩鍏嶅昂瀵镐笌閿氱偣閫昏緫鏁ｈ惤鍦ㄥ叿浣撴帶浠朵腑銆?
/// </summary>
public sealed class UiLayoutOptions
{
	/// <summary>
	/// 鏈熸湜瀹藉害銆?
	/// </summary>
	public float? Width { get; set; }

	/// <summary>
	/// 鏈€灏忓搴︺€?
	/// </summary>
	public float? MinWidth { get; set; }

	/// <summary>
	/// 鏈€澶у搴︺€?
	/// </summary>
	public float? MaxWidth { get; set; }

	/// <summary>
	/// 鏈熸湜楂樺害銆?
	/// </summary>
	public float? Height { get; set; }

	/// <summary>
	/// 鏈€灏忛珮搴︺€?
	/// </summary>
	public float? MinHeight { get; set; }

	/// <summary>
	/// 鏈€澶ч珮搴︺€?
	/// </summary>
	public float? MaxHeight { get; set; }

	/// <summary>
	/// 閿氱偣棰勮銆?
	/// </summary>
	public UiAnchorPreset? AnchorPreset { get; set; }

	/// <summary>
	/// 鍋忕Щ閲忋€?
	/// </summary>
	public Vector2? PositionOffset { get; set; }

	/// <summary>
	/// 浣嶇疆鍋忕Щ鎵€鍩轰簬鐨?UI 鑷韩閿氱偣銆?
	/// 涓虹┖鏃朵繚鎸侀粯璁や綅缃涔夛紱鏈夊€兼椂灏?PositionOffset 瑙ｉ噴涓鸿閿氱偣鍧愭爣銆?
	/// </summary>
	public UiPivotAnchorPreset? PositionAnchorPreset { get; set; }

	/// <summary>
	/// 鍐呰竟璺濄€?
	/// </summary>
	public UiSpacing? Padding { get; set; }

	/// <summary>
	/// 澶栬竟璺濄€?
	/// </summary>
	public UiSpacing? Margin { get; set; }

	/// <summary>
	/// 澶嶅埗褰撳墠甯冨眬閰嶇疆銆?
	/// </summary>
	/// <returns>鏂扮殑甯冨眬閰嶇疆瀹炰緥銆?/returns>
	public UiLayoutOptions Clone()
	{
		return new UiLayoutOptions
		{
			Width = Width,
			MinWidth = MinWidth,
			MaxWidth = MaxWidth,
			Height = Height,
			MinHeight = MinHeight,
			MaxHeight = MaxHeight,
			AnchorPreset = AnchorPreset,
			PositionOffset = PositionOffset,
			PositionAnchorPreset = PositionAnchorPreset,
			Padding = Padding,
			Margin = Margin
		};
	}
}

