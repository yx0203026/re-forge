#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using ReForgeFramework.Api.Events;
using ReForgeFramework.Settings.Controls;

namespace ReForgeFramework.EventWheel.UI;

/// <summary>
/// 事件选项分页运行时：仅通过可见性切页，避免改变原始选项索引语义。
/// </summary>
internal static class EventOptionPagerRuntime
{
	private const string PagerHostName = "ReForge_EventOptionPagerHost";
	private const string PagerPrevButtonName = "ReForge_EventOptionPagerPrevButton";
	private const string PagerNextButtonName = "ReForge_EventOptionPagerNextButton";
	private const string PagerTextName = "ReForge_EventOptionPagerText";
	private const string PagerLeftSpacerName = "ReForge_EventOptionPagerLeftSpacer";
	private const string PagerRightSpacerName = "ReForge_EventOptionPagerRightSpacer";
	private const string PagerInlineGapName = "ReForge_EventOptionPagerInlineGap";
	private const string PagerArrowScenePath = "res://scenes/screens/run_history_screen/run_history_arrow.tscn";
	private const string SourceModId = "reforge.eventwheel.ui";
	private const string ProjectSettingNormalPageSize = "reforge/eventwheel/ui/options_per_page";
	private const string ProjectSettingAncientPageSize = "reforge/eventwheel/ui/ancient_options_per_page";
	private const string EnvNormalPageSize = "REFORGE_EVENTWHEEL_OPTIONS_PER_PAGE";
	private const string EnvAncientPageSize = "REFORGE_EVENTWHEEL_ANCIENT_OPTIONS_PER_PAGE";
	private const string MetaLayoutBoundCleanupKey = "__reforge_eventwheel_layout_cleanup_bound";
	private const string MetaPagerButtonBoundKey = "__reforge_eventwheel_pager_button_bound";
	private const string MetaPagerButtonGuiInputBoundKey = "__reforge_eventwheel_pager_button_gui_input_bound";
	private const string MetaPagerButtonLayoutIdKey = "__reforge_eventwheel_pager_button_layout_id";
	private const string MetaPagerButtonCenterPivotBoundKey = "__reforge_eventwheel_pager_button_center_pivot_bound";
	private const string MetaPagerHostLayoutIdKey = "__reforge_eventwheel_pager_host_layout_id";
	private const string MetaPagerArrowReadyHookBoundKey = "__reforge_eventwheel_pager_arrow_ready_hook_bound";
	private const string MetaPagerEnsureRetryCountKey = "__reforge_eventwheel_pager_ensure_retry_count";
	private const string MetaPagerEnsureRetryPendingKey = "__reforge_eventwheel_pager_ensure_retry_pending";
	private const int DefaultOptionsPerPage = 4;
	private const int MinOptionsPerPage = 1;
	private const int MaxOptionsPerPage = 16;
	private const int MaxEnsureRetryCount = 8;
	private const float AncientPagerTopMargin = 10f;
	private const float NormalPagerTopMargin = 6f;
	private const float AncientPagerHeight = 48f;
	private const float NormalPagerHeight = 42f;
	private const float AncientPagerInlineGap = 12f;
	private const float NormalPagerInlineGap = 10f;
	private const float PagerButtonMinWidth = 64f;
	private const float PagerButtonHorizontalInset = 18f;
	private const float PagerButtonHeight = 44f;

	private static readonly object SyncRoot = new();
	private static readonly FieldInfo? EventField = typeof(NEventLayout).GetField("_event", BindingFlags.Instance | BindingFlags.NonPublic);
	private static readonly FieldInfo? OptionsContainerField = typeof(NEventLayout).GetField("_optionsContainer", BindingFlags.Instance | BindingFlags.NonPublic);
	private static readonly Dictionary<ulong, int> CurrentPageByLayout = new();
	private static readonly Dictionary<ulong, ulong> LastPagerInvokeMsByButton = new();
	private static readonly Dictionary<ulong, string> LastLayoutSnapshotByLayout = new();

	private static int _configuredNormalOptionsPerPage = DefaultOptionsPerPage;
	private static int _configuredAncientOptionsPerPage = DefaultOptionsPerPage;

	internal static void Configure(int? normalOptionsPerPage, int? ancientOptionsPerPage)
	{
		lock (SyncRoot)
		{
			if (normalOptionsPerPage.HasValue)
			{
				_configuredNormalOptionsPerPage = NormalizePageSize(normalOptionsPerPage.Value);
			}

			if (ancientOptionsPerPage.HasValue)
			{
				_configuredAncientOptionsPerPage = NormalizePageSize(ancientOptionsPerPage.Value);
			}
		}
	}

