#nullable enable

using System;
using Godot;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Helpers;
using ReForgeFramework.UI.Abstractions;
using ReForgeFramework.UI.Localization;

namespace ReForgeFramework.UI.Controls;

/// <summary>
/// 官方风格文本控件，基于 MegaLabel 并支持本地化刷新。
/// </summary>
public class Label : UiElement
{
	private static readonly Font LabelFont = LoadFontOrFallback("res://themes/kreon_regular_shared.tres");
	private static readonly StyleBoxEmpty EmptyStyleBox = new();

	private readonly string _text;
	private readonly string? _textKey;
	private readonly string? _locTable;
	private readonly string? _locEntryKey;

	/// <summary>
	/// 初始化文本控件。
	/// </summary>
	/// <param name="text">默认显示文本。</param>
	/// <param name="textKey">UI 本地化 key。</param>
	/// <param name="locTable">官方本地化表名。</param>
	/// <param name="locEntryKey">官方本地化词条键。</param>
	public Label(string text, string? textKey = null, string? locTable = null, string? locEntryKey = null)
	{
		_text = text;
		_textKey = textKey;
		_locTable = locTable;
		_locEntryKey = locEntryKey;
	}

	protected override Control CreateControl()
	{
		MegaLabel label = new()
		{
			Text = ResolveText(),
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			AutoSizeEnabled = false
		};

		ApplyOfficialStyle(label);

		BindLocalizationRefresh(label);

		return label;
	}

	private string ResolveText()
	{
		return UiLocalization.GetText(_textKey, _text, _locTable, _locEntryKey);
	}

	private static void ApplyOfficialStyle(MegaLabel label)
	{
		label.AddThemeFontOverride(ThemeConstants.Label.Font, LabelFont);
		label.AddThemeColorOverride(ThemeConstants.Label.FontColor, Colors.White);
		label.AddThemeColorOverride(ThemeConstants.Label.FontOutlineColor, new Color(0f, 0f, 0f, 0.5f));
		label.AddThemeConstantOverride(ThemeConstants.Label.LineSpacing, 3);
		label.AddThemeConstantOverride(ThemeConstants.Label.OutlineSize, 18);
		label.AddThemeStyleboxOverride(ThemeConstants.Control.Focus, EmptyStyleBox);
		label.FocusMode = Control.FocusModeEnum.None;
		label.SelfModulate = StsColors.cream;
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

	private void BindLocalizationRefresh(MegaLabel label)
	{
		if (string.IsNullOrWhiteSpace(_textKey)
			&& (string.IsNullOrWhiteSpace(_locTable) || string.IsNullOrWhiteSpace(_locEntryKey)))
		{
			return;
		}

		Action? handler = null;
		handler = () =>
		{
			if (!GodotObject.IsInstanceValid(label))
			{
				if (handler != null)
				{
					UiLocalization.LocaleChanged -= handler;
				}
				return;
			}

			label.SetTextAutoSize(ResolveText());
		};

		UiLocalization.LocaleChanged += handler;
		label.TreeExiting += () =>
		{
			if (handler != null)
			{
				UiLocalization.LocaleChanged -= handler;
			}
		};
	}
}
