#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;
using ReForgeFramework.Settings.Controls;

namespace ReForgeFramework.Settings.SystemAreas;

/// <summary>
/// 设置页 Tab 注入宿主：注册新 Tab，并将自定义内容绑定到官方设置页滚动内容区。
/// </summary>
public sealed class SettingTabPanelHost
{
	private const string MetaSwitchBoundKey = "__reforge_setting_tab_switch_bound";
	private const string MetaPagerButtonBoundKey = "__reforge_setting_tab_pager_button_bound";
	private const string MetaPagerTabBoundKey = "__reforge_setting_tab_pager_tab_bound";
	private const string MetaPagerArrowReadyHookBoundKey = "__reforge_setting_tab_pager_arrow_ready_hook_bound";
	private const string MetaPagerButtonManagerIdKey = "__reforge_setting_tab_pager_button_manager_id";
	private const string MetaPagerButtonGuiInputBoundKey = "__reforge_setting_tab_pager_button_gui_input_bound";
	private const string MetaBindRetryScheduledKey = "__reforge_setting_tab_bind_retry_scheduled";
	private const string MetaWaitManagerLoggedKey = "__reforge_setting_tab_wait_manager_logged";
	private const string SafetyPlaceholderName = "ReForgeSettingScreenSafetyPlaceholder";
	private const string SettingsTabPrevButtonName = "ReForgeSettingTabPagePrevButton";
	private const string SettingsTabNextButtonName = "ReForgeSettingTabPageNextButton";
	private const string SettingsTabPagerArrowScenePath = "res://scenes/screens/run_history_screen/run_history_arrow.tscn";
	private const int SettingsTabPageSize = 4;
	private const float SettingsTabPagerReservedWidth = 60f;
	private const float SettingsTabPagerButtonWidth = 52f;

	private readonly Dictionary<string, SettingTab> _tabsByScreenKey = new(StringComparer.OrdinalIgnoreCase);
	private static readonly Dictionary<ulong, int> _currentPageByManager = new();
	private static readonly Dictionary<ulong, Vector2> _originalHorizontalOffsetsByManager = new();
	private static readonly Dictionary<ulong, ulong> _lastPagerInvokeMsByButton = new();

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
		EnsurePaginationForManager(manager);
		RefreshPagination(manager);

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

		bool bound = TryConnectReleasedSignal(tab, () =>
		{
			object? ignoredResult = switchTabTo.Invoke(manager, new object?[] { tab });
		});