	internal static void EnsureForLayout(NEventLayout layout, string patchId, bool focusVisibleOption = false)
	{
		if (!GodotObject.IsInstanceValid(layout))
		{
			return;
		}

		BindLayoutCleanup(layout);

		if (!TryGetOptionsContainer(layout, out VBoxContainer? optionsContainer) || optionsContainer == null)
		{
			EmitLayoutDiagnostic(
				severity: EventWheelSeverity.Warning,
				layout: layout,
				eventModel: null,
				patchId: patchId,
				message: "Event option pager skipped because options container is unavailable.",
				context: null);
			ScheduleEnsureRetry(layout, patchId, "container_unavailable");
			return;
		}

		bool isAncient = IsAncientLayout(layout, out EventModel? eventModel);
		List<NEventOptionButton> optionButtons = CollectOptionButtons(layout, optionsContainer);
		if (optionButtons.Count == 0)
		{
			CurrentPageByLayout.Remove(layout.GetInstanceId());
			LastLayoutSnapshotByLayout.Remove(layout.GetInstanceId());
			TryRemovePagerHost(layout, optionsContainer);
			ScheduleEnsureRetry(layout, patchId, "option_buttons_empty");
			return;
		}

		layout.SetMeta(MetaPagerEnsureRetryCountKey, 0);
		layout.SetMeta(MetaPagerEnsureRetryPendingKey, false);

		int pageSize = ResolvePageSize(isAncient);
		int totalPages = Mathf.Max(1, Mathf.CeilToInt(optionButtons.Count / (float)pageSize));
		ulong layoutId = layout.GetInstanceId();
		int currentPage = CurrentPageByLayout.TryGetValue(layoutId, out int storedPage) ? storedPage : 0;
		currentPage = Mathf.Clamp(currentPage, 0, totalPages - 1);
		CurrentPageByLayout[layoutId] = currentPage;

		ApplyCurrentPage(optionButtons, currentPage, pageSize);

		if (totalPages <= 1)
		{
			TryRemovePagerHost(layout, optionsContainer);
			if (focusVisibleOption)
			{
				TryFocusFirstVisibleOption(optionButtons);
			}
			return;
		}

		HBoxContainer pagerHost = EnsurePagerHost(optionsContainer, layoutId, isAncient);
		if (pagerHost.GetParent() == null)
		{
			ScheduleEnsureRetry(layout, patchId, "pager_host_attach_failed");
			return;
		}

		Control prevButton = EnsurePagerButton(pagerHost, PagerPrevButtonName, isLeft: true, layoutId);
		Control nextButton = EnsurePagerButton(pagerHost, PagerNextButtonName, isLeft: false, layoutId);
		Label pageText = EnsurePagerText(pagerHost);
		Control leftSpacer = EnsurePagerSpacer(pagerHost, PagerLeftSpacerName);
		Control rightSpacer = EnsurePagerSpacer(pagerHost, PagerRightSpacerName);
		LayoutPagerChildren(pagerHost, prevButton, leftSpacer, pageText, rightSpacer, nextButton);

		BindPagerButtonPressed(prevButton, () => ChangePage(layout, -1));
		BindPagerButtonPressed(nextButton, () => ChangePage(layout, +1));

		SetButtonDisabled(prevButton, currentPage <= 0);
		SetButtonDisabled(nextButton, currentPage >= totalPages - 1);
		pageText.Text = $"{currentPage + 1}/{totalPages}";
		pagerHost.Visible = true;

		if (focusVisibleOption)
		{
			TryFocusFirstVisibleOption(optionButtons);
		}

		TryEmitLayoutSnapshot(
			layout: layout,
			eventModel: eventModel,
			patchId: patchId,
			optionCount: optionButtons.Count,
			pageSize: pageSize,
			totalPages: totalPages,
			currentPage: currentPage,
			isAncient: isAncient);
	}

	private static int ResolvePageSize(bool isAncient)
	{
		int configured = isAncient ? _configuredAncientOptionsPerPage : _configuredNormalOptionsPerPage;
		int resolved = configured;

		if (TryReadProjectSettingPageSize(isAncient, out int fromProjectSetting))
		{
			resolved = fromProjectSetting;
		}

		if (TryReadEnvironmentPageSize(isAncient, out int fromEnvironment))
		{
			resolved = fromEnvironment;
		}

		return NormalizePageSize(resolved);
	}

	private static bool TryReadProjectSettingPageSize(bool isAncient, out int pageSize)
	{
		string key = isAncient ? ProjectSettingAncientPageSize : ProjectSettingNormalPageSize;
		if (TryReadProjectSettingInt(key, out pageSize))
		{
			pageSize = NormalizePageSize(pageSize);
			return true;
		}

		if (isAncient && TryReadProjectSettingInt(ProjectSettingNormalPageSize, out pageSize))
		{
			pageSize = NormalizePageSize(pageSize);
			return true;
		}

		pageSize = 0;
		return false;
	}

