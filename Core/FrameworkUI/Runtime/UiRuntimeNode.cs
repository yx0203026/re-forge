#nullable enable

using System;
using System.Collections.Generic;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;
using ReForgeFramework.UI.SystemAreas;

namespace ReForgeFramework.UI.Runtime;

public partial class UiRuntimeNode : Node
{
	private sealed record PendingMount(SystemUiArea Area, Control Node);

	private const string RuntimeNodeName = "ReForgeUiRuntime";
	private const string GlobalLayerName = "ReForgeGlobalUiLayer";
	private const string GlobalRootName = "ReForgeGlobalUiRoot";
	private const string ReForgeNodePrefix = "ReForge";
	private static readonly string[] MainMenuPanelNodeNames =
	{
		"MainMenuTextButtons",
		"MainMenuButtons",
		"MainMenuButtonPanel"
	};
	private const string SettingsTabManagerNodeName = "SettingsTabManager";
	private const string SettingsTabScrollHostName = "ReForgeSettingTabScrollHost";
	private const string SettingsTabFadeLayerName = "ReForgeSettingTabFadeLayer";
	private const string SettingsTabPrevButtonName = "ReForgeSettingTabPagePrevButton";
	private const string SettingsTabNextButtonName = "ReForgeSettingTabPageNextButton";
	private const string SettingsTabPagerArrowScenePath = "res://scenes/screens/run_history_screen/run_history_arrow.tscn";
	private const int SettingsTabPageSize = 4;
	private const float SettingsTabPagerButtonWidth = 52f;
	private const float SettingsTabPagerReservedWidth = 60f;
	private static readonly StringName MetaSettingTabPagerButtonBound = "__reforge_setting_tab_pager_button_bound";
	private static readonly StringName MetaSettingTabPagerTabBound = "__reforge_setting_tab_pager_tab_bound";
	private static readonly StringName MetaSettingTabPagerArrowReadyHookBound = "__reforge_setting_tab_pager_arrow_ready_hook_bound";
	private static readonly Dictionary<ulong, int> _settingTabCurrentPageByManager = new();
	private static readonly Dictionary<ulong, Vector2> _settingTabOriginalHorizontalOffsetsByManager = new();

	private static UiRuntimeNode? _instance;

	private readonly List<PendingMount> _pendingMounts = new();
	private bool _attachScheduled;
	private bool _pendingFlushScheduled;
	private double _pendingRetryElapsed;
	private const double PendingRetryIntervalSeconds = 0.25;

	private CanvasLayer? _globalLayer;
	private Control? _globalRoot;

	public static UiRuntimeNode Ensure()
	{
		if (GodotObject.IsInstanceValid(_instance))
		{
			_instance!.EnsureAttachedToRoot();
			return _instance!;
		}

		if (Engine.GetMainLoop() is not SceneTree tree || tree.Root == null)
		{
			throw new System.InvalidOperationException("SceneTree is not ready, cannot initialize UI runtime.");
		}

		UiRuntimeNode? existing = tree.Root.GetNodeOrNull<UiRuntimeNode>(RuntimeNodeName);
		if (existing != null)
		{
			_instance = existing;
			_instance.EnsureAttachedToRoot();
			return _instance;
		}

		_instance = new UiRuntimeNode
		{
			Name = RuntimeNodeName,
			ProcessMode = ProcessModeEnum.Always
		};

		_instance.EnsureAttachedToRoot();
		return _instance;
	}

	public override void _EnterTree()
	{
		_attachScheduled = false;
		SchedulePendingFlush();
	}

	public override void _Process(double delta)
	{
		if (_pendingMounts.Count == 0)
		{
			_pendingRetryElapsed = 0;
			return;
		}

		_pendingRetryElapsed += delta;
		if (_pendingRetryElapsed < PendingRetryIntervalSeconds)
		{
			return;
		}

		_pendingRetryElapsed = 0;
		FlushPendingMounts();
	}

	public void MountGlobal(Control node)
	{
		if (!GodotObject.IsInstanceValid(node))
		{
			return;
		}

		EnsureGlobalRoot();
		if (_globalRoot == null)
		{
			return;
		}

		AttachControl(_globalRoot, node);
	}

