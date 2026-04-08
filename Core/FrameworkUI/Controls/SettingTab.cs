#nullable enable

using System;
using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;
using ReForgeFramework.UI.Abstractions;

namespace ReForgeFramework.UI.Controls;

/// <summary>
/// 设置页 Tab 独立控件：直接实例化官方 NSettingsTab。
/// </summary>
public sealed partial class SettingTab : UiElement
{
	private const string SettingsTabScenePath = "res://scenes/screens/settings_tab.tscn";
	private const string MetaReadyHookBound = "__reforge_setting_tab_ready_hook_bound";
	private const string SafetyPlaceholderName = "ReForgeSettingScreenSafetyPlaceholder";

	private string _text;
	private readonly Action? _onClick;
	private bool _selected;
	private readonly List<IUiElement> _entries = new();
	private VBoxContainer? _content;

	public SettingTab(string text, Action? onClick = null, bool selected = false, string? screenKey = null)
	{
		_text = text;
		_onClick = onClick;
		_selected = selected;
		ScreenKey = string.IsNullOrWhiteSpace(screenKey) ? text : screenKey;
	}

	public string ScreenKey { get; }

	public bool SelectedByDefault => _selected;

	public SettingTab Add(IUiElement entry)
	{
		if (entry is SettingOptionItem settingOptionItem && !settingOptionItem.HasHoverTip)
		{
			throw new InvalidOperationException(
				$"Setting option '{settingOptionItem.Title}' must configure hover tip via WithHoverTip(...) before adding to screen '{ScreenKey}'.");
		}

		if (!_entries.Contains(entry))
		{
			_entries.Add(entry);
		}

		if (_content != null)
		{
			AttachEntry(entry);
		}

		return this;
	}

	/// <summary>
	/// 添加“标题 + 勾选框”设置条目。
	/// </summary>
	public SettingTab AddToggle(
		string title,
		bool initialValue,
		Action<bool>? onToggled = null,
		string? tipLocTable = null,
		string? tipTitleEntryKey = null,
		string? tipDescriptionEntryKey = null)
	{
		SettingOptionItem item = SettingOptionItem.Toggle(title, initialValue, onToggled);
		if (!string.IsNullOrWhiteSpace(tipLocTable)
			&& !string.IsNullOrWhiteSpace(tipTitleEntryKey)
			&& !string.IsNullOrWhiteSpace(tipDescriptionEntryKey))
		{
			item.WithHoverTip(tipLocTable, tipTitleEntryKey, tipDescriptionEntryKey);
		}

		return Add(item);
	}

	/// <summary>
	/// 添加“标题 + 官方反馈风格按钮”设置条目。
	/// </summary>
	public SettingTab AddFeedbackButton(
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
		SettingOptionItem item = SettingOptionItem.FeedbackButton(title, buttonText, onPressed, textKey, locTable, locEntryKey);
		if (!string.IsNullOrWhiteSpace(tipLocTable)
			&& !string.IsNullOrWhiteSpace(tipTitleEntryKey)
			&& !string.IsNullOrWhiteSpace(tipDescriptionEntryKey))
		{
			item.WithHoverTip(tipLocTable, tipTitleEntryKey, tipDescriptionEntryKey);
		}

		return Add(item);
	}

	public new SettingTab WithHeight(float height)
	{
		base.WithHeight(height);
		return this;
	}

	public new SettingTab WithMinHeight(float minHeight)
	{
		base.WithMinHeight(minHeight);
		return this;
	}

	public new SettingTab WithMaxHeight(float maxHeight)
	{
		base.WithMaxHeight(maxHeight);
		return this;
	}

	public new SettingTab WithAnchor(UiAnchorPreset preset)
	{
		base.WithAnchor(preset);
		return this;
	}

	public SettingTab WithText(string text)
	{
		_text = text;
		if (BuiltControl is NSettingsTab tab && GodotObject.IsInstanceValid(tab))
		{
			ApplyStateOrSchedule(tab);
		}
		return this;
	}

	public SettingTab WithSelected(bool selected = true)
	{
		_selected = selected;
		if (BuiltControl is NSettingsTab tab && GodotObject.IsInstanceValid(tab))
		{
			ApplyStateOrSchedule(tab);
		}
		return this;
	}

