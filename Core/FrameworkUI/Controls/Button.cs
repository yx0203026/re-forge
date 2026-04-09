#nullable enable

using System;
using Godot;
using ReForgeFramework.UI.Abstractions;
using ReForgeFramework.UI.Localization;

namespace ReForgeFramework.UI.Controls;

/// <summary>
/// 通用按钮控件，支持本地化文本、官方样式模板与自定义样式回调。
/// </summary>
public class Button : UiElement
{
	private readonly string _text;
	private readonly string? _textKey;
	private readonly string? _locTable;
	private readonly string? _locEntryKey;
	private readonly Action? _onClick;
	private readonly UiButtonStylePreset _stylePreset;
	private readonly Action<Godot.Button>? _customStyler;

	/// <summary>
	/// 初始化按钮控件。
	/// </summary>
	/// <param name="text">默认显示文本。</param>
	/// <param name="onClick">点击回调。</param>
	/// <param name="textKey">UI 本地化 key。</param>
	/// <param name="locTable">官方本地化表名。</param>
	/// <param name="locEntryKey">官方本地化词条键。</param>
	/// <param name="stylePreset">按钮样式预设。</param>
	/// <param name="customStyler">原生按钮额外样式回调。</param>
	public Button(
		string text = "Button",
		Action? onClick = null,
		string? textKey = null,
		string? locTable = null,
		string? locEntryKey = null,
		UiButtonStylePreset stylePreset = UiButtonStylePreset.GodotDefault,
		Action<Godot.Button>? customStyler = null)
	{
		_text = text;
		_onClick = onClick;
		_textKey = textKey;
		_locTable = locTable;
		_locEntryKey = locEntryKey;
		_stylePreset = stylePreset;
		_customStyler = customStyler;
	}

	protected override Control CreateControl()
	{
		return CreateButtonControl();
	}

	protected virtual Godot.Button CreateButtonControl()
	{
		Godot.Button button = new Godot.Button
		{
			Text = ResolveText(),
			CustomMinimumSize = new Vector2(200f, 44f),
			FocusMode = Control.FocusModeEnum.All
		};

		ApplyStyle(button);

		if (_onClick != null)
		{
			button.Pressed += _onClick;
		}

		BindLocalizationRefresh(button);

		return button;
	}

	protected virtual void ApplyStyle(Godot.Button button)
	{
		UiButtonStyleTemplates.Apply(button, _stylePreset);
		_customStyler?.Invoke(button);
	}

	private string ResolveText()
	{
		return UiLocalization.GetText(_textKey, _text, _locTable, _locEntryKey);
	}

	private void BindLocalizationRefresh(Godot.Button button)
	{
		if (string.IsNullOrWhiteSpace(_textKey)
			&& (string.IsNullOrWhiteSpace(_locTable) || string.IsNullOrWhiteSpace(_locEntryKey)))
		{
			return;
		}

		Action? handler = null;
		handler = () =>
		{
			if (!GodotObject.IsInstanceValid(button))
			{
				if (handler != null)
				{
					UiLocalization.LocaleChanged -= handler;
				}
				return;
			}

			button.Text = ResolveText();
		};

		UiLocalization.LocaleChanged += handler;
		button.TreeExiting += () =>
		{
			if (handler != null)
			{
				UiLocalization.LocaleChanged -= handler;
			}
		};
	}
}
