#nullable enable

using Godot;

namespace ReForgeFramework.Settings.Abstractions;

/// <summary>
/// 瑙嗚鍙傛暟缁熶竴鍏ュ彛锛氱缉鏀俱€佺汗鐞嗐€侀鑹茬瓑鍙鐢ㄥ睘鎬с€?/// </summary>
public sealed class UiVisualOptions
{
	/// <summary>
	/// 鎺т欢缂╂斁鍊笺€?	/// </summary>
	public Vector2? Scale { get; set; }

	/// <summary>
	/// 鎺т欢璋冨埗棰滆壊銆?	/// </summary>
	public Color? SelfModulate { get; set; }

	/// <summary>
	/// 灞傜骇浼樺厛绾с€?	/// </summary>
	public int? LayerPriority { get; set; }

	/// <summary>
	/// 鏄惁鐩稿鐖惰妭鐐瑰眰绾с€?	/// </summary>
	public bool LayerPriorityRelative { get; set; } = true;

	/// <summary>
	/// 鍙鎬т綔鐢ㄥ煙銆?	/// </summary>
	public UiVisibilityScope VisibilityScope { get; set; } = UiVisibilityScope.Always;

	/// <summary>
	/// 鏄惁鍚敤涓績鏋㈣酱銆?	/// </summary>
	public bool CenterPivot { get; set; }

	/// <summary>
	/// UI 鑷韩鏋㈣酱閿氱偣锛堢嫭绔嬩簬甯冨眬閿氱偣锛夈€?	/// </summary>
	public UiPivotAnchorPreset? PivotAnchorPreset { get; set; }

	/// <summary>
	/// 闄勫姞绾圭悊銆?	/// </summary>
	public Texture2D? Texture { get; set; }

	/// <summary>
	/// 绾圭悊鎷変几妯″紡銆?	/// </summary>
	public TextureRect.StretchModeEnum TextureStretchMode { get; set; } = TextureRect.StretchModeEnum.KeepAspectCentered;

	/// <summary>
	/// 绾圭悊鏄惁鏄剧ず鍦ㄧ埗鎺т欢鍚庢柟銆?	/// </summary>
	public bool TextureShowBehindParent { get; set; } = true;
}