		if (!bound)
		{
			bound = TryConnectActionSignal(tab, "pressed", () =>
			{
				object? ignoredResult = switchTabTo.Invoke(manager, new object?[] { tab });
			});
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

	private static void EnsurePaginationForManager(NSettingsTabManager manager)
	{
		if (manager.GetParent() is not Control parent)
		{
			return;
		}

		ulong managerId = manager.GetInstanceId();
		if (!_originalHorizontalOffsetsByManager.ContainsKey(managerId))
		{
			_originalHorizontalOffsetsByManager[managerId] = new Vector2(manager.OffsetLeft, manager.OffsetRight);
		}

		Control prevButton = EnsurePaginationButton(parent, SettingsTabPrevButtonName, isLeft: true, managerId);
		Control nextButton = EnsurePaginationButton(parent, SettingsTabNextButtonName, isLeft: false, managerId);
		LayoutPaginationButtons(manager, prevButton, nextButton);
		BindPaginationButtonPressed(prevButton, () => ChangePage(manager, -1));
		BindPaginationButtonPressed(nextButton, () => ChangePage(manager, +1));
	}

	private static Control EnsurePaginationButton(Control parent, string name, bool isLeft, ulong managerId)
	{
		Control? existing = parent.GetNodeOrNull<Control>(name);
		if (existing != null && existing.HasMeta(MetaPagerButtonManagerIdKey))
		{
			string boundManagerId = existing.GetMeta(MetaPagerButtonManagerIdKey).ToString();
			if (!string.Equals(boundManagerId, managerId.ToString(), StringComparison.Ordinal))
			{
				existing.QueueFree();
				existing = null;
			}
		}

		Control button = existing ?? CreatePaginationButton(isLeft);
		button.Name = name;
		button.FocusMode = Control.FocusModeEnum.All;
		button.MouseFilter = Control.MouseFilterEnum.Stop;
		button.ZIndex = 310;
		button.SetMeta(MetaPagerButtonManagerIdKey, managerId.ToString());

		if (button is Godot.Button fallbackButton)
		{
			fallbackButton.Text = isLeft ? "<" : ">";
			fallbackButton.Flat = true;
		}

		TrySetArrowDirection(button, isLeft);

		if (button.GetParent() == null)
		{
			parent.AddChild(button);
		}

		if (!button.HasMeta("__reforge_setting_tab_pager_cleanup_bound"))
		{
			button.SetMeta("__reforge_setting_tab_pager_cleanup_bound", true);
			button.TreeExiting += () => _lastPagerInvokeMsByButton.Remove(button.GetInstanceId());
		}

		return button;
	}

	private static Control CreatePaginationButton(bool isLeft)
	{
		if (ResourceLoader.Exists(SettingsTabPagerArrowScenePath))
		{
			PackedScene? scene = ResourceLoader.Load<PackedScene>(SettingsTabPagerArrowScenePath);
			if (scene?.Instantiate() is Control officialArrow)
			{
				TrySetArrowDirection(officialArrow, isLeft);
				return officialArrow;
			}
		}

		return new Godot.Button
		{
			Text = isLeft ? "<" : ">",
			FocusMode = Control.FocusModeEnum.All
		};
	}

	private static void TrySetArrowDirection(Control button, bool isLeft)
	{
		PropertyInfo? isLeftProperty = button.GetType().GetProperty("IsLeft", BindingFlags.Instance | BindingFlags.Public);
		if (isLeftProperty?.CanWrite == true && isLeftProperty.PropertyType == typeof(bool))
		{
			if (button.IsNodeReady())
			{
				isLeftProperty.SetValue(button, isLeft);
				return;
			}

			if (!button.HasMeta(MetaPagerArrowReadyHookBoundKey))
			{
				button.SetMeta(MetaPagerArrowReadyHookBoundKey, true);
				button.Connect(Node.SignalName.Ready, Callable.From(() =>
				{
					if (!GodotObject.IsInstanceValid(button))
					{
						return;
					}

					PropertyInfo? lateIsLeftProperty = button.GetType().GetProperty("IsLeft", BindingFlags.Instance | BindingFlags.Public);
					if (lateIsLeftProperty?.CanWrite == true && lateIsLeftProperty.PropertyType == typeof(bool))
					{
						lateIsLeftProperty.SetValue(button, isLeft);
					}
				}), (uint)GodotObject.ConnectFlags.OneShot);
			}

			return;
		}

		if (button.GetNodeOrNull<TextureRect>("TextureRect") is TextureRect icon)
		{
			icon.FlipH = !isLeft;
		}
	}

	private static void LayoutPaginationButtons(NSettingsTabManager manager, Control prevButton, Control nextButton)
	{
		ulong managerId = manager.GetInstanceId();
		if (!_originalHorizontalOffsetsByManager.TryGetValue(managerId, out Vector2 originalOffsets))
		{
			originalOffsets = new Vector2(manager.OffsetLeft, manager.OffsetRight);
			_originalHorizontalOffsetsByManager[managerId] = originalOffsets;
		}

		ApplyPagerButtonVerticalLayout(manager, prevButton);
		prevButton.AnchorLeft = manager.AnchorLeft;
		prevButton.AnchorRight = manager.AnchorLeft;
		prevButton.OffsetLeft = originalOffsets.X;
		prevButton.OffsetRight = originalOffsets.X + SettingsTabPagerButtonWidth;

		ApplyPagerButtonVerticalLayout(manager, nextButton);
		nextButton.AnchorLeft = manager.AnchorRight;
		nextButton.AnchorRight = manager.AnchorRight;
		nextButton.OffsetLeft = originalOffsets.Y - SettingsTabPagerButtonWidth;
		nextButton.OffsetRight = originalOffsets.Y;
	}

	private static void ApplyPagerButtonVerticalLayout(Control manager, Control button)
	{
		button.AnchorTop = manager.AnchorTop;
		button.AnchorBottom = manager.AnchorBottom;
		button.OffsetTop = manager.OffsetTop;
		button.OffsetBottom = manager.OffsetBottom;
	}

	private static void BindPaginationButtonPressed(Control button, Action handler)
	{
		if (button.HasMeta(MetaPagerButtonBoundKey))
		{
			return;
		}

		button.SetMeta(MetaPagerButtonBoundKey, true);
		Action safeHandler = WrapDebouncedPagerHandler(button, handler);
		Control clickSource = ResolveClickSource(button);

		if (clickSource is BaseButton baseButton)
		{
			baseButton.Pressed += safeHandler;
			return;
		}

		bool signalBound = TryConnectReleasedSignal(clickSource, safeHandler);
		if (!signalBound)
		{
			signalBound = TryConnectActionSignal(clickSource, "pressed", safeHandler);
		}

		if (!signalBound)
		{
			GD.Print("[ReForge.Settings] Pager button signal binding failed, fallback to gui_input only.");
		}

		BindPaginationGuiInputFallback(clickSource, safeHandler);
	}

	private static Action WrapDebouncedPagerHandler(Control button, Action handler)
	{
		return () =>
		{
			if (!GodotObject.IsInstanceValid(button))
			{
				return;
			}

			ulong buttonId = button.GetInstanceId();
			ulong now = Time.GetTicksMsec();
			if (_lastPagerInvokeMsByButton.TryGetValue(buttonId, out ulong lastInvoke)
				&& now - lastInvoke < 120)
			{
				return;
			}

			_lastPagerInvokeMsByButton[buttonId] = now;
			handler();
		};
	}

	private static void BindPaginationGuiInputFallback(Control button, Action handler)
	{
		if (button.HasMeta(MetaPagerButtonGuiInputBoundKey))
		{
			return;
		}

		Error connectResult = button.Connect(Control.SignalName.GuiInput, Callable.From<InputEvent>(inputEvent =>
		{
			if (inputEvent is not InputEventMouseButton mouseButton)
			{
				return;
			}

			if (mouseButton.ButtonIndex != MouseButton.Left || !mouseButton.Pressed)
			{
				return;
			}

			handler();
		}));

		if (connectResult == Error.Ok)
		{
			button.SetMeta(MetaPagerButtonGuiInputBoundKey, true);
		}
	}

	private static void ChangePage(NSettingsTabManager manager, int delta)
	{
		if (!GodotObject.IsInstanceValid(manager))
		{
			return;
		}

		ulong managerId = manager.GetInstanceId();
		int currentPage = _currentPageByManager.TryGetValue(managerId, out int existingPage) ? existingPage : 0;
		_currentPageByManager[managerId] = currentPage + delta;
		RefreshPagination(manager);
	}

	private static void RefreshPagination(NSettingsTabManager manager)
	{
		List<NSettingsTab> tabs = CollectDirectSettingTabs(manager);
		foreach (NSettingsTab tab in tabs)
		{
			BindTabPageSyncSignal(manager, tab);
		}

		int totalPages = Mathf.Max(1, Mathf.CeilToInt(tabs.Count / (float)SettingsTabPageSize));
		ulong managerId = manager.GetInstanceId();
		int currentPage = _currentPageByManager.TryGetValue(managerId, out int existingPage) ? existingPage : 0;
		currentPage = Mathf.Clamp(currentPage, 0, totalPages - 1);
		_currentPageByManager[managerId] = currentPage;

		bool pagingEnabled = tabs.Count > SettingsTabPageSize;
		ApplyPaginationLayout(manager, pagingEnabled);

		int pageStart = currentPage * SettingsTabPageSize;
		int pageEndExclusive = pageStart + SettingsTabPageSize;
		for (int i = 0; i < tabs.Count; i++)
		{
			tabs[i].Visible = !pagingEnabled || (i >= pageStart && i < pageEndExclusive);
		}

		if (manager.GetParent() is not Control parent)
		{
			return;
		}

		Control? prevButton = parent.GetNodeOrNull<Control>(SettingsTabPrevButtonName);
		if (prevButton != null)
		{
			prevButton.Visible = pagingEnabled;
			SetButtonDisabled(prevButton, !pagingEnabled || currentPage <= 0);
		}

		Control? nextButton = parent.GetNodeOrNull<Control>(SettingsTabNextButtonName);
		if (nextButton != null)
		{
			nextButton.Visible = pagingEnabled;
			SetButtonDisabled(nextButton, !pagingEnabled || currentPage >= totalPages - 1);
		}
	}

	private static void ApplyPaginationLayout(NSettingsTabManager manager, bool pagingEnabled)
	{
		ulong managerId = manager.GetInstanceId();
		if (!_originalHorizontalOffsetsByManager.TryGetValue(managerId, out Vector2 originalOffsets))
		{
			originalOffsets = new Vector2(manager.OffsetLeft, manager.OffsetRight);
			_originalHorizontalOffsetsByManager[managerId] = originalOffsets;
		}

		if (pagingEnabled)
		{
			manager.OffsetLeft = originalOffsets.X + SettingsTabPagerReservedWidth;
			manager.OffsetRight = originalOffsets.Y - SettingsTabPagerReservedWidth;
			return;
		}

		manager.OffsetLeft = originalOffsets.X;
		manager.OffsetRight = originalOffsets.Y;
	}

	private static List<NSettingsTab> CollectDirectSettingTabs(NSettingsTabManager manager)
	{
		List<NSettingsTab> tabs = new();
		foreach (Node child in manager.GetChildren())
		{
			if (child is NSettingsTab tab)
			{
				tabs.Add(tab);
			}
		}

		return tabs;
	}

	private static void BindTabPageSyncSignal(NSettingsTabManager manager, NSettingsTab tab)
	{
		if (tab.HasMeta(MetaPagerTabBoundKey))
		{
			return;
		}

		tab.SetMeta(MetaPagerTabBoundKey, true);

		void HandleTabActivation()
		{
			if (!GodotObject.IsInstanceValid(manager) || !GodotObject.IsInstanceValid(tab))
			{
				return;
			}

			List<NSettingsTab> latestTabs = CollectDirectSettingTabs(manager);
			int tabIndex = latestTabs.IndexOf(tab);
			if (tabIndex < 0)
			{
				return;
			}

			_currentPageByManager[manager.GetInstanceId()] = tabIndex / SettingsTabPageSize;
			RefreshPagination(manager);
		}

		if (TryConnectReleasedSignal(tab, HandleTabActivation))
		{
			return;
		}

		TryConnectActionSignal(tab, "pressed", HandleTabActivation);
	}

	private static bool TryConnectActionSignal(Control source, string signalName, Action handler)
	{
		if (!source.HasSignal(signalName))
		{
			return false;
		}

		if (source.Connect(signalName, Callable.From(handler)) == Error.Ok)
		{
			return true;
		}

		if (source.Connect(signalName, Callable.From<GodotObject>(_ => handler())) == Error.Ok)
		{
			return true;
		}

		return false;
	}

	private static bool TryConnectReleasedSignal(Control source, Action handler)
	{
		if (source is NClickableControl clickable)
		{
			Error clickableConnect = clickable.Connect(NClickableControl.SignalName.Released, Callable.From<NClickableControl>(_ => handler()));
			if (clickableConnect == Error.Ok)
			{
				return true;
			}
		}

		if (TryConnectActionSignal(source, "released", handler))
		{
			return true;
		}

		return false;
	}

	private static Control ResolveClickSource(Control root)
	{
		if (root is NClickableControl)
		{
			return root;
		}

		NClickableControl? clickableChild = FindFirstClickableDescendant(root);
		return clickableChild ?? root;
	}

	private static NClickableControl? FindFirstClickableDescendant(Node node)
	{
		foreach (Node child in node.GetChildren())
		{
			if (child is NClickableControl clickable)
			{
				return clickable;
			}

			NClickableControl? nested = FindFirstClickableDescendant(child);
			if (nested != null)
			{
				return nested;
			}
		}

		return null;
	}

	private static void SetButtonDisabled(Control button, bool disabled)
	{
		if (button is BaseButton baseButton)
		{
			baseButton.Disabled = disabled;
		}

		try
		{
			PropertyInfo? isEnabledProperty = button.GetType().GetProperty("IsEnabled", BindingFlags.Instance | BindingFlags.Public);
			if (isEnabledProperty?.CanWrite == true && isEnabledProperty.PropertyType == typeof(bool))
			{
				isEnabledProperty.SetValue(button, !disabled);
			}

			MethodInfo? toggler = button.GetType().GetMethod(disabled ? "Disable" : "Enable", BindingFlags.Instance | BindingFlags.Public);
			toggler?.Invoke(button, null);
		}
		catch
		{
			// 某些控件没有 Enable/Disable 或 IsEnabled，忽略即可。
		}
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

