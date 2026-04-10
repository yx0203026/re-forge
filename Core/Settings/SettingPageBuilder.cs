#nullable enable

using System;
using Godot;
using ReForgeFramework.Settings.Abstractions;
using ReForgeFramework.Settings.Controls;

namespace ReForgeFramework.Settings;

/// <summary>
/// 设置页链式构建器：用于快速组织设置页面内容。
/// </summary>
public sealed class SettingPageBuilder
{
	private readonly SettingTab _tab;

	internal SettingPageBuilder(SettingTab tab)
	{
		_tab = tab;
	}

	public SettingPageBuilder AddToggle(
		string title,
		bool initialValue,
		Action<bool>? onToggled = null,
		string? tipLocTable = null,
		string? tipTitleEntryKey = null,
		string? tipDescriptionEntryKey = null)
	{
		_tab.AddToggle(title, initialValue, onToggled, tipLocTable, tipTitleEntryKey, tipDescriptionEntryKey);
		return this;
	}

	public SettingPageBuilder AddFeedbackButton(
		string title,
		string buttonText,
		Action? onPressed = null,
		string? textKey = null,
		string? locTable = null,
		string? locEntryKey = null,
		string? tipLocTable = null,
		string? tipTitleEntryKey = null,
		string? tipDescriptionEntryKey = null)
	{
		_tab.AddFeedbackButton(title, buttonText, onPressed, textKey, locTable, locEntryKey, tipLocTable, tipTitleEntryKey, tipDescriptionEntryKey);
		return this;
	}

	public SettingPageBuilder AddElement(IUiElement element)
	{
		_tab.Add(element);
		return this;
	}

	public SettingPageBuilder AddControl(Control control)
	{
		_tab.Add(new NativeControlElement(control));
		return this;
	}

	public SettingTab Build()
	{
		return _tab;
	}

	private sealed class NativeControlElement : UiElement
	{
		private readonly Control _control;

		public NativeControlElement(Control control)
		{
			_control = control ?? throw new ArgumentNullException(nameof(control));
		}

		protected override Control CreateControl()
		{
			return _control;
		}
	}
}