	private static bool TryReadProjectSettingInt(string key, out int value)
	{
		value = 0;
		try
		{
			if (!ProjectSettings.HasSetting(key))
			{
				return false;
			}

			object raw = ProjectSettings.GetSetting(key);
			return TryParseInt(raw, out value);
		}
		catch
		{
			return false;
		}
	}

	private static bool TryReadEnvironmentPageSize(bool isAncient, out int pageSize)
	{
		string envKey = isAncient ? EnvAncientPageSize : EnvNormalPageSize;
		string? envValue = System.Environment.GetEnvironmentVariable(envKey);
		if (!string.IsNullOrWhiteSpace(envValue) && int.TryParse(envValue, out int parsed))
		{
			pageSize = NormalizePageSize(parsed);
			return true;
		}

		if (isAncient)
		{
			envValue = System.Environment.GetEnvironmentVariable(EnvNormalPageSize);
			if (!string.IsNullOrWhiteSpace(envValue) && int.TryParse(envValue, out parsed))
			{
				pageSize = NormalizePageSize(parsed);
				return true;
			}
		}

		pageSize = 0;
		return false;
	}

	private static int NormalizePageSize(int value)
	{
		return Mathf.Clamp(value, MinOptionsPerPage, MaxOptionsPerPage);
	}

	private static bool TryParseInt(object? raw, out int value)
	{
		switch (raw)
		{
			case null:
				value = 0;
				return false;
			case int i:
				value = i;
				return true;
			case long l when l >= int.MinValue && l <= int.MaxValue:
				value = (int)l;
				return true;
			case float f:
				value = (int)f;
				return true;
			case double d:
				value = (int)d;
				return true;
			case string s when int.TryParse(s, out int parsed):
				value = parsed;
				return true;
			default:
				value = 0;
				return false;
		}
	}

	private static void BindLayoutCleanup(NEventLayout layout)
	{
		if (layout.HasMeta(MetaLayoutBoundCleanupKey))
		{
			return;
		}

		layout.SetMeta(MetaLayoutBoundCleanupKey, true);
		ulong layoutId = layout.GetInstanceId();
		layout.TreeExiting += () =>
		{
			CurrentPageByLayout.Remove(layoutId);
			LastLayoutSnapshotByLayout.Remove(layoutId);
		};
	}

	private static HBoxContainer EnsurePagerHost(VBoxContainer optionsContainer, ulong layoutId, bool isAncient)
	{
		Control? parent = optionsContainer.GetParent() as Control;
		bool useInlineHost = isAncient || parent == null;
		Node targetHostParent = useInlineHost ? optionsContainer : parent!;

		// 清理非目标父节点上的旧分页条，避免重复挂载后显示异常。
		if (useInlineHost && parent != null)
		{
			HBoxContainer? floatingHost = parent.GetNodeOrNull<HBoxContainer>(PagerHostName);
			if (floatingHost != null)
			{
				floatingHost.QueueFree();
			}
		}
		else
		{
			HBoxContainer? inlineHost = optionsContainer.GetNodeOrNull<HBoxContainer>(PagerHostName);
			if (inlineHost != null)
			{
				inlineHost.QueueFree();
			}
		}

		HBoxContainer? existing = targetHostParent.GetNodeOrNull<HBoxContainer>(PagerHostName);
		if (existing != null && existing.HasMeta(MetaPagerHostLayoutIdKey))
		{
			string boundLayoutId = existing.GetMeta(MetaPagerHostLayoutIdKey).ToString();
			if (!string.Equals(boundLayoutId, layoutId.ToString(), StringComparison.Ordinal))
			{
				existing.QueueFree();
				existing = null;
			}
		}

		HBoxContainer host = existing ?? new HBoxContainer
		{
			Name = PagerHostName,
			Alignment = BoxContainer.AlignmentMode.Begin,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
			MouseFilter = Control.MouseFilterEnum.Stop,
			ThemeTypeVariation = "CardButton"
		};

		host.SetMeta(MetaPagerHostLayoutIdKey, layoutId.ToString());
		host.Visible = true;
		host.TopLevel = false;

		if (host.GetParent() == null)
		{
			if (!ReForge.LifecycleSafety.TryAddChild(targetHostParent, host, out _))
			{
				return host;
			}
		}

		if (useInlineHost)
		{
			PositionPagerHostInline(optionsContainer, host, isAncient);
		}
		else
		{
			PositionPagerHostFloating(parent!, optionsContainer, host, isAncient);
		}

		return host;
	}

