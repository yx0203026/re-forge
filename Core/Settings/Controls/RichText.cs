#nullable enable

using System;
using Godot;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Helpers;
using ReForgeFramework.Settings.Abstractions;
using ReForgeFramework.Settings.Localization;

namespace ReForgeFramework.Settings.Controls;

/// <summary>
/// 瀵屾枃鏈帶浠讹紝鏀寔 BBCode 骞朵繚鎸佸畼鏂瑰瓧浣撲笌閰嶈壊椋庢牸銆?/// </summary>
public class RichText : UiElement
{
	private static readonly Font NormalFont = LoadFontOrFallback("res://themes/source_code_pro_medium_shared.tres");
	private static readonly Font BoldFont = LoadFontOrFallback("res://themes/source_code_pro_semibold_shared.tres");
	private const int DefaultFontSize = 24;

	private readonly string _text;
	private readonly bool _bbcodeEnabled;
	private readonly string? _textKey;
	private readonly string? _locTable;
	private readonly string? _locEntryKey;
	private int? _fontSize;
	private UiPivotAnchorPreset? _textAnchorPreset;

	/// <summary>
	/// 鍒濆鍖栧瘜鏂囨湰鎺т欢銆?	/// </summary>
	/// <param name="text">榛樿鏄剧ず鏂囨湰銆?/param>
	/// <param name="bbcodeEnabled">鏄惁鍚敤 BBCode銆?/param>
	/// <param name="textKey">UI 鏈湴鍖?key銆?/param>
	/// <param name="locTable">瀹樻柟鏈湴鍖栬〃鍚嶃€?/param>
	/// <param name="locEntryKey">瀹樻柟鏈湴鍖栬瘝鏉￠敭銆?/param>
	public RichText(
		string text,
		bool bbcodeEnabled = true,
		string? textKey = null,
		string? locTable = null,
		string? locEntryKey = null,
		int? fontSize = null,
		UiPivotAnchorPreset? textAnchorPreset = null)
	{
		_text = text;
		_bbcodeEnabled = bbcodeEnabled;
		_textKey = textKey;
		_locTable = locTable;
		_locEntryKey = locEntryKey;
		_fontSize = fontSize;
		_textAnchorPreset = textAnchorPreset;
	}

	/// <summary>
	/// 璁剧疆瀵屾枃鏈瓧鍙枫€?	/// </summary>
	/// <param name="fontSize">瀛椾綋澶у皬锛屽繀椤诲ぇ浜?0銆?/param>
	/// <returns>褰撳墠瀵屾枃鏈帶浠跺疄渚嬨€?/returns>
	public RichText WithFontSize(int fontSize)
	{
		if (fontSize <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(fontSize), fontSize, "Font size must be greater than 0.");
		}

