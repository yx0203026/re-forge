#nullable enable

using System.Collections.Generic;
using Godot;
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
	private const string SettingsTabLeftFadeName = "ReForgeSettingTabLeftFade";
	private const string SettingsTabRightFadeName = "ReForgeSettingTabRightFade";
	private const int SettingsTabFadeWidth = 84;
	private static readonly StringName MetaSettingTabWheelBound = "__reforge_setting_tab_wheel_bound";
	private static readonly Color SettingsTabFadeColor = new(0.05f, 0.08f, 0.12f, 0.96f);
	private static Texture2D? _settingsTabLeftFadeTexture;
	private static Texture2D? _settingsTabRightFadeTexture;

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

		_ = EnsureSettingTabScrollHost(tabManager);
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

	private static ScrollContainer EnsureSettingTabScrollHost(Control tabManager)
	{
		if (tabManager.GetParent() is ScrollContainer existingHost && existingHost.Name == SettingsTabScrollHostName)
		{
			ConfigureSettingTabScrollHost(existingHost);
			return existingHost;
		}

		if (tabManager.GetParent() is not Control parent)
		{
			throw new System.InvalidOperationException("SettingsTabManager parent is not Control.");
		}

		ScrollContainer scrollHost = new()
		{
			Name = SettingsTabScrollHostName
		};

		CopyLayout(tabManager, scrollHost);
		ConfigureSettingTabScrollHost(scrollHost);

		int oldIndex = tabManager.GetIndex();
		parent.AddChild(scrollHost);
		parent.MoveChild(scrollHost, oldIndex);
		tabManager.Reparent(scrollHost, keepGlobalTransform: true);
		tabManager.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		return scrollHost;
	}

	private static void ConfigureSettingTabScrollHost(ScrollContainer scrollHost)
	{
		scrollHost.HorizontalScrollMode = ScrollContainer.ScrollMode.ShowNever;
		scrollHost.VerticalScrollMode = ScrollContainer.ScrollMode.ShowNever;
		scrollHost.MouseFilter = Control.MouseFilterEnum.Stop;
		EnsureSettingTabEdgeFade(scrollHost);

		if (scrollHost.HasMeta(MetaSettingTabWheelBound))
		{
			return;
		}

		scrollHost.SetMeta(MetaSettingTabWheelBound, true);
		scrollHost.Connect(Control.SignalName.GuiInput, Callable.From<InputEvent>(inputEvent =>
		{
			if (inputEvent is not InputEventMouseButton mouseButton || !mouseButton.IsPressed())
			{
				return;
			}

			const int step = 96;
			switch (mouseButton.ButtonIndex)
			{
				case MouseButton.WheelUp:
				case MouseButton.WheelLeft:
					scrollHost.ScrollHorizontal = Mathf.Max(0, scrollHost.ScrollHorizontal - step);
					break;
				case MouseButton.WheelDown:
				case MouseButton.WheelRight:
					scrollHost.ScrollHorizontal += step;
					break;
			}
		}));
	}

	private static void EnsureSettingTabEdgeFade(ScrollContainer scrollHost)
	{
		if (scrollHost.GetParent() is not Control parent)
		{
			return;
		}

		Control fadeLayer = parent.GetNodeOrNull<Control>(SettingsTabFadeLayerName) ?? CreateSettingTabFadeLayer();
		CopyLayout(scrollHost, fadeLayer);
		if (fadeLayer.GetParent() == null)
		{
			parent.AddChild(fadeLayer);
		}

		// 让遮罩层稳定覆盖在滚动容器上方。
		parent.MoveChild(fadeLayer, parent.GetChildCount() - 1);

		TextureRect leftFade = fadeLayer.GetNodeOrNull<TextureRect>(SettingsTabLeftFadeName) ?? CreateSettingTabFadeOverlay(leftSide: true);
		if (leftFade.GetParent() == null)
		{
			fadeLayer.AddChild(leftFade);
		}

		TextureRect rightFade = fadeLayer.GetNodeOrNull<TextureRect>(SettingsTabRightFadeName) ?? CreateSettingTabFadeOverlay(leftSide: false);
		if (rightFade.GetParent() == null)
		{
			fadeLayer.AddChild(rightFade);
		}

		fadeLayer.MoveChild(leftFade, fadeLayer.GetChildCount() - 1);
		fadeLayer.MoveChild(rightFade, fadeLayer.GetChildCount() - 1);
	}

	private static Control CreateSettingTabFadeLayer()
	{
		return new Control
		{
			Name = SettingsTabFadeLayerName,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			ZIndex = 300
		};
	}

	private static TextureRect CreateSettingTabFadeOverlay(bool leftSide)
	{
		TextureRect fade = new()
		{
			Name = leftSide ? SettingsTabLeftFadeName : SettingsTabRightFadeName,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			Texture = GetSettingTabFadeTexture(leftSide),
			StretchMode = TextureRect.StretchModeEnum.Scale,
			ZIndex = 200
		};

		fade.AnchorTop = 0;
		fade.AnchorBottom = 1;
		fade.OffsetTop = 0;
		fade.OffsetBottom = 0;

		if (leftSide)
		{
			fade.AnchorLeft = 0;
			fade.AnchorRight = 0;
			fade.OffsetLeft = 0;
			fade.OffsetRight = SettingsTabFadeWidth;
		}
		else
		{
			fade.AnchorLeft = 1;
			fade.AnchorRight = 1;
			fade.OffsetLeft = -SettingsTabFadeWidth;
			fade.OffsetRight = 0;
		}

		return fade;
	}

	private static Texture2D GetSettingTabFadeTexture(bool leftSide)
	{
		if (leftSide)
		{
			_settingsTabLeftFadeTexture ??= BuildFadeTexture(leftToRight: true);
			return _settingsTabLeftFadeTexture;
		}

		_settingsTabRightFadeTexture ??= BuildFadeTexture(leftToRight: false);
		return _settingsTabRightFadeTexture;
	}

	private static Texture2D BuildFadeTexture(bool leftToRight)
	{
		const int width = 128;
		const int height = 4;
		Image image = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);

		for (int x = 0; x < width; x++)
		{
			float t = (float)x / (width - 1);
			float alpha = leftToRight ? 1f - t : t;
			Color color = new(SettingsTabFadeColor.R, SettingsTabFadeColor.G, SettingsTabFadeColor.B, SettingsTabFadeColor.A * alpha);
			for (int y = 0; y < height; y++)
			{
				image.SetPixel(x, y, color);
			}
		}

		return ImageTexture.CreateFromImage(image);
	}

	private static void AttachSettingTab(Control tabManager, Control child)
	{
		if (child.GetParent() == tabManager)
		{
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