	protected override Control CreateControl()
	{
		NSettingsTab? tab = LoadOfficialSettingsTab();
		if (tab == null)
		{
			return BuildFallbackControl();
		}

		tab.Name = "ReForgeSettingTab";
		ApplyStateOrSchedule(tab);

		if (_onClick != null)
		{
			tab.Connect(NClickableControl.SignalName.Released, Callable.From<GodotObject>(_ => _onClick()));
		}

		return tab;
	}

	private void ApplyStateOrSchedule(NSettingsTab tab)
	{
		if (!GodotObject.IsInstanceValid(tab))
		{
			return;
		}

		if (TryApplyState(tab))
		{
			return;
		}

		if (tab.HasMeta(MetaReadyHookBound))
		{
			return;
		}

		tab.SetMeta(MetaReadyHookBound, true);
		tab.Connect(Node.SignalName.Ready, Callable.From(() =>
		{
			if (!GodotObject.IsInstanceValid(tab))
			{
				return;
			}

			TryApplyState(tab);
		}), (uint)GodotObject.ConnectFlags.OneShot);
	}

	private bool TryApplyState(NSettingsTab tab)
	{
		if (!tab.IsNodeReady())
		{
			return false;
		}

		tab.SetLabel(_text);
		ApplySelectedState(tab, _selected);
		return true;
	}

	private static NSettingsTab? LoadOfficialSettingsTab()
	{
		if (!ResourceLoader.Exists(SettingsTabScenePath))
		{
			GD.Print($"[ReForge.UI] Missing official settings tab scene: '{SettingsTabScenePath}'.");
			return null;
		}

		PackedScene? scene = ResourceLoader.Load<PackedScene>(SettingsTabScenePath);
		if (scene == null)
		{
			return null;
		}

		return scene.InstantiateOrNull<NSettingsTab>();
	}

	private static void ApplySelectedState(NSettingsTab tab, bool selected)
	{
		if (selected)
		{
			tab.Select();
			return;
		}

		tab.Deselect();
	}

	private Control BuildFallbackControl()
	{
		Godot.Button fallback = new()
		{
			Name = "FallbackSettingTab",
			Text = _text,
			FocusMode = Control.FocusModeEnum.All,
			SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
			ButtonPressed = _selected
		};

		if (_onClick != null)
		{
			fallback.Pressed += _onClick;
		}

		return fallback;
	}

	internal void BindPanel(NSettingsPanel panel)
	{
		_content = panel.GetNodeOrNull<VBoxContainer>("VBoxContainer");
		if (_content == null)
		{
			return;
		}

		if (_entries.Count == 0)
		{
			EnsureSafetyPlaceholder();
			return;
		}

		RemoveSafetyPlaceholder();
		foreach (IUiElement entry in _entries)
		{
			AttachEntry(entry);
		}
	}

	private void AttachEntry(IUiElement entry)
	{
		if (_content == null)
		{
			return;
		}

		Control control = entry.Build();
		if (!GodotObject.IsInstanceValid(control))
		{
			return;
		}

		RemoveSafetyPlaceholder();
		if (control.GetParent() == _content)
		{
			return;
		}

		if (control.GetParent() == null)
		{
			_content.AddChild(control);
		}
		else
		{
			control.Reparent(_content, keepGlobalTransform: true);
		}
	}

	private void EnsureSafetyPlaceholder()
	{
		if (_content == null || _content.GetNodeOrNull<NButton>(SafetyPlaceholderName) != null)
		{
			return;
		}

		NButton placeholder = new()
		{
			Name = SafetyPlaceholderName,
			Visible = false,
			FocusMode = Control.FocusModeEnum.All,
			MouseFilter = Control.MouseFilterEnum.Ignore
		};

		_content.AddChild(placeholder);
	}

	private void RemoveSafetyPlaceholder()
	{
		if (_content?.GetNodeOrNull<NButton>(SafetyPlaceholderName) is { } placeholder)
		{
			placeholder.QueueFree();
		}
	}
}