	private static void PositionPagerHostInline(VBoxContainer optionsContainer, HBoxContainer pagerHost, bool isAncient)
	{
		float height = isAncient ? AncientPagerHeight : NormalPagerHeight;
		pagerHost.AnchorLeft = 0f;
		pagerHost.AnchorRight = 0f;
		pagerHost.AnchorTop = 0f;
		pagerHost.AnchorBottom = 0f;
		pagerHost.OffsetLeft = 0f;
		pagerHost.OffsetRight = 0f;
		pagerHost.OffsetTop = 0f;
		pagerHost.OffsetBottom = 0f;
		pagerHost.CustomMinimumSize = new Vector2(0f, height);

		// 内联模式放在选项列表顶部，保证 Ancient 布局可见。
		optionsContainer.MoveChild(pagerHost, 0);
		EnsureInlineGapSpacer(optionsContainer, isAncient);
	}

	private static void EnsureInlineGapSpacer(VBoxContainer optionsContainer, bool isAncient)
	{
		Control? existing = optionsContainer.GetNodeOrNull<Control>(PagerInlineGapName);
		Control gap = existing ?? new Control
		{
			Name = PagerInlineGapName,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			FocusMode = Control.FocusModeEnum.None,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsVertical = Control.SizeFlags.ShrinkBegin
		};

		gap.CustomMinimumSize = new Vector2(0f, isAncient ? AncientPagerInlineGap : NormalPagerInlineGap);
		if (gap.GetParent() == null)
		{
			if (!ReForge.LifecycleSafety.TryAddChild(optionsContainer, gap, out _))
			{
				return;
			}
		}

		int desiredIndex = Mathf.Min(optionsContainer.GetChildCount() - 1, 1);
		optionsContainer.MoveChild(gap, desiredIndex);
	}

	private static void PositionPagerHostFloating(Control parent, VBoxContainer optionsContainer, HBoxContainer pagerHost, bool isAncient)
	{
		int desiredIndex = Mathf.Min(parent.GetChildCount() - 1, optionsContainer.GetIndex() + 1);
		parent.MoveChild(pagerHost, desiredIndex);

		float topMargin = isAncient ? AncientPagerTopMargin : NormalPagerTopMargin;
		float height = isAncient ? AncientPagerHeight : NormalPagerHeight;

		pagerHost.AnchorLeft = optionsContainer.AnchorLeft;
		pagerHost.AnchorRight = optionsContainer.AnchorRight;
		pagerHost.AnchorTop = optionsContainer.AnchorTop;
		pagerHost.AnchorBottom = optionsContainer.AnchorTop;
		pagerHost.OffsetLeft = optionsContainer.OffsetLeft;
		pagerHost.OffsetRight = optionsContainer.OffsetRight;
		pagerHost.OffsetTop = optionsContainer.OffsetBottom + topMargin;
		pagerHost.OffsetBottom = pagerHost.OffsetTop + height;
	}

	private static Control EnsurePagerButton(HBoxContainer host, string name, bool isLeft, ulong layoutId)
	{
		Control? existing = host.GetNodeOrNull<Control>(name);
		if (existing != null && existing.HasMeta(MetaPagerButtonLayoutIdKey))
		{
			string boundLayoutId = existing.GetMeta(MetaPagerButtonLayoutIdKey).ToString();
			if (!string.Equals(boundLayoutId, layoutId.ToString(), StringComparison.Ordinal))
			{
				existing.QueueFree();
				existing = null;
			}
		}

		Control button = existing ?? CreatePagerButton(isLeft);
		button.Name = name;
		button.FocusMode = Control.FocusModeEnum.All;
		button.MouseFilter = Control.MouseFilterEnum.Stop;
		button.CustomMinimumSize = new Vector2(PagerButtonMinWidth + PagerButtonHorizontalInset, PagerButtonHeight);
		button.ZIndex = 0;
		button.SetMeta(MetaPagerButtonLayoutIdKey, layoutId.ToString());
		EnsureCenterPivotScaling(button);

		if (button is Godot.Button fallbackButton)
		{
			fallbackButton.Text = isLeft ? "<" : ">";
			UiButtonStyleTemplates.Apply(fallbackButton, UiButtonStylePreset.OfficialSettingsArrow);
		}

		if (button.GetParent() == null)
		{
			if (!ReForge.LifecycleSafety.TryAddChild(host, button, out _))
			{
				return button;
			}
		}

		TrySetArrowDirection(button, isLeft);

		if (!button.HasMeta("__reforge_eventwheel_pager_cleanup_bound"))
		{
			button.SetMeta("__reforge_eventwheel_pager_cleanup_bound", true);
			button.TreeExiting += () => LastPagerInvokeMsByButton.Remove(button.GetInstanceId());
		}

		return button;
	}