		_fontSize = fontSize;
		ApplyFontSizeIfBuilt();
		return this;
	}

	/// <summary>
	/// 璁剧疆鏂囨湰閿氱偣銆?	/// </summary>
	/// <param name="preset">鏂囨湰閿氱偣棰勮銆?/param>
	/// <returns>褰撳墠瀵屾枃鏈帶浠跺疄渚嬨€?/returns>
	public RichText WithTextAnchor(UiPivotAnchorPreset preset)
	{
		_textAnchorPreset = preset;
		ApplyTextAnchorIfBuilt();
		return this;
	}

	/// <summary>
	/// 璁剧疆鏂囨湰閿氱偣妯″紡锛堜笌 WithTextAnchor 璇箟涓€鑷达級銆?	/// </summary>
	/// <param name="preset">鏂囨湰閿氱偣棰勮銆?/param>
	/// <returns>褰撳墠瀵屾枃鏈帶浠跺疄渚嬨€?/returns>
	public RichText WithTextAnchorMode(UiPivotAnchorPreset preset)
	{
		return WithTextAnchor(preset);
	}

	protected override Control CreateControl()
	{
		MegaRichTextLabel richText = new()
		{
			BbcodeEnabled = _bbcodeEnabled,
			FitContent = true,
			ScrollActive = false,
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			AutoSizeEnabled = false,
			Text = ResolveText()
		};

		ApplyOfficialStyle(richText, _fontSize ?? DefaultFontSize);
		if (_textAnchorPreset is UiPivotAnchorPreset anchorPreset)
		{
			ApplyTextAnchor(richText, anchorPreset);
		}
		BindLocalizationRefresh(richText);
		return richText;
	}

	private string ResolveText()
	{
		return UiLocalization.GetText(_textKey, _text, _locTable, _locEntryKey);
	}

	private static void ApplyOfficialStyle(MegaRichTextLabel richText, int fontSize)
	{
		richText.AddThemeFontOverride(ThemeConstants.RichTextLabel.NormalFont, NormalFont);
		richText.AddThemeFontOverride(ThemeConstants.RichTextLabel.BoldFont, BoldFont);
		richText.AddThemeFontOverride(ThemeConstants.RichTextLabel.ItalicsFont, NormalFont);
		richText.AddThemeColorOverride(ThemeConstants.RichTextLabel.DefaultColor, StsColors.cream);
		richText.AddThemeColorOverride(ThemeConstants.RichTextLabel.FontShadowColor, new Color(0f, 0f, 0f, 0.5f));
		richText.AddThemeConstantOverride("shadow_offset_x", 3);
		richText.AddThemeConstantOverride("shadow_offset_y", 2);
		richText.AddThemeFontSizeOverride(ThemeConstants.RichTextLabel.NormalFontSize, fontSize);
		richText.AddThemeFontSizeOverride(ThemeConstants.RichTextLabel.BoldFontSize, fontSize);
		richText.AddThemeFontSizeOverride(ThemeConstants.RichTextLabel.BoldItalicsFontSize, fontSize);
		richText.AddThemeFontSizeOverride(ThemeConstants.RichTextLabel.ItalicsFontSize, fontSize);
		richText.AddThemeFontSizeOverride(ThemeConstants.RichTextLabel.MonoFontSize, fontSize);
	}

	private void ApplyFontSizeIfBuilt()
	{
		if (_fontSize is not int fontSize)
		{
			return;
		}

		if (BuiltControl is MegaRichTextLabel builtRichText)
		{
			builtRichText.AddThemeFontSizeOverride(ThemeConstants.RichTextLabel.NormalFontSize, fontSize);
			builtRichText.AddThemeFontSizeOverride(ThemeConstants.RichTextLabel.BoldFontSize, fontSize);
			builtRichText.AddThemeFontSizeOverride(ThemeConstants.RichTextLabel.BoldItalicsFontSize, fontSize);
			builtRichText.AddThemeFontSizeOverride(ThemeConstants.RichTextLabel.ItalicsFontSize, fontSize);
			builtRichText.AddThemeFontSizeOverride(ThemeConstants.RichTextLabel.MonoFontSize, fontSize);
		}
	}

	private void ApplyTextAnchorIfBuilt()
	{
		if (_textAnchorPreset is not UiPivotAnchorPreset anchorPreset)
		{
			return;
		}

		if (BuiltControl is MegaRichTextLabel builtRichText)
		{
			ApplyTextAnchor(builtRichText, anchorPreset);
		}
	}

	private static void ApplyTextAnchor(MegaRichTextLabel richText, UiPivotAnchorPreset preset)
	{
		richText.HorizontalAlignment = MapHorizontalAlignment(preset);
		richText.VerticalAlignment = MapVerticalAlignment(preset);
	}

	private static HorizontalAlignment MapHorizontalAlignment(UiPivotAnchorPreset preset)
	{
		return preset switch
		{
			UiPivotAnchorPreset.TopLeft or UiPivotAnchorPreset.CenterLeft or UiPivotAnchorPreset.BottomLeft => HorizontalAlignment.Left,
			UiPivotAnchorPreset.TopCenter or UiPivotAnchorPreset.Center or UiPivotAnchorPreset.BottomCenter => HorizontalAlignment.Center,
			UiPivotAnchorPreset.TopRight or UiPivotAnchorPreset.CenterRight or UiPivotAnchorPreset.BottomRight => HorizontalAlignment.Right,
			_ => HorizontalAlignment.Left
		};
	}

	private static VerticalAlignment MapVerticalAlignment(UiPivotAnchorPreset preset)
	{
		return preset switch
		{
			UiPivotAnchorPreset.TopLeft or UiPivotAnchorPreset.TopCenter or UiPivotAnchorPreset.TopRight => VerticalAlignment.Top,
			UiPivotAnchorPreset.CenterLeft or UiPivotAnchorPreset.Center or UiPivotAnchorPreset.CenterRight => VerticalAlignment.Center,
			UiPivotAnchorPreset.BottomLeft or UiPivotAnchorPreset.BottomCenter or UiPivotAnchorPreset.BottomRight => VerticalAlignment.Bottom,
			_ => VerticalAlignment.Top
		};
	}

	private static Font LoadFontOrFallback(string path)
	{
		if (ResourceLoader.Exists(path))
		{
			Font? font = ResourceLoader.Load<Font>(path);
			if (font != null)
			{
				return font;
			}
		}

		return ThemeDB.FallbackFont;
	}

	private void BindLocalizationRefresh(MegaRichTextLabel richText)
	{
		if (string.IsNullOrWhiteSpace(_textKey)
			&& (string.IsNullOrWhiteSpace(_locTable) || string.IsNullOrWhiteSpace(_locEntryKey)))
		{
			return;
		}

		Action? handler = null;
		handler = () =>
		{
			if (!GodotObject.IsInstanceValid(richText))
			{
				if (handler != null)
				{
					UiLocalization.LocaleChanged -= handler;
				}
				return;
			}

			richText.Text = ResolveText();
		};

		UiLocalization.LocaleChanged += handler;
		richText.TreeExiting += () =>
		{
			if (handler != null)
			{
				UiLocalization.LocaleChanged -= handler;
			}
		};
	}
}

