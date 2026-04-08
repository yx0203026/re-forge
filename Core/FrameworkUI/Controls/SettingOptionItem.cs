#nullable enable

using Godot;
using MegaCrit.Sts2.Core.Helpers;
using ReForgeFramework.UI.Abstractions;

namespace ReForgeFramework.UI.Controls;

/// <summary>
/// 自定义设置条目（官方风格）：左侧说明文本 + 右侧操作控件。
/// </summary>
public sealed class SettingOptionItem : UiElement
{
	private const string SettingsLineThemePath = "res://themes/settings_screen_line_header.tres";
	private const string FontNormalPath = "res://themes/kreon_regular_shared.tres";
	private const string FontBoldPath = "res://themes/kreon_bold_shared.tres";

	private readonly string _title;
	private readonly IUiElement _optionControl;

	public SettingOptionItem(string title, IUiElement optionControl)
	{
		_title = title;
		_optionControl = optionControl;
	}

	public static SettingOptionItem Toggle(string title, bool initialValue, System.Action<bool>? onToggled = null)
	{
		return new SettingOptionItem(title, new TickBox(initialValue, onToggled));
	}

	protected override Control CreateControl()
	{
		VBoxContainer root = new()
		{
			Name = "ReForgeSettingOptionItem",
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
		};

		MarginContainer row = new()
		{
			Name = "Row",
			CustomMinimumSize = new Vector2(0f, 64f),
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
		};
		row.AddThemeConstantOverride("margin_left", 12);
		row.AddThemeConstantOverride("margin_top", 0);
		row.AddThemeConstantOverride("margin_right", 12);
		row.AddThemeConstantOverride("margin_bottom", 0);

		RichTextLabel labelControl = BuildOfficialLabel();
		labelControl.Name = "Label";
		labelControl.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		labelControl.FocusMode = Control.FocusModeEnum.None;
		labelControl.MouseFilter = Control.MouseFilterEnum.Ignore;
		labelControl.BbcodeEnabled = true;
		labelControl.Text = _title;
		labelControl.VerticalAlignment = VerticalAlignment.Center;

		HBoxContainer rowContent = new()
		{
			Name = "RowContent",
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			Alignment = BoxContainer.AlignmentMode.Begin
		};
		rowContent.AddThemeConstantOverride("separation", 16);

		Control option = _optionControl.Build();
		option.Name = "Option";
		option.CustomMinimumSize = new Vector2(320f, 64f);
		option.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;

		rowContent.AddChild(labelControl);
		rowContent.AddChild(option);
		row.AddChild(rowContent);

		ColorRect divider = new()
		{
			Name = "Divider",
			CustomMinimumSize = new Vector2(0f, 2f),
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			Color = new Color(0.909804f, 0.862745f, 0.745098f, 0.25098f)
		};

		root.AddChild(row);
		root.AddChild(divider);
		return root;
	}

	private static RichTextLabel BuildOfficialLabel()
	{
		RichTextLabel label = new();

		if (ResourceLoader.Exists(SettingsLineThemePath))
		{
			Theme? theme = ResourceLoader.Load<Theme>(SettingsLineThemePath);
			if (theme != null)
			{
				label.Theme = theme;
			}
		}

		if (ResourceLoader.Exists(FontNormalPath))
		{
			FontVariation? normalFont = ResourceLoader.Load<FontVariation>(FontNormalPath);
			if (normalFont != null)
			{
				label.AddThemeFontOverride("normal_font", normalFont);
			}
		}

		if (ResourceLoader.Exists(FontBoldPath))
		{
			FontVariation? boldFont = ResourceLoader.Load<FontVariation>(FontBoldPath);
			if (boldFont != null)
			{
				label.AddThemeFontOverride("bold_font", boldFont);
			}
		}

		label.AddThemeFontSizeOverride("normal_font_size", 28);
		label.AddThemeFontSizeOverride("bold_font_size", 28);
		label.AddThemeFontSizeOverride("bold_italics_font_size", 28);
		label.AddThemeFontSizeOverride("italics_font_size", 28);
		label.AddThemeFontSizeOverride("mono_font_size", 28);

		return label;
	}
}
