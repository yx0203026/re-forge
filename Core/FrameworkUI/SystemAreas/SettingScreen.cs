#nullable enable

using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;
using ReForgeFramework.UI.Abstractions;

namespace ReForgeFramework.UI.SystemAreas;

/// <summary>
/// 自定义设置页内容容器：支持添加 UI 条目并绑定到官方 NSettingsPanel。
/// </summary>
public sealed class SettingScreen
{
	private const string SafetyPlaceholderName = "ReForgeSettingScreenSafetyPlaceholder";

	private readonly List<IUiElement> _entries = new();
	private readonly HashSet<IUiElement> _attachedEntries = new();

	private NSettingsPanel? _panel;
	private VBoxContainer? _content;

	internal SettingScreen(string key)
	{
		Key = key;
	}

	public string Key { get; }

	public SettingScreen Add(IUiElement entry)
	{
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

	internal void BindPanel(NSettingsPanel panel)
	{
		_panel = panel;
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
			_attachedEntries.Add(entry);
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

		_attachedEntries.Add(entry);
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
