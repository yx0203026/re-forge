#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;
using ReForgeFramework.Settings.Controls;

namespace ReForgeFramework.Settings.SystemAreas;

/// <summary>
/// 设置页 Tab 注入宿主：注册新 Tab，并将自定义内容绑定到官方设置页滚动内容区。
/// </summary>
public sealed class SettingTabPanelHost
{
	private const string MetaSwitchBoundKey = "__reforge_setting_tab_switch_bound";
	private const string MetaBindRetryScheduledKey = "__reforge_setting_tab_bind_retry_scheduled";
	private const string MetaWaitManagerLoggedKey = "__reforge_setting_tab_wait_manager_logged";
	private const string SafetyPlaceholderName = "ReForgeSettingScreenSafetyPlaceholder";

	private readonly Dictionary<string, SettingTab> _tabsByScreenKey = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// 添加或复用一个设置标签并尝试挂载到官方设置页。
	/// </summary>
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

		MountTab(tab);
	}

	internal void RemountAll()
	{
		foreach (SettingTab tab in _tabsByScreenKey.Values)
		{
			MountTab(tab);
		}
	}

	/// <summary>
	/// 按 screenKey 获取已注册的设置标签。
	/// </summary>
	public SettingTab? GetSettingTab(string screenKey)
	{
		if (string.IsNullOrWhiteSpace(screenKey))
		{
			return null;
		}

		return _tabsByScreenKey.TryGetValue(screenKey, out SettingTab? tab) ? tab : null;
	}

	private void MountTab(SettingTab tabModel)
	{
		Control built = tabModel.Build();
		if (built is not NSettingsTab runtimeTab)
		{
			GD.PrintErr($"[ReForge.Settings] Failed to inject tab '{tabModel.ScreenKey}' because built control is not NSettingsTab.");
			return;
		}

		void TryMountAndBind()
		{
			if (!GodotObject.IsInstanceValid(runtimeTab))
			{
				return;
			}

			if (!TryAttachToManager(runtimeTab, out NSettingsTabManager? manager) || manager == null)
			{
				if (!runtimeTab.HasMeta(MetaWaitManagerLoggedKey))
				{
					GD.Print($"[ReForge.Settings] Waiting for NSettingsTabManager to bind screen '{tabModel.ScreenKey}'.");
					runtimeTab.SetMeta(MetaWaitManagerLoggedKey, true);
				}

				ScheduleRetry();
				return;
			}

			runtimeTab.SetMeta(MetaWaitManagerLoggedKey, false);
			if (TryBindToOfficialManager(manager, runtimeTab, tabModel, tabModel.SelectedByDefault))
			{
				return;
			}

			ScheduleRetry();
		}

		void ScheduleRetry()
		{
			if (!GodotObject.IsInstanceValid(runtimeTab))
			{
				return;
			}

			if (runtimeTab.HasMeta(MetaBindRetryScheduledKey))
			{
				return;
			}

			if (Engine.GetMainLoop() is not SceneTree tree)
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
				TryMountAndBind();
			}), (uint)GodotObject.ConnectFlags.OneShot);
		}

		TryMountAndBind();
	}

	private static bool TryAttachToManager(NSettingsTab tab, out NSettingsTabManager? manager)
	{
		manager = FindSettingsTabManager(tab);
		if (manager == null)
		{
			return false;
		}

		if (tab.GetParent() == manager)
		{
			return true;
		}

		if (tab.GetParent() == null)
		{
			manager.AddChild(tab);
		}
		else
		{
			tab.Reparent(manager, keepGlobalTransform: true);
		}

		return true;
	}

	private static bool TryBindToOfficialManager(NSettingsTabManager manager, NSettingsTab tab, SettingTab tabModel, bool selectWhenBound)
	{
		if (!GodotObject.IsInstanceValid(tab) || !GodotObject.IsInstanceValid(manager))
		{
			return false;
		}

		if (!tab.IsNodeReady() || !manager.IsNodeReady())
		{
			return false;
		}

		if (!TryGetTabsDictionary(manager, out IDictionary? tabs) || tabs == null)
		{
			GD.PrintErr($"[ReForge.Settings] Cannot access NSettingsTabManager._tabs for screen '{tabModel.ScreenKey}'.");
			return false;
		}

		NSettingsPanel? panel;
		if (tabs.Contains(tab))
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
			GD.PrintErr($"[ReForge.Settings] Failed to create/bind custom settings panel for '{tabModel.ScreenKey}'.");
			return false;
		}

		tabModel.BindPanel(panel);
		BindSwitchSignalIfNeeded(manager, tab);

		if (selectWhenBound)
		{
			SelectTabDeferred(manager, tab);
		}

		return true;
	}

	private static void BindSwitchSignalIfNeeded(NSettingsTabManager manager, NSettingsTab tab)
	{
		if (tab.HasMeta(MetaSwitchBoundKey))
		{
			return;
		}

		MethodInfo? switchTabTo = manager.GetType().GetMethod(
			"SwitchTabTo",
			BindingFlags.Instance | BindingFlags.NonPublic,
			null,
			new[] { typeof(NSettingsTab) },
			null);

		if (switchTabTo == null)
		{
			return;
		}

		bool bound = false;
		if (tab.HasSignal("released"))
		{
			Error connectReleased = tab.Connect("released", Callable.From(() =>
			{
				object? ignoredResult = switchTabTo.Invoke(manager, new object?[] { tab });
			}));

			bound = connectReleased == Error.Ok;
		}

		if (!bound && tab.HasSignal("pressed"))
		{
			Error connectPressed = tab.Connect("pressed", Callable.From(() =>
			{
				object? ignoredResult = switchTabTo.Invoke(manager, new object?[] { tab });
			}));

			bound = connectPressed == Error.Ok;
		}

		if (!bound)
		{
			GD.PrintErr("[ReForge.Settings] Failed to bind tab switch signal for custom setting tab.");
			return;
		}

		tab.SetMeta(MetaSwitchBoundKey, true);
	}

	private static void SelectTabDeferred(NSettingsTabManager manager, NSettingsTab tab)
	{
		MethodInfo? switchTabTo = manager.GetType().GetMethod(
			"SwitchTabTo",
			BindingFlags.Instance | BindingFlags.NonPublic,
			null,
			new[] { typeof(NSettingsTab) },
			null);

		if (switchTabTo == null)
		{
			return;
		}

		Callable.From(() =>
		{
			if (!GodotObject.IsInstanceValid(tab) || !tab.IsNodeReady() || !GodotObject.IsInstanceValid(manager))
			{
				return;
			}

			object? ignoredResult = switchTabTo.Invoke(manager, new object?[] { tab });
		}).CallDeferred();
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
		Godot.Button placeholder = new()
		{
			Name = SafetyPlaceholderName,
			Text = string.Empty,
			Flat = true,
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
}