	private static void EnsureCenterPivotScaling(Control button)
	{
		ApplyCenterPivotRecursive(button);

		if (button.HasMeta(MetaPagerButtonCenterPivotBoundKey))
		{
			return;
		}

		button.SetMeta(MetaPagerButtonCenterPivotBoundKey, true);
		button.Resized += () =>
		{
			if (!GodotObject.IsInstanceValid(button))
			{
				return;
			}

			ApplyCenterPivotRecursive(button);
		};

		button.ChildEnteredTree += _ =>
		{
			if (!GodotObject.IsInstanceValid(button))
			{
				return;
			}

			ApplyCenterPivotRecursive(button);
		};
	}

	private static void ApplyCenterPivotRecursive(Control root)
	{
		root.PivotOffset = new Vector2(root.Size.X * 0.5f, root.Size.Y * 0.5f);

		foreach (Node child in root.GetChildren())
		{
			if (child is Control controlChild)
			{
				ApplyCenterPivotRecursive(controlChild);
			}
		}
	}

	private static Control CreatePagerButton(bool isLeft)
	{
		if (ResourceLoader.Exists(PagerArrowScenePath))
		{
			PackedScene? scene = ResourceLoader.Load<PackedScene>(PagerArrowScenePath);
			if (scene?.Instantiate() is Control officialArrow)
			{
				officialArrow.MouseFilter = Control.MouseFilterEnum.Stop;
				TrySetArrowDirection(officialArrow, isLeft);
				return officialArrow;
			}
		}

		return new Godot.Button
		{
			Text = isLeft ? "<" : ">",
			Flat = true,
			MouseFilter = Control.MouseFilterEnum.Stop,
			FocusMode = Control.FocusModeEnum.All
		};
	}

	private static Label EnsurePagerText(HBoxContainer host)
	{
		Label? existing = host.GetNodeOrNull<Label>(PagerTextName);
		if (existing != null)
		{
			return existing;
		}

		Label text = new()
		{
			Name = PagerTextName,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
			CustomMinimumSize = new Vector2(80f, 0f),
			Text = "1/1"
		};
		ReForge.LifecycleSafety.TryAddChild(host, text, out _);
		return text;
	}

	private static Control EnsurePagerSpacer(HBoxContainer host, string name)
	{
		Control? existing = host.GetNodeOrNull<Control>(name);
		if (existing != null)
		{
			return existing;
		}

		Control spacer = new()
		{
			Name = name,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			FocusMode = Control.FocusModeEnum.None,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsVertical = Control.SizeFlags.Fill,
			CustomMinimumSize = Vector2.Zero
		};

		if (!ReForge.LifecycleSafety.TryAddChild(host, spacer, out _))
		{
			return spacer;
		}
		return spacer;
	}

	private static void LayoutPagerChildren(
		HBoxContainer host,
		Control prevButton,
		Control leftSpacer,
		Label pageText,
		Control rightSpacer,
		Control nextButton)
	{
		// 使用两侧弹性占位，让翻页按钮贴边，页码保持中间。
		prevButton.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
		prevButton.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
		nextButton.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;
		nextButton.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;

		leftSpacer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		leftSpacer.SizeFlagsVertical = Control.SizeFlags.Fill;
		rightSpacer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		rightSpacer.SizeFlagsVertical = Control.SizeFlags.Fill;
		pageText.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
		pageText.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;

		host.MoveChild(prevButton, 0);
		host.MoveChild(leftSpacer, 1);
		host.MoveChild(pageText, 2);
		host.MoveChild(rightSpacer, 3);
		host.MoveChild(nextButton, 4);
	}

	private static void TryRemovePagerHost(NEventLayout layout, VBoxContainer optionsContainer)
	{
		if (!GodotObject.IsInstanceValid(layout))
		{
			return;
		}

		HBoxContainer? inlineHost = optionsContainer.GetNodeOrNull<HBoxContainer>(PagerHostName);
		if (inlineHost != null)
		{
			inlineHost.QueueFree();
		}

		Control? inlineGap = optionsContainer.GetNodeOrNull<Control>(PagerInlineGapName);
		if (inlineGap != null)
		{
			inlineGap.QueueFree();
		}

		if (optionsContainer.GetParent() is not Control parent)
		{
			return;
		}

		HBoxContainer? pagerHost = parent.GetNodeOrNull<HBoxContainer>(PagerHostName);
		if (pagerHost == null)
		{
			return;
		}

		pagerHost.QueueFree();
	}

