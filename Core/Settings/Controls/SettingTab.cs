#nullable enable

using System;
using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;
using ReForgeFramework.Settings.Abstractions;

namespace ReForgeFramework.Settings.Controls;

/// <summary>
/// 璁剧疆椤?Tab 鐙珛鎺т欢锛氱洿鎺ュ疄渚嬪寲瀹樻柟 NSettingsTab銆?
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

	/// <summary>
	/// 鍒濆鍖栬缃〉鏍囩銆?
	/// </summary>
	/// <param name="text">鏍囩鏂囨湰銆?/param>
	/// <param name="onClick">鐐瑰嚮鍥炶皟銆?/param>
	/// <param name="selected">鏄惁榛樿閫変腑銆?/param>
	/// <param name="screenKey">璁剧疆椤靛敮涓€閿紱涓虹┖鏃朵娇鐢ㄦ枃鏈€?/param>
	public SettingTab(string text, Action? onClick = null, bool selected = false, string? screenKey = null)
	{
		_text = text;
		_onClick = onClick;
		_selected = selected;
		ScreenKey = string.IsNullOrWhiteSpace(screenKey) ? text : screenKey;
	}

	/// <summary>
	/// 璁剧疆椤靛敮涓€閿紝鐢ㄤ簬鏌ユ壘涓庤矾鐢便€?
	/// </summary>
	public string ScreenKey { get; }

	/// <summary>
	/// 褰撳墠鏍囩鏄惁榛樿閫変腑銆?
	/// </summary>
	public bool SelectedByDefault => _selected;

	/// <summary>
	/// 娣诲姞涓€涓缃」鎺т欢銆?
	/// </summary>
	/// <param name="entry">寰呮坊鍔犳帶浠躲€?/param>
	/// <returns>褰撳墠鏍囩瀹炰緥銆?/returns>
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
	/// 娣诲姞鈥滄爣棰?+ 鍕鹃€夋鈥濊缃潯鐩€?
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
	/// 娣诲姞鈥滄爣棰?+ 瀹樻柟鍙嶉椋庢牸鎸夐挳鈥濊缃潯鐩€?
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

	/// <summary>
	/// 璁剧疆鏍囩楂樺害銆?
	/// </summary>
	/// <param name="height">鐩爣楂樺害銆?/param>
	/// <returns>褰撳墠鏍囩瀹炰緥銆?/returns>
	public new SettingTab WithHeight(float height)
	{
		base.WithHeight(height);
		return this;
	}

	/// <summary>
	/// 璁剧疆鏍囩鏈€灏忛珮搴︺€?
	/// </summary>
	/// <param name="minHeight">鏈€灏忛珮搴︺€?/param>
	/// <returns>褰撳墠鏍囩瀹炰緥銆?/returns>
	public new SettingTab WithMinHeight(float minHeight)
	{
		base.WithMinHeight(minHeight);
		return this;
	}

	/// <summary>
	/// 璁剧疆鏍囩鏈€澶ч珮搴︺€?
	/// </summary>
	/// <param name="maxHeight">鏈€澶ч珮搴︺€?/param>
	/// <returns>褰撳墠鏍囩瀹炰緥銆?/returns>
	public new SettingTab WithMaxHeight(float maxHeight)
	{
		base.WithMaxHeight(maxHeight);
		return this;
	}

	/// <summary>
	/// 璁剧疆鏍囩閿氱偣棰勮銆?
	/// </summary>
	/// <param name="preset">閿氱偣棰勮銆?/param>
	/// <returns>褰撳墠鏍囩瀹炰緥銆?/returns>
	public new SettingTab WithAnchor(UiAnchorPreset preset)
	{
		base.WithAnchor(preset);
		return this;
	}

	/// <summary>
	/// 璁剧疆鏍囩鍋忕Щ閲忋€?
	/// </summary>
	/// <param name="x">X 鏂瑰悜鍋忕Щ銆?/param>
	/// <param name="y">Y 鏂瑰悜鍋忕Щ銆?/param>
	/// <returns>褰撳墠鏍囩瀹炰緥銆?/returns>
	public new SettingTab WithPositionOffset(float x, float y)
	{
		base.WithPositionOffset(x, y);
		return this;
	}

	/// <summary>
	/// 璁剧疆鏍囩鍋忕Щ鍚戦噺銆?
	/// </summary>
	/// <param name="offset">鍋忕Щ鍚戦噺銆?/param>
	/// <returns>褰撳墠鏍囩瀹炰緥銆?/returns>
	public new SettingTab WithPositionOffset(Vector2 offset)
	{
		base.WithPositionOffset(offset);
		return this;
	}

	/// <summary>
	/// 鏇存柊鏍囩鏄剧ず鏂囨湰銆?
	/// </summary>
	/// <param name="text">鏂版枃鏈€?/param>
	/// <returns>褰撳墠鏍囩瀹炰緥銆?/returns>
	public SettingTab WithText(string text)
	{
		_text = text;
		if (BuiltControl is NSettingsTab tab && GodotObject.IsInstanceValid(tab))
		{
			ApplyStateOrSchedule(tab);
		}
		return this;
	}

	/// <summary>
	/// 璁剧疆鏍囩鏄惁閫変腑銆?
	/// </summary>
	/// <param name="selected">鏄惁閫変腑銆?/param>
	/// <returns>褰撳墠鏍囩瀹炰緥銆?/returns>
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

