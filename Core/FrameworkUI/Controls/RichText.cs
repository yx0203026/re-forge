#nullable enable

using System;
using Godot;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Helpers;
using ReForgeFramework.UI.Abstractions;
using ReForgeFramework.UI.Localization;

namespace ReForgeFramework.UI.Controls;

/// <summary>
/// 富文本控件，支持 BBCode 并保持官方字体与配色风格。
/// </summary>
public class RichText : UiElement
{
	private static readonly Font NormalFont = LoadFontOrFallback("res://themes/source_code_pro_medium_shared.tres");
	private static readonly Font BoldFont = LoadFontOrFallback("res://themes/source_code_pro_semibold_shared.tres");

	private readonly string _text;
	private readonly bool _bbcodeEnabled;
	private readonly string? _textKey;
	private readonly string? _locTable;
	private readonly string? _locEntryKey;

	/// <summary>
	/// 初始化富文本控件。
	/// </summary>
	/// <param name="text">默认显示文本。</param>
	/// <param name="bbcodeEnabled">是否启用 BBCode。</param>
	/// <param name="textKey">UI 本地化 key。</param>
	/// <param name="locTable">官方本地化表名。</param>
	/// <param name="locEntryKey">官方本地化词条键。</param>
	public RichText(
		string text,
		bool bbcodeEnabled = true,
		string? textKey = null,
		string? locTable = null,
		string? locEntryKey = null)
	{
		_text = text;
		_bbcodeEnabled = bbcodeEnabled;
		_textKey = textKey;
		_locTable = locTable;
		_locEntryKey = locEntryKey;
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

		ApplyOfficialStyle(richText);
		BindLocalizationRefresh(richText);
		return richText;
	}

	private string ResolveText()
	{
		return UiLocalization.GetText(_textKey, _text, _locTable, _locEntryKey);
	}

	private static void ApplyOfficialStyle(MegaRichTextLabel richText)
	{
		richText.AddThemeFontOverride(ThemeConstants.RichTextLabel.NormalFont, NormalFont);
		richText.AddThemeFontOverride(ThemeConstants.RichTextLabel.BoldFont, BoldFont);
		richText.AddThemeFontOverride(ThemeConstants.RichTextLabel.ItalicsFont, NormalFont);
		richText.AddThemeColorOverride(ThemeConstants.RichTextLabel.DefaultColor, StsColors.cream);
		richText.AddThemeColorOverride(ThemeConstants.RichTextLabel.FontShadowColor, new Color(0f, 0f, 0f, 0.5f));
		richText.AddThemeConstantOverride("shadow_offset_x", 3);
		richText.AddThemeConstantOverride("shadow_offset_y", 2);
		richText.AddThemeFontSizeOverride(ThemeConstants.RichTextLabel.NormalFontSize, 24);
		richText.AddThemeFontSizeOverride(ThemeConstants.RichTextLabel.BoldFontSize, 24);
		richText.AddThemeFontSizeOverride(ThemeConstants.RichTextLabel.BoldItalicsFontSize, 24);
		richText.AddThemeFontSizeOverride(ThemeConstants.RichTextLabel.ItalicsFontSize, 24);
		richText.AddThemeFontSizeOverride(ThemeConstants.RichTextLabel.MonoFontSize, 24);
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

			richText.SetTextAutoSize(ResolveText());
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