	private static List<NEventOptionButton> CollectOptionButtons(NEventLayout layout, VBoxContainer optionsContainer)
	{
		List<NEventOptionButton> result = new();
		HashSet<ulong> dedupe = new();

		foreach (Node child in optionsContainer.GetChildren())
		{
			if (child is not NEventOptionButton optionButton)
			{
				continue;
			}

			ulong id = optionButton.GetInstanceId();
			if (dedupe.Add(id))
			{
				result.Add(optionButton);
			}
		}

		if (result.Count > 0)
		{
			return result;
		}

		PropertyInfo? optionButtonsProperty = layout.GetType().GetProperty(
			"OptionButtons",
			BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		if (optionButtonsProperty == null || optionButtonsProperty.GetGetMethod(nonPublic: true) == null)
		{
			return result;
		}

		object? raw = optionButtonsProperty.GetValue(layout);
		if (raw is not IEnumerable enumerable)
		{
			return result;
		}

		foreach (object? item in enumerable)
		{
			if (item is not NEventOptionButton optionButton)
			{
				continue;
			}

			ulong id = optionButton.GetInstanceId();
			if (dedupe.Add(id))
			{
				result.Add(optionButton);
			}
		}

		return result;
	}

	private static void ApplyCurrentPage(List<NEventOptionButton> optionButtons, int page, int pageSize)
	{
		int pageStart = page * pageSize;
		int pageEndExclusive = pageStart + pageSize;

		for (int i = 0; i < optionButtons.Count; i++)
		{
			NEventOptionButton button = optionButtons[i];
			if (!GodotObject.IsInstanceValid(button))
			{
				continue;
			}

			button.Visible = i >= pageStart && i < pageEndExclusive;
		}
	}

	private static void ChangePage(NEventLayout layout, int delta)
	{
		if (!GodotObject.IsInstanceValid(layout))
		{
			return;
		}

		if (!TryGetOptionsContainer(layout, out VBoxContainer? optionsContainer) || optionsContainer == null)
		{
			return;
		}

		List<NEventOptionButton> optionButtons = CollectOptionButtons(layout, optionsContainer);
		if (optionButtons.Count == 0)
		{
			return;
		}

		bool isAncient = IsAncientLayout(layout, out _);
		int pageSize = ResolvePageSize(isAncient);
		if (optionButtons.Count <= pageSize)
		{
			return;
		}

		ulong layoutId = layout.GetInstanceId();
		int totalPages = Mathf.Max(1, Mathf.CeilToInt(optionButtons.Count / (float)pageSize));
		int currentPage = CurrentPageByLayout.TryGetValue(layoutId, out int storedPage) ? storedPage : 0;
		int nextPage = Mathf.Clamp(currentPage + delta, 0, totalPages - 1);
		if (nextPage == currentPage)
		{
			return;
		}

		CurrentPageByLayout[layoutId] = nextPage;
		EnsureForLayout(layout, patchId: "eventwheel.ui.page_change", focusVisibleOption: true);
	}

	private static void BindPagerButtonPressed(Control button, Action handler)
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
			BindPagerGuiInputFallback(clickSource, safeHandler);
			return;
		}

		bool signalBound = TryConnectReleasedSignal(clickSource, safeHandler);
		if (!signalBound)
		{
			signalBound = TryConnectActionSignal(clickSource, "pressed", safeHandler);
		}

		if (!signalBound)
		{
			GD.Print("[ReForge.EventWheel] Pager signal bind failed, fallback to gui_input.");
		}

		BindPagerGuiInputFallback(clickSource, safeHandler);
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
			if (LastPagerInvokeMsByButton.TryGetValue(buttonId, out ulong lastInvoke)
				&& now - lastInvoke < 120)
			{
				return;
			}

