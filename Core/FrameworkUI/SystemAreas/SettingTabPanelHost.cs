#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;
using ReForgeFramework.UI.Controls;
using ReForgeFramework.UI.Runtime;

namespace ReForgeFramework.UI.SystemAreas;

/// <summary>
/// 设置页 Tab 注入宿主：注册新 Tab，并将自定义内容绑定到官方设置页滚动内容区。
/// </summary>
public sealed class SettingTabPanelHost
{
	private const string MetaSwitchBoundKey = "__reforge_setting_tab_switch_bound";
	private const string MetaBindCompletedKey = "__reforge_setting_tab_bind_completed";
	private const string MetaBindRetryScheduledKey = "__reforge_setting_tab_bind_retry_scheduled";
	private const string MetaWaitManagerLoggedKey = "__reforge_setting_tab_wait_manager_logged";
	private const string SafetyPlaceholderName = "ReForgeSettingScreenSafetyPlaceholder";

	private readonly Dictionary<string, SettingScreen> _screens = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, SettingTab> _tabsByScreenKey = new(StringComparer.OrdinalIgnoreCase);

	public void AddChild(SettingTab tab)
	{
		ArgumentNullException.ThrowIfNull(tab);

		if (_tabsByScreenKey.TryGetValue(tab.ScreenKey, out SettingTab? existing))
		{
			tab = existing;
		}
		else
		{
			_tabsByScreenKey[tab.ScreenKey] = tab;
		}

		_ = GetOrCreateScreen(tab.ScreenKey);
		MountTab(tab);
	}

	internal void RemountAll()
	{
		foreach (SettingTab tab in _tabsByScreenKey.Values)
		{
			MountTab(tab);
		}
	}

	private void MountTab(SettingTab tab)
	{
		SettingScreen screen = GetOrCreateScreen(tab.ScreenKey);

		Control built = tab.Build();
		UiRuntimeNode.Ensure().MountToArea(SystemUiArea.SettingTabPanel, built);
		if (built is not NSettingsTab runtimeTab)
		{
			GD.Print($"[ReForge.UI] Failed to inject tab '{tab.ScreenKey}' because built control is not NSettingsTab.");
			return;
		}

		GD.Print($"[ReForge.UI] Injecting settings tab '{tab.ScreenKey}'.");

		void TryBind()
		{
			if (!GodotObject.IsInstanceValid(runtimeTab))
			{
				return;
			}

			if (TryBindToOfficialManager(runtimeTab, screen, tab.SelectedByDefault))
			{
				return;
			}

			ScheduleBindRetry();
		}

		void ScheduleBindRetry()
		{
			if (!GodotObject.IsInstanceValid(runtimeTab) || !runtimeTab.IsInsideTree())
			{
				return;
			}

			if (runtimeTab.HasMeta(MetaBindRetryScheduledKey))
			{
				return;
			}

			if (runtimeTab.GetTree() is not SceneTree tree)
			{
				return;
			}

			runtimeTab.SetMeta(MetaBindRetryScheduledKey, true);
			tree.Connect(SceneTree.SignalName.ProcessFrame, Callable.From(() =>
			{
				if (!GodotObject.IsInstanceValid(runtimeTab))
				{
					return;
				}

				runtimeTab.SetMeta(MetaBindRetryScheduledKey, false);
				TryBind();
			}), (uint)GodotObject.ConnectFlags.OneShot);
		}

		runtimeTab.TreeEntered += TryBind;
		TryBind();
	}

	private SettingScreen GetOrCreateScreen(string screenKey)
	{
		if (!_screens.TryGetValue(screenKey, out SettingScreen? screen))
		{
			screen = new SettingScreen(screenKey);
			_screens[screenKey] = screen;
		}

		return screen;
	}

	public SettingScreen? GetSettingScreen(string screenKey)
	{
		if (string.IsNullOrWhiteSpace(screenKey))
		{
			return null;
		}

		return _screens.TryGetValue(screenKey, out SettingScreen? screen) ? screen : null;
	}