	public void MountToArea(SystemUiArea area, Control node)
	{
		if (!GodotObject.IsInstanceValid(node))
		{
			return;
		}

		if (TryResolveArea(area, out Control? host))
		{
			if (area == SystemUiArea.SettingTabPanel)
			{
				AttachSettingTab(host, node);
				return;
			}

			AttachControl(host, node);
			return;
		}

		for (int i = 0; i < _pendingMounts.Count; i++)
		{
			PendingMount pending = _pendingMounts[i];
			if (pending.Area == area && ReferenceEquals(pending.Node, node))
			{
				return;
			}
		}

		_pendingMounts.Add(new PendingMount(area, node));
		GD.Print($"[ReForge.UI] Area '{area}' unavailable, queued mount for '{node.Name}'.");
		SchedulePendingFlush();
	}

	private void EnsureAttachedToRoot()
	{
		if (GetParent() != null)
		{
			_attachScheduled = false;
			return;
		}

		if (_attachScheduled)
		{
			return;
		}

		if (Engine.GetMainLoop() is not SceneTree tree || tree.Root == null)
		{
			return;
		}

		// 优先尝试延迟挂载，保证在主循环节点树稳定后进入场景。
		_attachScheduled = true;
		tree.Root.CallDeferred(Node.MethodName.AddChild, this);
		GD.Print("[ReForge.UI] UiRuntimeNode scheduled for deferred initialization.");
	}

	private void SchedulePendingFlush()
	{
		if (_pendingMounts.Count == 0 || _pendingFlushScheduled || !IsInsideTree())
		{
			return;
		}

		_pendingFlushScheduled = true;
		CallDeferred(nameof(FlushPendingMountsDeferred));
	}

	private void FlushPendingMountsDeferred()
	{
		_pendingFlushScheduled = false;
		if (!IsInsideTree() || _pendingMounts.Count == 0)
		{
			return;
		}

		FlushPendingMounts();
	}

	private void EnsureGlobalRoot()
	{
		if (!GodotObject.IsInstanceValid(_globalLayer))
		{
			_globalLayer = new CanvasLayer
			{
				Name = GlobalLayerName,
				Layer = 200
			};
			AddChild(_globalLayer);
		}

		if (!GodotObject.IsInstanceValid(_globalRoot))
		{
			_globalRoot = new Control
			{
				Name = GlobalRootName,
				MouseFilter = Control.MouseFilterEnum.Ignore
			};
			_globalRoot.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
			_globalLayer!.AddChild(_globalRoot);
		}
	}

	private void FlushPendingMounts()
	{
		for (int i = _pendingMounts.Count - 1; i >= 0; i--)
		{
			PendingMount item = _pendingMounts[i];
			if (!GodotObject.IsInstanceValid(item.Node))
			{
				_pendingMounts.RemoveAt(i);
				continue;
			}

			if (!TryResolveArea(item.Area, out Control? host))
			{
				continue;
			}

			if (item.Area == SystemUiArea.SettingTabPanel)
			{
				AttachSettingTab(host, item.Node);
			}
			else
			{
				AttachControl(host, item.Node);
			}
			_pendingMounts.RemoveAt(i);
			GD.Print($"[ReForge.UI] Pending mount for '{item.Node.Name}' attached to '{item.Area}'.");
		}
	}

	private static bool TryResolveArea(SystemUiArea area, out Control host)
	{
		host = null!;
		if (Engine.GetMainLoop() is not SceneTree tree || tree.Root == null)
		{
			return false;
		}

		Control? resolved = area switch
		{
			SystemUiArea.MainMenuButtonPanel => ResolveMainMenuPanel(tree.Root),
			SystemUiArea.SettingTabPanel => ResolveSettingTabPanel(tree.Root),
			_ => null
		};

		if (resolved == null)
		{
			return false;
		}

		host = resolved;

		return host != null && GodotObject.IsInstanceValid(host) && host.IsInsideTree();
	}

	internal static bool TryDetectAreaHostFromNode(Node node, out SystemUiArea area)
	{
		area = default;

		if (node is NSettingsTabManager)
		{
			area = SystemUiArea.SettingTabPanel;
			return true;
		}

		if (node is not Control control)
		{
			return false;
		}

		if (IsMainMenuPanelHostControl(control))
		{
			area = SystemUiArea.MainMenuButtonPanel;
			return true;
		}

		if (control.Name == SettingsTabManagerNodeName)
		{
			area = SystemUiArea.SettingTabPanel;
			return true;
		}

		return false;
	}

	private static Control? ResolveMainMenuPanel(Node root)
	{
		for (int i = 0; i < MainMenuPanelNodeNames.Length; i++)
		{
			Control? exact = FindControlByName(root, MainMenuPanelNodeNames[i]);
			if (exact != null && IsMainMenuPanelHostControl(exact))
			{
				return exact;
			}
		}

		return FindFirstControlByPredicate(root, IsMainMenuPanelHostControl);
	}