			LastPagerInvokeMsByButton[buttonId] = now;
			handler();
		};
	}

	private static bool TryConnectActionSignal(Control source, string signalName, Action handler)
	{
		if (!source.HasSignal(signalName))
		{
			return false;
		}

		Error direct = source.Connect(signalName, Callable.From(handler));
		if (direct == Error.Ok)
		{
			return true;
		}

		Error withSender = source.Connect(signalName, Callable.From<GodotObject>(_ => handler()));
		return withSender == Error.Ok;
	}

	private static bool TryConnectReleasedSignal(Control source, Action handler)
	{
		return TryConnectActionSignal(source, "released", handler);
	}

	private static void BindPagerGuiInputFallback(Control source, Action handler)
	{
		if (source.HasMeta(MetaPagerButtonGuiInputBoundKey))
		{
			return;
		}

		Error connectResult = source.Connect(Control.SignalName.GuiInput, Callable.From<InputEvent>(inputEvent =>
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
			source.SetMeta(MetaPagerButtonGuiInputBoundKey, true);
		}
	}

	private static Control ResolveClickSource(Control root)
	{
		if (root is BaseButton || root.HasSignal("released") || root.HasSignal("pressed"))
		{
			return root;
		}

		Control? clickableChild = FindFirstClickableDescendant(root);
		return clickableChild ?? root;
	}

	private static Control? FindFirstClickableDescendant(Node node)
	{
		foreach (Node child in node.GetChildren())
		{
			if (child is not Control control)
			{
				continue;
			}

			if (control is BaseButton || control.HasSignal("released") || control.HasSignal("pressed"))
			{
				return control;
			}

			Control? nested = FindFirstClickableDescendant(control);
			if (nested != null)
			{
				return nested;
			}
		}

		return null;
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

					PropertyInfo? lateProperty = button.GetType().GetProperty("IsLeft", BindingFlags.Instance | BindingFlags.Public);
					if (lateProperty?.CanWrite == true && lateProperty.PropertyType == typeof(bool))
					{
						lateProperty.SetValue(button, isLeft);
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
			// 目标控件可能不暴露 Enable/Disable，忽略即可。
		}
	}

	private static void TryFocusFirstVisibleOption(List<NEventOptionButton> optionButtons)
	{
		for (int i = 0; i < optionButtons.Count; i++)
		{
			NEventOptionButton button = optionButtons[i];
			if (!GodotObject.IsInstanceValid(button) || !button.Visible)
			{
				continue;
			}

			if (button.FocusMode == Control.FocusModeEnum.None)
			{
				button.FocusMode = Control.FocusModeEnum.All;
			}

			button.CallDeferred("grab_focus");
			return;
		}
	}

	private static bool IsAncientLayout(NEventLayout layout, out EventModel? eventModel)
	{
		bool hasEvent = TryGetLayoutEvent(layout, out eventModel);
		if (layout is NAncientEventLayout)
		{
			return true;
		}

		if (!hasEvent || eventModel == null)
		{
			return false;
		}

		return eventModel is AncientEventModel;
	}

	private static bool TryGetLayoutEvent(NEventLayout layout, out EventModel? eventModel)
	{
		eventModel = null;
		if (EventField == null)
		{
			return false;
		}

		if (EventField.GetValue(layout) is EventModel model)
		{
			eventModel = model;
			return true;
		}

		return false;
	}

	private static bool TryGetOptionsContainer(NEventLayout layout, out VBoxContainer? optionsContainer)
	{
		optionsContainer = null;
		if (OptionsContainerField != null
			&& OptionsContainerField.GetValue(layout) is VBoxContainer reflectedContainer
			&& GodotObject.IsInstanceValid(reflectedContainer))
		{
			optionsContainer = reflectedContainer;
			return true;
		}

		if (TryFindOptionsContainerFallback(layout, out VBoxContainer? fallbackContainer)
			&& fallbackContainer != null)
		{
			optionsContainer = fallbackContainer;
			return true;
		}

		return false;
	}

	private static bool TryFindOptionsContainerFallback(NEventLayout layout, out VBoxContainer? optionsContainer)
	{
		optionsContainer = null;
		Queue<Node> queue = new();
		queue.Enqueue(layout);

		int bestDescendantMatchCount = 0;
		while (queue.Count > 0)
		{
			Node node = queue.Dequeue();
			foreach (Node child in node.GetChildren())
			{
				queue.Enqueue(child);
			}

			if (node is not VBoxContainer container || !GodotObject.IsInstanceValid(container))
			{
				continue;
			}

			int directMatchCount = CountDirectOptionButtons(container);
			if (directMatchCount > 0)
			{
				optionsContainer = container;
				return true;
			}

			int descendantMatchCount = CountDescendantOptionButtons(container);
			if (descendantMatchCount > bestDescendantMatchCount)
			{
				bestDescendantMatchCount = descendantMatchCount;
				optionsContainer = container;
			}
		}

		return optionsContainer != null && bestDescendantMatchCount > 0;
	}

	private static int CountDirectOptionButtons(VBoxContainer container)
	{
		int count = 0;
		foreach (Node child in container.GetChildren())
		{
			if (child is NEventOptionButton)
			{
				count++;
			}
		}

		return count;
	}

	private static int CountDescendantOptionButtons(Node root)
	{
		int count = 0;
		Queue<Node> queue = new();
		queue.Enqueue(root);

		while (queue.Count > 0)
		{
			Node current = queue.Dequeue();
			foreach (Node child in current.GetChildren())
			{
				if (child is NEventOptionButton)
				{
					count++;
				}

				queue.Enqueue(child);
			}
		}

		return count;
	}

	private static void ScheduleEnsureRetry(NEventLayout layout, string patchId, string reason)
	{
		if (!GodotObject.IsInstanceValid(layout))
		{
			return;
		}

		int retryCount = 0;
		if (layout.HasMeta(MetaPagerEnsureRetryCountKey)
			&& TryParseInt(layout.GetMeta(MetaPagerEnsureRetryCountKey), out int parsedRetryCount))
		{
			retryCount = parsedRetryCount;
		}

		if (retryCount >= MaxEnsureRetryCount)
		{
			EmitLayoutDiagnostic(
				severity: EventWheelSeverity.Warning,
				layout: layout,
				eventModel: null,
				patchId: patchId,
				message: $"Event option pager retry exhausted. reason='{reason}', retryCount={retryCount}.",
				context: null);
			return;
		}

		layout.SetMeta(MetaPagerEnsureRetryCountKey, retryCount + 1);
		if (ReForge.LifecycleSafety.TryScheduleOnNextProcessFrame(
			layout,
			MetaPagerEnsureRetryPendingKey,
			() => EnsureForLayout(layout, $"{patchId}.retry"),
			out string scheduleFailure))
		{
			return;
		}

		EmitLayoutDiagnostic(
			severity: EventWheelSeverity.Warning,
			layout: layout,
			eventModel: null,
			patchId: patchId,
			message: $"Event option pager retry scheduling failed. reason='{scheduleFailure}'.",
			context: null);
	}

	private static void TryEmitLayoutSnapshot(
		NEventLayout layout,
		EventModel? eventModel,
		string patchId,
		int optionCount,
		int pageSize,
		int totalPages,
		int currentPage,
		bool isAncient)
	{
		ulong layoutId = layout.GetInstanceId();
		string snapshot = $"{optionCount}:{pageSize}:{totalPages}:{currentPage}:{isAncient}";
		if (LastLayoutSnapshotByLayout.TryGetValue(layoutId, out string? previous)
			&& StringComparer.Ordinal.Equals(previous, snapshot))
		{
			return;
		}

		LastLayoutSnapshotByLayout[layoutId] = snapshot;

		EmitLayoutDiagnostic(
			severity: EventWheelSeverity.Info,
			layout: layout,
			eventModel: eventModel,
			patchId: patchId,
			message: $"Event option pager applied. optionCount={optionCount}, pageSize={pageSize}, page={currentPage + 1}/{totalPages}.",
			context: new Dictionary<string, string>(StringComparer.Ordinal)
			{
				["optionCount"] = optionCount.ToString(),
				["pageSize"] = pageSize.ToString(),
				["page"] = (currentPage + 1).ToString(),
				["totalPages"] = totalPages.ToString(),
				["isAncient"] = isAncient ? "true" : "false"
			});
	}

	private static void EmitLayoutDiagnostic(
		EventWheelSeverity severity,
		NEventLayout layout,
		EventModel? eventModel,
		string patchId,
		string message,
		IReadOnlyDictionary<string, string>? context)
	{
		Dictionary<string, string> mergedContext = new(StringComparer.Ordinal)
		{
			["patchId"] = patchId,
			["layoutType"] = layout.GetType().FullName ?? layout.GetType().Name
		};

		if (context != null)
		{
			foreach (KeyValuePair<string, string> pair in context)
			{
				mergedContext[pair.Key] = pair.Value;
			}
		}

		string eventId = ResolveEventId(eventModel);
		if (eventId.Length == 0)
		{
			eventId = "unknown.event";
		}

		if (TryGetDiagnostics(out ReForgeFramework.EventWheel.EventWheelDiagnostics? diagnostics) && diagnostics != null)
		{
			diagnostics.Track(
				stage: EventWheelStage.Layout,
				severity: severity,
				eventId: eventId,
				sourceModId: SourceModId,
				message: message,
				exceptionSummary: severity == EventWheelSeverity.Error ? message : null,
				context: mergedContext);
			return;
		}

		if (severity == EventWheelSeverity.Error)
		{
			GD.PrintErr($"[ReForge.EventWheel] {message}");
		}
	}

	private static bool TryGetDiagnostics(out ReForgeFramework.EventWheel.EventWheelDiagnostics? diagnostics)
	{
		diagnostics = null;
		if (!global::ReForge.EventWheel.TryGetRuntime(
			out ReForgeFramework.EventWheel.EventDefinitionRegistry? registry,
			out ReForgeFramework.EventWheel.EventMutationPlanner? planner,
			out ReForgeFramework.EventWheel.EventMutationExecutor? executor,
			out ReForgeFramework.EventWheel.EventWheelDiagnostics? runtimeDiagnostics))
		{
			return false;
		}

		diagnostics = runtimeDiagnostics;
		return diagnostics != null;
	}

	private static string ResolveEventId(EventModel? model)
	{
		if (model == null)
		{
			return string.Empty;
		}

		try
		{
			string entry = model.Id.Entry?.Trim() ?? string.Empty;
			if (entry.Length > 0)
			{
				return entry;
			}
		}
		catch
		{
			// 忽略并回退。
		}

		try
		{
			string fromId = model.Id.ToString()?.Trim() ?? string.Empty;
			if (fromId.Length > 0)
			{
				return fromId;
			}
		}
		catch
		{
			// 忽略并回退。
		}

		return model.GetType().Name?.Trim() ?? string.Empty;
	}
}