	private static bool TryBindToOfficialManager(NSettingsTab tab, SettingScreen screen, bool selectWhenBound)
	{
		if (!GodotObject.IsInstanceValid(tab))
		{
			return false;
		}

		if (tab.HasMeta(MetaBindCompletedKey))
		{
			return true;
		}

		if (!tab.IsNodeReady())
		{
			return false;
		}

		NSettingsTabManager? manager = FindSettingsTabManager(tab);
		if (manager == null)
		{
			if (!tab.HasMeta(MetaWaitManagerLoggedKey))
			{
				GD.Print($"[ReForge.UI] Waiting for NSettingsTabManager to bind screen '{screen.Key}'.");
				tab.SetMeta(MetaWaitManagerLoggedKey, true);
			}
			return false;
		}

		if (!manager.IsNodeReady())
		{
			return false;
		}

		if (!TryGetTabsDictionary(manager, out IDictionary? tabs))
		{
			GD.Print($"[ReForge.UI] Cannot access NSettingsTabManager._tabs for screen '{screen.Key}'.");
			return false;
		}

		NSettingsPanel? panel;
		if (tabs!.Contains(tab))
		{
			panel = tabs[tab] as NSettingsPanel;
		}
		else
		{
			panel = CreateCustomSettingsPanel(tabs);
			if (panel == null)
			{
				return false;
			}

			tabs[tab] = panel;
		}

		if (panel == null)
		{
			GD.Print($"[ReForge.UI] Failed to create/bind custom settings panel for '{screen.Key}'.");
			return false;
		}

		screen.BindPanel(panel);
		tab.SetMeta(MetaBindCompletedKey, true);
		tab.SetMeta(MetaWaitManagerLoggedKey, false);
		GD.Print($"[ReForge.UI] Bound custom settings screen '{screen.Key}' to panel '{panel.Name}'.");
		if (!tab.HasMeta(MetaSwitchBoundKey))
		{
			MethodInfo? switchTabTo = manager.GetType().GetMethod("SwitchTabTo", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(NSettingsTab) }, null);
			if (switchTabTo != null)
			{
				tab.Connect(NClickableControl.SignalName.Released, Callable.From<GodotObject>(_ =>
				{
					object? ignoredResult = switchTabTo.Invoke(manager, new object?[] { tab });
				}));
				tab.SetMeta(MetaSwitchBoundKey, true);
			}
		}

		if (selectWhenBound)
		{
			MethodInfo? switchTabTo = manager.GetType().GetMethod("SwitchTabTo", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(NSettingsTab) }, null);
			if (switchTabTo != null)
			{
				Callable.From(() =>
				{
					if (!GodotObject.IsInstanceValid(tab) || !tab.IsNodeReady() || !GodotObject.IsInstanceValid(manager))
					{
						return;
					}

					object? ignoredResult = switchTabTo.Invoke(manager, new object?[] { tab });
				}).CallDeferred();
			}
		}

		return true;
	}

	private static NSettingsPanel? CreateCustomSettingsPanel(IDictionary tabs)
	{
		NSettingsPanel? template = null;
		foreach (DictionaryEntry entry in tabs)
		{
			if (entry.Value is NSettingsPanel existingPanel)
			{
				template = existingPanel;
				break;
			}
		}

		if (template == null)
		{
			return null;
		}

		NSettingsPanel panel = new()
		{
			Name = $"ReForgeSettingsPanel{tabs.Count}",
			Visible = false
		};

		CopyLayout(template, panel);

		VBoxContainer content = new()
		{
			Name = "VBoxContainer",
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsVertical = Control.SizeFlags.ShrinkBegin
		};

		if (template.GetNodeOrNull<VBoxContainer>("VBoxContainer") is { } templateContent)
		{
			CopyLayout(templateContent, content);
		}

		panel.AddChild(content);

		// NSettingsPanel._Ready 要求至少有一个可聚焦设置项，否则 list.First() 会抛异常。
		NButton placeholder = new()
		{
			Name = SafetyPlaceholderName,
			Visible = false,
			FocusMode = Control.FocusModeEnum.All,
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		content.AddChild(placeholder);

		if (template.GetParent() is Node parent)
		{
			parent.AddChild(panel);
			parent.MoveChild(panel, parent.GetChildCount() - 1);
		}

		return panel;
	}

	private static void CopyLayout(Control source, Control target)
	{
		target.AnchorLeft = source.AnchorLeft;
		target.AnchorTop = source.AnchorTop;
		target.AnchorRight = source.AnchorRight;
		target.AnchorBottom = source.AnchorBottom;
		target.OffsetLeft = source.OffsetLeft;
		target.OffsetTop = source.OffsetTop;
		target.OffsetRight = source.OffsetRight;
		target.OffsetBottom = source.OffsetBottom;
		target.GrowHorizontal = source.GrowHorizontal;
		target.GrowVertical = source.GrowVertical;
		target.SizeFlagsHorizontal = source.SizeFlagsHorizontal;
		target.SizeFlagsVertical = source.SizeFlagsVertical;
		target.CustomMinimumSize = source.CustomMinimumSize;
	}

	private static bool TryGetTabsDictionary(NSettingsTabManager manager, out IDictionary? tabs)
	{
		tabs = null;
		FieldInfo? tabsField = manager.GetType().GetField("_tabs", BindingFlags.Instance | BindingFlags.NonPublic);
		if (tabsField?.GetValue(manager) is IDictionary dictionary)
		{
			tabs = dictionary;
			return true;
		}

		return false;
	}

	private static NSettingsTabManager? FindSettingsTabManager(Node start)
	{
		Node? cursor = start;
		while (cursor != null)
		{
			if (cursor is NSettingsTabManager manager)
			{
				return manager;
			}

			cursor = cursor.GetParent();
		}

		if (Engine.GetMainLoop() is not SceneTree tree || tree.Root == null)
		{
			return null;
		}

		return FindSettingsTabManagerRecursive(tree.Root);
	}

	private static NSettingsTabManager? FindSettingsTabManagerRecursive(Node node)
	{
		if (node is NSettingsTabManager manager)
		{
			return manager;
		}

		foreach (Node child in node.GetChildren())
		{
			NSettingsTabManager? found = FindSettingsTabManagerRecursive(child);
			if (found != null)
			{
				return found;
			}
		}

		return null;
	}
}