	private static bool IsMainMenuPanelHostControl(Control control)
	{
		if (control is not Container)
		{
			return false;
		}

		return IsMainMenuPanelNameCandidate(control.Name);
	}

	private static bool IsMainMenuPanelNameCandidate(StringName nodeName)
	{
		string name = nodeName.ToString();
		if (string.IsNullOrWhiteSpace(name))
		{
			return false;
		}

		if (name.StartsWith(ReForgeNodePrefix, System.StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		for (int i = 0; i < MainMenuPanelNodeNames.Length; i++)
		{
			if (string.Equals(name, MainMenuPanelNodeNames[i], System.StringComparison.Ordinal))
			{
				return true;
			}
		}

		return name.Contains("MainMenu", System.StringComparison.OrdinalIgnoreCase)
			&& (name.Contains("Buttons", System.StringComparison.OrdinalIgnoreCase)
				|| name.Contains("ButtonPanel", System.StringComparison.OrdinalIgnoreCase)
				|| name.Contains("ButtonContainer", System.StringComparison.OrdinalIgnoreCase));
	}

	private static Control? FindFirstControlByPredicate(Node node, System.Func<Control, bool> predicate)
	{
		if (node is Control control && predicate(control))
		{
			return control;
		}

		foreach (Node child in node.GetChildren())
		{
			Control? found = FindFirstControlByPredicate(child, predicate);
			if (found != null)
			{
				return found;
			}
		}

		return null;
	}

	private static Control? FindControlByName(Node node, string nodeName)
	{
		if (node is Control control && node.Name == nodeName)
		{
			return control;
		}

		foreach (Node child in node.GetChildren())
		{
			Control? found = FindControlByName(child, nodeName);
			if (found != null)
			{
				return found;
			}
		}

		return null;
	}

	private static Control? ResolveSettingTabPanel(Node root)
	{
		Control? tabManager = FindControlByName(root, SettingsTabManagerNodeName);
		if (tabManager == null)
		{
			NSettingsTabManager? manager = FindSettingsTabManagerByType(root);
			tabManager = manager;
		}

		if (tabManager == null)
		{
			return null;
		}

		EnsureSettingTabPager(tabManager);
		return tabManager;
	}

	private static NSettingsTabManager? FindSettingsTabManagerByType(Node node)
	{
		if (node is NSettingsTabManager manager)
		{
			return manager;
		}

		foreach (Node child in node.GetChildren())
		{
			NSettingsTabManager? found = FindSettingsTabManagerByType(child);
			if (found != null)
			{
				return found;
			}
		}

		return null;
	}

	private static void EnsureSettingTabPager(Control tabManager)
	{
		DetachLegacySettingTabScrollHost(tabManager);

		if (tabManager.GetParent() is not Control parent)
		{
			return;
		}

		AdjustSettingTabManagerForPager(tabManager);

		Control prevButton = EnsureSettingTabPagerButton(parent, SettingsTabPrevButtonName, isLeft: true);
		Control nextButton = EnsureSettingTabPagerButton(parent, SettingsTabNextButtonName, isLeft: false);
		LayoutSettingTabPagerButtons(tabManager, prevButton, nextButton);

		BindSettingTabPagerButtonPressed(prevButton, () => ChangeSettingTabPage(tabManager, -1));
		BindSettingTabPagerButtonPressed(nextButton, () => ChangeSettingTabPage(tabManager, +1));

		RefreshSettingTabPagination(tabManager);
	}

	private static void DetachLegacySettingTabScrollHost(Control tabManager)
	{
		if (tabManager.GetParent() is not ScrollContainer scrollHost || scrollHost.Name != SettingsTabScrollHostName)
		{
			return;
		}

		if (scrollHost.GetParent() is not Control parent)
		{
			return;
		}

		int oldIndex = scrollHost.GetIndex();
		tabManager.Reparent(parent, keepGlobalTransform: true);
		parent.MoveChild(tabManager, oldIndex);

		Control? fadeLayer = parent.GetNodeOrNull<Control>(SettingsTabFadeLayerName);
		fadeLayer?.QueueFree();
		scrollHost.QueueFree();
	}

	private static void AdjustSettingTabManagerForPager(Control tabManager)
	{
		ulong managerId = tabManager.GetInstanceId();
		if (!_settingTabOriginalHorizontalOffsetsByManager.ContainsKey(managerId))
		{
			_settingTabOriginalHorizontalOffsetsByManager[managerId] = new Vector2(tabManager.OffsetLeft, tabManager.OffsetRight);
		}

		Vector2 originalOffsets = _settingTabOriginalHorizontalOffsetsByManager[managerId];
		tabManager.OffsetLeft = originalOffsets.X + SettingsTabPagerReservedWidth;
		tabManager.OffsetRight = originalOffsets.Y - SettingsTabPagerReservedWidth;
	}

	private static Control EnsureSettingTabPagerButton(Control parent, string name, bool isLeft)
	{
		Control button = parent.GetNodeOrNull<Control>(name) ?? CreateSettingTabPagerButton(isLeft);
		button.Name = name;
		button.FocusMode = Control.FocusModeEnum.All;
		button.MouseFilter = Control.MouseFilterEnum.Stop;
		button.ZIndex = 310;

		if (button is Button fallback)
		{
			fallback.Text = isLeft ? "<" : ">";
		}

		TrySetArrowDirection(button, isLeft);

		if (button.GetParent() == null)
		{
			parent.AddChild(button);
		}

		return button;
	}

	private static Control CreateSettingTabPagerButton(bool isLeft)
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

		return new Button
		{
			Text = isLeft ? "<" : ">"
		};
	}

	private static void TrySetArrowDirection(Control button, bool isLeft)
	{
		PropertyInfo? prop = button.GetType().GetProperty("IsLeft", BindingFlags.Instance | BindingFlags.Public);
		if (prop?.CanWrite == true && prop.PropertyType == typeof(bool))
		{
			if (button.IsNodeReady())
			{
				prop.SetValue(button, isLeft);
				return;
			}

			if (!button.HasMeta(MetaSettingTabPagerArrowReadyHookBound))
			{
				button.SetMeta(MetaSettingTabPagerArrowReadyHookBound, true);
				button.Connect(Node.SignalName.Ready, Callable.From(() =>
				{
					if (!GodotObject.IsInstanceValid(button))
					{
						return;
					}

					PropertyInfo? lateProp = button.GetType().GetProperty("IsLeft", BindingFlags.Instance | BindingFlags.Public);
					if (lateProp?.CanWrite == true && lateProp.PropertyType == typeof(bool))
					{
						lateProp.SetValue(button, isLeft);
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

	private static void BindSettingTabPagerButtonPressed(Control button, Action handler)
	{
		if (button.HasMeta(MetaSettingTabPagerButtonBound))
		{
			return;
		}

		button.SetMeta(MetaSettingTabPagerButtonBound, true);

		if (button is Button fallback)
		{
			fallback.Pressed += handler;
			return;
		}

		if (button.HasSignal(NClickableControl.SignalName.Released))
		{
			button.Connect(NClickableControl.SignalName.Released, Callable.From<GodotObject>(_ => handler()));
		}
	}

	private static void LayoutSettingTabPagerButtons(Control tabManager, Control prevButton, Control nextButton)
	{
		ulong managerId = tabManager.GetInstanceId();
		if (!_settingTabOriginalHorizontalOffsetsByManager.TryGetValue(managerId, out Vector2 originalOffsets))
		{
			originalOffsets = new Vector2(tabManager.OffsetLeft - SettingsTabPagerReservedWidth, tabManager.OffsetRight + SettingsTabPagerReservedWidth);
		}

		ApplyPagerButtonVerticalLayout(tabManager, prevButton);
		prevButton.AnchorLeft = tabManager.AnchorLeft;
		prevButton.AnchorRight = tabManager.AnchorLeft;
		prevButton.OffsetLeft = originalOffsets.X;
		prevButton.OffsetRight = originalOffsets.X + SettingsTabPagerButtonWidth;

		ApplyPagerButtonVerticalLayout(tabManager, nextButton);
		nextButton.AnchorLeft = tabManager.AnchorRight;
		nextButton.AnchorRight = tabManager.AnchorRight;
		nextButton.OffsetLeft = originalOffsets.Y - SettingsTabPagerButtonWidth;
		nextButton.OffsetRight = originalOffsets.Y;
	}

	private static void ApplyPagerButtonVerticalLayout(Control tabManager, Control button)
	{
		button.AnchorTop = tabManager.AnchorTop;
		button.AnchorBottom = tabManager.AnchorBottom;
		button.OffsetTop = tabManager.OffsetTop;
		button.OffsetBottom = tabManager.OffsetBottom;
	}

	private static void ChangeSettingTabPage(Control tabManager, int delta)
	{
		if (!GodotObject.IsInstanceValid(tabManager))
		{
			return;
		}

		ulong managerId = tabManager.GetInstanceId();
		int currentPage = _settingTabCurrentPageByManager.TryGetValue(managerId, out int existingPage) ? existingPage : 0;
		_settingTabCurrentPageByManager[managerId] = currentPage + delta;
		RefreshSettingTabPagination(tabManager);
	}

	private static void RefreshSettingTabPagination(Control tabManager)
	{
		List<NSettingsTab> tabs = CollectDirectSettingTabs(tabManager);
		BindSettingTabPagerSignals(tabManager, tabs);

		int totalPages = Mathf.Max(1, Mathf.CeilToInt(tabs.Count / (float)SettingsTabPageSize));
		ulong managerId = tabManager.GetInstanceId();
		int currentPage = _settingTabCurrentPageByManager.TryGetValue(managerId, out int existingPage) ? existingPage : 0;
		currentPage = Mathf.Clamp(currentPage, 0, totalPages - 1);
		_settingTabCurrentPageByManager[managerId] = currentPage;

		int pageStart = currentPage * SettingsTabPageSize;
		int pageEndExclusive = pageStart + SettingsTabPageSize;
		for (int i = 0; i < tabs.Count; i++)
		{
			tabs[i].Visible = i >= pageStart && i < pageEndExclusive;
		}

		if (tabManager.GetParent() is not Control parent)
		{
			return;
		}

		Control? prevButton = parent.GetNodeOrNull<Control>(SettingsTabPrevButtonName);
		if (prevButton != null)
		{
			SetPagerButtonDisabled(prevButton, currentPage <= 0);
		}

		Control? nextButton = parent.GetNodeOrNull<Control>(SettingsTabNextButtonName);
		if (nextButton != null)
		{
			SetPagerButtonDisabled(nextButton, currentPage >= totalPages - 1);
		}
	}

	private static void SetPagerButtonDisabled(Control button, bool disabled)
	{
		if (button is NClickableControl clickable)
		{
			if (disabled)
			{
				clickable.Disable();
			}
			else
			{
				clickable.Enable();
			}
			return;
		}

		if (button is BaseButton baseButton)
		{
			baseButton.Disabled = disabled;
		}
	}

	private static List<NSettingsTab> CollectDirectSettingTabs(Control tabManager)
	{
		List<NSettingsTab> tabs = new();
		foreach (Node child in tabManager.GetChildren())
		{
			if (child is NSettingsTab tab)
			{
				tabs.Add(tab);
			}
		}

		return tabs;
	}

	private static void BindSettingTabPagerSignals(Control tabManager, List<NSettingsTab> tabs)
	{
		for (int i = 0; i < tabs.Count; i++)
		{
			NSettingsTab tab = tabs[i];
			if (tab.HasMeta(MetaSettingTabPagerTabBound))
			{
				continue;
			}

			tab.SetMeta(MetaSettingTabPagerTabBound, true);
			tab.Connect(NClickableControl.SignalName.Released, Callable.From<GodotObject>(_ =>
			{
				if (!GodotObject.IsInstanceValid(tabManager) || !GodotObject.IsInstanceValid(tab))
				{
					return;
				}

				List<NSettingsTab> latestTabs = CollectDirectSettingTabs(tabManager);
				int clickedTabIndex = latestTabs.IndexOf(tab);
				if (clickedTabIndex < 0)
				{
					return;
				}

				ulong managerId = tabManager.GetInstanceId();
				_settingTabCurrentPageByManager[managerId] = clickedTabIndex / SettingsTabPageSize;
				RefreshSettingTabPagination(tabManager);
			}));
		}
	}

	private static void AttachSettingTab(Control tabManager, Control child)
	{
		if (child.GetParent() == tabManager)
		{
			EnsureSettingTabPager(tabManager);
			return;
		}

		if (child.GetParent() == null)
		{
			tabManager.AddChild(child);
		}
		else
		{
			child.Reparent(tabManager, keepGlobalTransform: true);
		}

		int rightTriggerIndex = FindDirectChildIndexByName(tabManager, "RightTriggerIcon");
		if (rightTriggerIndex >= 0)
		{
			tabManager.MoveChild(child, rightTriggerIndex);
		}

		EnsureSettingTabPager(tabManager);
	}

	private static int FindDirectChildIndexByName(Node parent, string childName)
	{
		int index = 0;
		foreach (Node child in parent.GetChildren())
		{
			if (child.Name == childName)
			{
				return index;
			}
			index++;
		}

		return -1;
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

	private static void AttachControl(Control parent, Control child)
	{
		if (child.GetParent() == parent)
		{
			return;
		}

		if (child.GetParent() == null)
		{
			parent.AddChild(child);
			return;
		}

		child.Reparent(parent, keepGlobalTransform: true);
	}
}
