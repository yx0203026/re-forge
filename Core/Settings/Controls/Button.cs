#nullable enable

using System;
using Godot;
using ReForgeFramework.Settings.Abstractions;
using ReForgeFramework.Settings.Localization;

namespace ReForgeFramework.Settings.Controls;

/// <summary>
/// 閫氱敤鎸夐挳鎺т欢锛屾敮鎸佹湰鍦板寲鏂囨湰銆佸畼鏂规牱寮忔ā鏉夸笌鑷畾涔夋牱寮忓洖璋冦€?
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
	/// 鍒濆鍖栨寜閽帶浠躲€?
	/// </summary>
	/// <param name="text">榛樿鏄剧ず鏂囨湰銆?/param>
	/// <param name="onClick">鐐瑰嚮鍥炶皟銆?/param>
	/// <param name="textKey">UI 鏈湴鍖?key銆?/param>
	/// <param name="locTable">瀹樻柟鏈湴鍖栬〃鍚嶃€?/param>
	/// <param name="locEntryKey">瀹樻柟鏈湴鍖栬瘝鏉￠敭銆?/param>
	/// <param name="stylePreset">鎸夐挳鏍峰紡棰勮銆?/param>
	/// <param name="customStyler">鍘熺敓鎸夐挳棰濆鏍峰紡鍥炶皟銆?/param>
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

