#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.Core.Runs;

namespace ReForgeFramework.Api.Ui;

internal sealed partial class DebuffSelectionOverlayScreen : Control, IOverlayScreen, IScreenContext
{
	private const int EntriesPerPage = 3;
	private const string CommonBannerScenePath = "res://scenes/ui/common_banner.tscn";
	private const string RewardBannerTexturePath = "res://images/ui/reward_screen/reward_banner.png";
	private const string RewardSkipButtonTexturePath = "res://images/ui/reward_screen/reward_skip_button.png";
	private const string BannerFontPath = "res://themes/kreon_bold_glyph_space_three.tres";
	private const string ActionFontPath = "res://themes/kreon_bold_glyph_space_two.tres";
	private const float CardXOffset = 350f;
	private static readonly Vector2 EntryAnimPosOffset = new(0f, 50f);
	private static readonly Color BannerFontColor = new(1f, 0.964706f, 0.886275f, 1f);
	private static readonly Color BannerOutlineColor = new(0.290196f, 0.235294f, 0.164706f, 0.752941f);
	private static readonly Color ActionFontColor = new(0.992157f, 0.956863f, 0.890196f, 1f);
	private static readonly Color ActionOutlineColor = new(0.121569f, 0.25098f, 0.270588f, 1f);
	private static readonly Color SummaryFontColor = new(0.937255f, 0.784314f, 0.317647f, 1f);

	private readonly TaskCompletionSource<IReadOnlyList<DebuffSelectionEntry>> _completionSource = new();
	private readonly List<DebuffSelectionEntry> _entries;
	private readonly LocString _title;
	private readonly int _minSelect;
	private readonly int _maxSelect;
	private readonly bool _cancelable;
	private readonly Dictionary<NGridCardHolder, DebuffSelectionEntry> _entryByHolder = new();
	private readonly List<NGridCardHolder> _entryHolders = new();
	private readonly HashSet<NGridCardHolder> _selectedHolders = new();
	private int _currentPage;
	private PackedScene? _commonBannerScene;
	private Texture2D? _rewardBannerTexture;
	private Texture2D? _rewardSkipButtonTexture;
	private Font? _bannerFont;
	private Font? _actionFont;

	private Control? _entryContainer;
	private readonly Dictionary<NGridCardHolder, NCard> _entryCardByHolder = new();
	private Tween? _entryRowTween;
	private Label? _selectionSummaryLabel;
	private Label? _pageLabel;
	private BaseButton? _prevPageButton;
	private BaseButton? _nextPageButton;
	private BaseButton? _cancelButton;
	private HBoxContainer? _pagerBar;
	private HBoxContainer? _bottomBar;

	public NetScreenType ScreenType => NetScreenType.CardSelection;

	public bool UseSharedBackstop => true;

	public Control DefaultFocusedControl =>
		(Control?)GetVisibleEntryHolders().FirstOrDefault()
		?? (Control?)_nextPageButton
		?? (Control?)_prevPageButton
		?? this;

	private DebuffSelectionOverlayScreen(
		IReadOnlyList<DebuffSelectionEntry> entries,
		LocString title,
		int minSelect,
		int maxSelect,
		bool cancelable)
	{
		_entries = entries.ToList();
		_title = title;
		_minSelect = minSelect;
		_maxSelect = maxSelect;
		_cancelable = cancelable;
		Name = "ReForgeDebuffSelectionOverlay";
		MouseFilter = MouseFilterEnum.Stop;
		AnchorRight = 1f;
		AnchorBottom = 1f;
		_currentPage = 0;
	}

	public static async Task<IReadOnlyList<DebuffSelectionEntry>> ShowAndWait(
		IReadOnlyList<DebuffSelectionEntry> entries,
		LocString title,
		int minSelect,
		int maxSelect,
		bool cancelable)
	{
		if (!ReForge.LifecycleSafety.TryGetOverlayStack(out NOverlayStack? stack, out string reason) || stack == null)
		{
			throw new InvalidOperationException($"Debuff selection overlay runtime is not ready. {reason}");
		}

		DebuffSelectionOverlayScreen screen = new(entries, title, minSelect, maxSelect, cancelable);
		stack.Push(screen);
		return await screen.WaitForSelection();
	}

	public override void _Ready()
	{
		LoadOfficialUiResources();
		BuildVisualTree();
		RefreshSelectionState();
		RefreshPageVisibility();
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event.IsActionPressed("ui_accept"))
		{
			TryCompleteSelectionIfValid();
			AcceptEvent();
			return;
		}

		if (!_cancelable)
		{
			return;
		}

		if (@event.IsActionPressed("ui_cancel"))
		{
			CompleteWithCurrentSelectionOrCancel(forceCancel: true);
			AcceptEvent();
		}
	}

	public override void _ExitTree()
	{
		_entryRowTween?.Kill();
		_entryRowTween = null;

		_entryCardByHolder.Clear();
		_selectedHolders.Clear();

		if (!_completionSource.Task.IsCompleted)
		{
			_completionSource.SetResult(Array.Empty<DebuffSelectionEntry>());
		}

		base._ExitTree();
	}

	public void AfterOverlayOpened()
	{
		Modulate = Colors.White;
		PlayVisibleEntriesAnimateInOfficial();
	}

	public void AfterOverlayClosed()
	{
		this.QueueFreeSafely();
	}

	public void AfterOverlayShown()
	{
		Visible = true;
	}

	public void AfterOverlayHidden()
	{
		Visible = false;
	}

	private async Task<IReadOnlyList<DebuffSelectionEntry>> WaitForSelection()
	{
		IReadOnlyList<DebuffSelectionEntry> selected = await _completionSource.Task;
		if (!ReForge.LifecycleSafety.TryGetOverlayStack(out NOverlayStack? stack, out _ ) || stack == null)
		{
			this.QueueFreeSafely();
			return selected;
		}

		stack.Remove(this);
		return selected;
	}

	private void BuildVisualTree()
	{
		ColorRect dimLayer = new()
		{
			Name = "DimLayer",
			AnchorRight = 1f,
			AnchorBottom = 1f,
			Color = new Color(0f, 0f, 0f, 0.34f),
			MouseFilter = MouseFilterEnum.Stop
		};
		AddChild(dimLayer);

		Control topLayer = new()
		{
			AnchorRight = 1f,
			AnchorBottom = 1f,
			MouseFilter = MouseFilterEnum.Ignore
		};
		AddChild(topLayer);

		Control bannerRoot = CreateBannerRoot(ResolveOverlayTitleText());
		topLayer.AddChild(bannerRoot);

		MarginContainer contentMargin = new()
		{
			AnchorRight = 1f,
			AnchorBottom = 1f,
			MouseFilter = MouseFilterEnum.Ignore
		};
		contentMargin.AddThemeConstantOverride("margin_left", 300);
		contentMargin.AddThemeConstantOverride("margin_right", 300);
		contentMargin.AddThemeConstantOverride("margin_top", 210);
		contentMargin.AddThemeConstantOverride("margin_bottom", 110);
		AddChild(contentMargin);

		VBoxContainer panel = new()
		{
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			SizeFlagsVertical = SizeFlags.ExpandFill,
			MouseFilter = MouseFilterEnum.Stop
		};
		panel.AddThemeConstantOverride("separation", 16);
		contentMargin.AddChild(panel);

		_selectionSummaryLabel = new Label
		{
			Name = "SelectionSummary",
			HorizontalAlignment = HorizontalAlignment.Center,
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			AutowrapMode = TextServer.AutowrapMode.WordSmart
		};
		ApplySummaryLabelStyle(_selectionSummaryLabel);
		panel.AddChild(_selectionSummaryLabel);

		CenterContainer entryCenter = new()
		{
			Name = "EntryCenter",
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			SizeFlagsVertical = SizeFlags.ExpandFill,
			CustomMinimumSize = new Vector2(0f, 240f),
			MouseFilter = MouseFilterEnum.Ignore
		};
		panel.AddChild(entryCenter);

		_entryContainer = new Control
		{
			AnchorLeft = 0.5f,
			AnchorTop = 0.5f,
			AnchorRight = 0.5f,
			AnchorBottom = 0.5f,
			OffsetLeft = 0f,
			OffsetTop = 0f,
			OffsetRight = 0f,
			OffsetBottom = 0f,
			MouseFilter = MouseFilterEnum.Ignore
		};
		entryCenter.AddChild(_entryContainer);

		for (int index = 0; index < _entries.Count; index++)
		{
			DebuffSelectionEntry entry = _entries[index];
			NGridCardHolder optionHolder = CreateEntryHolder(entry);
			_entryContainer.AddChild(optionHolder);
			_entryByHolder[optionHolder] = entry;
			_entryHolders.Add(optionHolder);
		}

		_pagerBar = new HBoxContainer
		{
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			Alignment = BoxContainer.AlignmentMode.Center
		};
		_pagerBar.AddThemeConstantOverride("separation", 12);
		panel.AddChild(_pagerBar);

		_prevPageButton = CreateActionButton(
			new LocString("gameplay_ui", "REFORGE.UI.DEBUFF_SELECTION.PREV_PAGE").GetFormattedText(),
			new Vector2(276f, 72f));
		_prevPageButton.Pressed += () => ChangePage(_currentPage - 1);
		_pagerBar.AddChild((Control)_prevPageButton);

		_pageLabel = new Label
		{
			HorizontalAlignment = HorizontalAlignment.Center,
			SizeFlagsHorizontal = SizeFlags.ExpandFill
		};
		ApplySummaryLabelStyle(_pageLabel);
		_pagerBar.AddChild(_pageLabel);

		_nextPageButton = CreateActionButton(
			new LocString("gameplay_ui", "REFORGE.UI.DEBUFF_SELECTION.NEXT_PAGE").GetFormattedText(),
			new Vector2(276f, 72f));
		_nextPageButton.Pressed += () => ChangePage(_currentPage + 1);
		_pagerBar.AddChild((Control)_nextPageButton);

		_bottomBar = new HBoxContainer
		{
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			Alignment = BoxContainer.AlignmentMode.Center
		};
		_bottomBar.AddThemeConstantOverride("separation", 20);
		panel.AddChild(_bottomBar);

		_cancelButton = CreateActionButton(
			new LocString("gameplay_ui", "REFORGE.UI.DEBUFF_SELECTION.CANCEL").GetFormattedText(),
			new Vector2(276f, 72f));
		_cancelButton.Visible = _cancelable;
		_cancelButton.Pressed += () => CompleteWithCurrentSelectionOrCancel(forceCancel: true);
		_bottomBar.AddChild((Control)_cancelButton);
	}

	private NGridCardHolder CreateEntryHolder(DebuffSelectionEntry entry)
	{
		CardModel carrierCard = CreateDebuffPreviewCardModel(entry);
		NCard? cardNode = NCard.Create(carrierCard);
		if (cardNode == null)
		{
			throw new InvalidOperationException("Failed to create NCard for debuff selection entry.");
		}

		cardNode.Scale = Vector2.One;
		cardNode.MouseFilter = MouseFilterEnum.Ignore;
		cardNode.UpdateVisuals(PileType.None, CardPreviewMode.Normal);
		ApplyDebuffTextToCard(cardNode, entry);
		HideCardCostVisuals(cardNode);

		NGridCardHolder? holder = NGridCardHolder.Create(cardNode);
		if (holder == null)
		{
			throw new InvalidOperationException("Failed to create NGridCardHolder for debuff selection entry.");
		}

		holder.CustomMinimumSize = new Vector2(300f, 422f);
		holder.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
		holder.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		holder.MouseFilter = MouseFilterEnum.Stop;
		holder.FocusMode = FocusModeEnum.All;
		holder.SetClickable(isClickable: true);
		holder.Position = Vector2.Zero;
		holder.Scale = holder.SmallScale;

		holder.Connect(NCardHolder.SignalName.Pressed, Callable.From<NCardHolder>(OnEntryHolderPressed));

		cardNode.Connect(Node.SignalName.Ready, Callable.From(() =>
		{
			if (!GodotObject.IsInstanceValid(cardNode))
			{
				return;
			}

			cardNode.ActivateRewardScreenGlow();
			cardNode.CardHighlight.AnimHideInstantly();
		}));

		_entryCardByHolder[holder] = cardNode;
		return holder;
	}

	private void OnEntryHolderPressed(NCardHolder cardHolder)
	{
		if (cardHolder is not NGridCardHolder holder || !_entryByHolder.ContainsKey(holder))
		{
			return;
		}

		if (_maxSelect == 1)
		{
			_selectedHolders.Clear();
			_selectedHolders.Add(holder);
			RefreshSelectionState();
			CompleteWithCurrentSelectionOrCancel(forceCancel: false);
			return;
		}

		if (_selectedHolders.Contains(holder))
		{
			_selectedHolders.Remove(holder);
		}
		else
		{
			if (_selectedHolders.Count >= _maxSelect)
			{
				return;
			}

			_selectedHolders.Add(holder);
		}

		RefreshSelectionState();

		if (_selectedHolders.Count >= _maxSelect)
		{
			CompleteWithCurrentSelectionOrCancel(forceCancel: false);
		}
	}

	private static CardModel CreateDebuffPreviewCardModel(DebuffSelectionEntry entry)
	{
		CardModel prototype = ResolveCarrierCardModel(entry);
		if (TryCreateOfficialCardInstance(prototype, out CardModel? runtimeCard) && runtimeCard != null)
		{
			return runtimeCard;
		}

		return prototype.ToMutable();
	}

	private static bool TryCreateOfficialCardInstance(CardModel canonicalCard, out CardModel? runtimeCard)
	{
		runtimeCard = null;
		try
		{
			RunState? runState = RunManager.Instance?.DebugOnlyGetState();
			if (runState == null)
			{
				return false;
			}

			Player? localPlayer = LocalContext.GetMe(runState);
			if (localPlayer == null)
			{
				return false;
			}

			if (CombatManager.Instance.IsInProgress && localPlayer.Creature?.CombatState != null)
			{
				runtimeCard = localPlayer.Creature.CombatState.CreateCard(canonicalCard, localPlayer);
			}
			else
			{
				runtimeCard = runState.CreateCard(canonicalCard, localPlayer);
			}

			return runtimeCard != null;
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[ReForge.UI.DebuffSelection] Failed to create official runtime card for preview: {ex.Message}");
			return false;
		}
	}

	private static void ApplyDebuffTextToCard(NCard cardNode, DebuffSelectionEntry entry)
	{
		string debuffTitle = entry.Debuff.Title.GetFormattedText();
		if (string.IsNullOrWhiteSpace(debuffTitle))
		{
			debuffTitle = entry.Debuff.Id.Entry;
		}

		string debuffDescription = BuildDebuffDescription(entry);

		if (cardNode.GetNodeOrNull<MegaLabel>("%TitleLabel") is MegaLabel titleLabel)
		{
			titleLabel.SetTextAutoSize(debuffTitle);
			titleLabel.Text = debuffTitle;
		}

		if (cardNode.GetNodeOrNull<MegaRichTextLabel>("%DescriptionLabel") is MegaRichTextLabel descriptionLabel)
		{
			descriptionLabel.Text = "[center]" + debuffDescription + "[/center]";
		}
	}

	private static string BuildDebuffDescription(DebuffSelectionEntry entry)
	{
		LocString description = entry.Debuff.Description;
		description.Add("Amount", entry.Amount);
		description.Add("OnPlayer", true);
		description.Add("IsMultiplayer", false);
		description.Add("PlayerCount", 1);
		description.Add("OwnerName", string.Empty);
		description.Add("ApplierName", string.Empty);
		description.Add("TargetName", string.Empty);

		string text = description.GetFormattedText();
		if (!string.IsNullOrWhiteSpace(text))
		{
			return text;
		}

		return entry.Debuff.DumbHoverTip.Description;
	}

	private static void HideCardCostVisuals(NCard cardNode)
	{
		if (cardNode.GetNodeOrNull<Control>("%EnergyIcon") is Control energyIcon)
		{
			energyIcon.Visible = false;
		}

		if (cardNode.GetNodeOrNull<Control>("%StarIcon") is Control starIcon)
		{
			starIcon.Visible = false;
		}

		if (cardNode.GetNodeOrNull<Control>("%UnplayableEnergyIcon") is Control unplayableEnergyIcon)
		{
			unplayableEnergyIcon.Visible = false;
		}

		if (cardNode.GetNodeOrNull<Control>("%UnplayableStarIcon") is Control unplayableStarIcon)
		{
			unplayableStarIcon.Visible = false;
		}
	}

	private static CardModel ResolveCarrierCardModel(DebuffSelectionEntry entry)
	{
		string powerEntry = entry.Debuff.Id.Entry;
		string normalizedEntry = powerEntry.EndsWith("_POWER", StringComparison.OrdinalIgnoreCase)
			? powerEntry[..^6]
			: powerEntry;

		CardModel? matchedCard = ModelDb.AllCards.FirstOrDefault(card =>
			string.Equals(card.Id.Entry, powerEntry, StringComparison.OrdinalIgnoreCase)
			|| string.Equals(card.Id.Entry, normalizedEntry, StringComparison.OrdinalIgnoreCase));

		CardModel prototype = matchedCard ?? ModelDb.Card<Disintegration>();
		return prototype;
	}

	private void RefreshSelectionState()
	{
		int selectedCount = _selectedHolders.Count;

		if (_selectionSummaryLabel != null)
		{
			_selectionSummaryLabel.Text = FormatLocTemplate(
				"gameplay_ui",
				"REFORGE.UI.DEBUFF_SELECTION.SUMMARY",
				selectedCount,
				_minSelect,
				_maxSelect);
		}

		bool reachedMax = selectedCount >= _maxSelect;
		foreach (NGridCardHolder holder in _entryByHolder.Keys)
		{
			bool isSelected = _selectedHolders.Contains(holder);
			bool isDisabled = !isSelected && (reachedMax || !holder.Visible);
			holder.SetClickable(!isDisabled);
			holder.Modulate = isDisabled ? new Color(1f, 1f, 1f, 0.62f) : Colors.White;
			ApplySelectionHighlight(holder);
		}

		RefreshPaginationState();
	}

	private void CompleteWithCurrentSelectionOrCancel(bool forceCancel)
	{
		if (_completionSource.Task.IsCompleted)
		{
			return;
		}

		if (forceCancel)
		{
			_completionSource.SetResult(Array.Empty<DebuffSelectionEntry>());
			return;
		}

		List<DebuffSelectionEntry> selected = GetSelectedButtons()
			.Select(holder => _entryByHolder[holder])
			.ToList();

		if (selected.Count < _minSelect || selected.Count > _maxSelect)
		{
			RefreshSelectionState();
			return;
		}

		_completionSource.SetResult(selected);
	}

	private List<NGridCardHolder> GetSelectedButtons()
	{
		return _selectedHolders.ToList();
	}

	private List<NGridCardHolder> GetVisibleEntryHolders()
	{
		return _entryHolders.Where(static holder => holder.Visible).ToList();
	}

	private void ChangePage(int page)
	{
		int maxPageIndex = Math.Max(0, TotalPages - 1);
		int clamped = Math.Clamp(page, 0, maxPageIndex);
		if (clamped == _currentPage)
		{
			return;
		}

		_currentPage = clamped;
		RefreshPageVisibility();
		RefreshSelectionState();
		PlayVisibleEntriesAnimateInOfficial();
		ActiveScreenContext.Instance.Update();
	}

	private void RefreshPageVisibility()
	{
		(int start, int endExclusive) = GetPageRange(_currentPage);
		for (int i = 0; i < _entryHolders.Count; i++)
		{
			NGridCardHolder holder = _entryHolders[i];
			bool visible = i >= start && i < endExclusive;
			holder.Visible = visible;
			if (!visible)
			{
				holder.SetClickable(isClickable: false);
			}
			ApplySelectionHighlight(holder);
		}

		ApplyOfficialEntryLayout();

		UpdateFocusNeighborsForVisibleEntries();
		RefreshPaginationState();
	}

	private void ApplyOfficialEntryLayout()
	{
		if (!IsInsideTree())
		{
			return;
		}

		List<NGridCardHolder> visibleHolders = GetVisibleEntryHolders();
		if (visibleHolders.Count == 0)
		{
			return;
		}

		Vector2 basePos = Vector2.Left * (visibleHolders.Count - 1) * CardXOffset * 0.5f;
		for (int i = 0; i < visibleHolders.Count; i++)
		{
			NGridCardHolder holder = visibleHolders[i];
			holder.Position = basePos + Vector2.Right * CardXOffset * i;
			holder.Modulate = Colors.White;
		}
	}

	private void PlayVisibleEntriesAnimateInOfficial()
	{
		if (!IsInsideTree())
		{
			return;
		}

		List<NGridCardHolder> visibleHolders = GetVisibleEntryHolders();
		if (visibleHolders.Count == 0)
		{
			return;
		}

		_entryRowTween?.Kill();
		_entryRowTween = CreateTween().SetParallel();

		Vector2 basePos = Vector2.Left * (visibleHolders.Count - 1) * CardXOffset * 0.5f;
		for (int i = 0; i < visibleHolders.Count; i++)
		{
			NGridCardHolder holder = visibleHolders[i];
			Vector2 targetPos = basePos + Vector2.Right * CardXOffset * i;
			float delay = (float)i / Math.Max(1, visibleHolders.Count) * 0.2f;

			holder.Position = targetPos + EntryAnimPosOffset;
			holder.Modulate = Colors.Black;

			_entryRowTween.TweenProperty(holder, "position", targetPos, 0.4f)
				.SetDelay(delay)
				.SetEase(Tween.EaseType.Out)
				.SetTrans(Tween.TransitionType.Back);

			_entryRowTween.TweenProperty(holder, "modulate", Colors.White, 0.4f)
				.SetDelay(delay)
				.SetEase(Tween.EaseType.Out)
				.SetTrans(Tween.TransitionType.Expo);
		}
	}

	private void UpdateFocusNeighborsForVisibleEntries()
	{
		List<NGridCardHolder> visibleButtons = GetVisibleEntryHolders();
		for (int i = 0; i < visibleButtons.Count; i++)
		{
			NGridCardHolder current = visibleButtons[i];
			NGridCardHolder left = visibleButtons[(i - 1 + visibleButtons.Count) % visibleButtons.Count];
			NGridCardHolder right = visibleButtons[(i + 1) % visibleButtons.Count];
			current.FocusNeighborTop = current.GetPath();
			current.FocusNeighborBottom = current.GetPath();
			current.FocusNeighborLeft = left.GetPath();
			current.FocusNeighborRight = right.GetPath();
		}
	}

	private void RefreshPaginationState()
	{
		if (_prevPageButton != null)
		{
			_prevPageButton.Visible = TotalPages > 1;
			_prevPageButton.Disabled = _currentPage <= 0;
			ApplyActionButtonDisabledState(_prevPageButton, _prevPageButton.Disabled);
		}

		if (_nextPageButton != null)
		{
			_nextPageButton.Visible = TotalPages > 1;
			_nextPageButton.Disabled = _currentPage >= TotalPages - 1;
			ApplyActionButtonDisabledState(_nextPageButton, _nextPageButton.Disabled);
		}

		if (_pageLabel != null)
		{
			if (TotalPages <= 1)
			{
				_pageLabel.Text = string.Empty;
			}
			else
			{
				_pageLabel.Text = FormatLocTemplate(
					"gameplay_ui",
					"REFORGE.UI.DEBUFF_SELECTION.PAGE_STATUS",
					_currentPage + 1,
					TotalPages);
			}
		}
	}

	private (int Start, int EndExclusive) GetPageRange(int page)
	{
		int start = page * EntriesPerPage;
		int endExclusive = Math.Min(start + EntriesPerPage, _entryHolders.Count);
		return (start, endExclusive);
	}

	private int TotalPages => Math.Max(1, (_entryHolders.Count + EntriesPerPage - 1) / EntriesPerPage);

	private static string BuildEntryText(DebuffSelectionEntry entry)
	{
		string title = entry.Debuff.Title.GetFormattedText();
		if (string.IsNullOrWhiteSpace(title))
		{
			title = entry.Debuff.Id.Entry;
		}

		return FormatLocTemplate(
			"gameplay_ui",
			"REFORGE.UI.DEBUFF_SELECTION.ENTRY",
			title,
			entry.Amount);
	}

	private static string FormatLocTemplate(string table, string key, params object[] args)
	{
		LocString loc = new(table, key);
		string template = loc.GetRawText();
		if (string.IsNullOrEmpty(template))
		{
			GD.Print($"[ReForge.UI.DebuffSelection] Missing template text for {table}.{key}.");
			return string.Empty;
		}

		try
		{
			return string.Format(template, args);
		}
		catch (FormatException ex)
		{
			GD.PrintErr($"[ReForge.UI.DebuffSelection] Template format error at {table}.{key}. template='{template}', error='{ex.Message}'.");
			return template;
		}
	}

	private void LoadOfficialUiResources()
	{
		if (ResourceLoader.Exists(CommonBannerScenePath))
		{
			_commonBannerScene = ResourceLoader.Load<PackedScene>(CommonBannerScenePath);
		}

		if (ResourceLoader.Exists(RewardBannerTexturePath))
		{
			_rewardBannerTexture = ResourceLoader.Load<Texture2D>(RewardBannerTexturePath);
		}

		if (ResourceLoader.Exists(RewardSkipButtonTexturePath))
		{
			_rewardSkipButtonTexture = ResourceLoader.Load<Texture2D>(RewardSkipButtonTexturePath);
		}

		if (ResourceLoader.Exists(BannerFontPath))
		{
			_bannerFont = ResourceLoader.Load<Font>(BannerFontPath);
		}

		if (ResourceLoader.Exists(ActionFontPath))
		{
			_actionFont = ResourceLoader.Load<Font>(ActionFontPath);
		}
	}

	private Control CreateBannerRoot(string titleText)
	{
		if (_commonBannerScene?.Instantiate() is Control officialBanner)
		{
			officialBanner.Name = "OfficialStyleBanner";
			officialBanner.AnchorLeft = 0.5f;
			officialBanner.AnchorTop = 0f;
			officialBanner.AnchorRight = 0.5f;
			officialBanner.AnchorBottom = 0f;
			officialBanner.OffsetLeft = -327f;
			officialBanner.OffsetTop = 74f;
			officialBanner.OffsetRight = 327f;
			officialBanner.OffsetBottom = 236f;
			officialBanner.MouseFilter = MouseFilterEnum.Ignore;
			officialBanner.Connect(Node.SignalName.Ready, Callable.From(() =>
			{
				if (!GodotObject.IsInstanceValid(officialBanner))
				{
					return;
				}

				officialBanner.Modulate = Colors.White;
				ApplyBannerTitle(officialBanner, ResolveOverlayTitleText());

				if (officialBanner.HasMethod("AnimateIn"))
				{
					officialBanner.CallDeferred("AnimateIn");
				}
			}));

			return officialBanner;
		}

		Control bannerRoot = new()
		{
			Name = "OfficialStyleBanner",
			AnchorLeft = 0.5f,
			AnchorTop = 0f,
			AnchorRight = 0.5f,
			AnchorBottom = 0f,
			OffsetLeft = -327f,
			OffsetTop = 84f,
			OffsetRight = 327f,
			OffsetBottom = 246f,
			MouseFilter = MouseFilterEnum.Ignore
		};

		TextureRect bannerTexture = new()
		{
			AnchorRight = 1f,
			AnchorBottom = 1f,
			ExpandMode = TextureRect.ExpandModeEnum.FitWidthProportional,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
			Texture = _rewardBannerTexture,
			MouseFilter = MouseFilterEnum.Ignore
		};
		bannerRoot.AddChild(bannerTexture);

		Label bannerLabel = new()
		{
			Text = titleText,
			AnchorLeft = 0f,
			AnchorTop = 0f,
			AnchorRight = 1f,
			AnchorBottom = 1f,
			OffsetLeft = 104f,
			OffsetTop = 21f,
			OffsetRight = -100f,
			OffsetBottom = -56f,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			MouseFilter = MouseFilterEnum.Ignore
		};
		ApplyBannerLabelStyle(bannerLabel);
		bannerRoot.AddChild(bannerLabel);

		return bannerRoot;
	}

	private void ApplyBannerTitle(Control bannerRoot, string titleText)
	{
		if (bannerRoot is NCommonBanner commonBanner)
		{
			if (commonBanner.label != null)
			{
				commonBanner.label.SetTextAutoSize(titleText);
				commonBanner.label.Text = titleText;
				ApplyBannerLabelStyle(commonBanner.label);
				return;
			}

			if (commonBanner.GetNodeOrNull<MegaLabel>("MegaLabel") is MegaLabel commonLabel)
			{
				commonLabel.SetTextAutoSize(titleText);
				commonLabel.Text = titleText;
				ApplyBannerLabelStyle(commonLabel);
				return;
			}

			GD.PrintErr("[ReForge.UI.DebuffSelection] NCommonBanner label is not ready yet.");
			return;
		}

		if (bannerRoot.GetNodeOrNull<MegaLabel>("MegaLabel") is MegaLabel megaLabel)
		{
			megaLabel.SetTextAutoSize(titleText);
			megaLabel.Text = titleText;
			ApplyBannerLabelStyle(megaLabel);
			return;
		}

		if (bannerRoot.GetNodeOrNull<Label>("MegaLabel") is Label label)
		{
			if (label.HasMethod("SetTextAutoSize"))
			{
				label.Call("SetTextAutoSize", titleText);
			}
			label.Text = titleText;
			ApplyBannerLabelStyle(label);
		}
	}

	private string ResolveOverlayTitleText()
	{
		string formatted = _title.GetFormattedText();
		if (!string.IsNullOrWhiteSpace(formatted))
		{
			return formatted;
		}

		string raw = _title.GetRawText();
		if (!string.IsNullOrWhiteSpace(raw))
		{
			return raw;
		}

		GD.PrintErr($"[ReForge.UI.DebuffSelection] Missing overlay title text. table={_title.LocTable}, key={_title.LocEntryKey}");
		return "Debuff Selection";
	}

	private BaseButton CreateActionButton(string text, Vector2 size)
	{
		if (_rewardSkipButtonTexture != null)
		{
			TextureButton textureButton = new()
			{
				CustomMinimumSize = size,
				TextureNormal = _rewardSkipButtonTexture,
				TextureHover = _rewardSkipButtonTexture,
				TexturePressed = _rewardSkipButtonTexture,
				TextureDisabled = _rewardSkipButtonTexture,
				IgnoreTextureSize = true,
				StretchMode = TextureButton.StretchModeEnum.Scale,
				FocusMode = FocusModeEnum.All,
				MouseFilter = MouseFilterEnum.Stop
			};

			Label label = new()
			{
				Text = text,
				AnchorLeft = 0f,
				AnchorTop = 0f,
				AnchorRight = 1f,
				AnchorBottom = 1f,
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Center,
				MouseFilter = MouseFilterEnum.Ignore,
				AutowrapMode = TextServer.AutowrapMode.Off
			};
			ApplyActionLabelStyle(label);
			textureButton.AddChild(label);

			return textureButton;
		}

		Button fallback = new()
		{
			Text = text,
			CustomMinimumSize = size,
			FocusMode = FocusModeEnum.All,
			MouseFilter = MouseFilterEnum.Stop,
			Flat = true
		};
		ApplyActionLabelStyle(fallback);
		return fallback;
	}

	private void ApplyEntryButtonTheme(Button button)
	{
		button.AddThemeColorOverride("font_color", ActionFontColor);
		button.AddThemeColorOverride("font_outline_color", ActionOutlineColor);
		button.AddThemeColorOverride("font_hover_color", ActionFontColor);
		button.AddThemeColorOverride("font_hover_pressed_color", ActionFontColor);
		button.AddThemeColorOverride("font_pressed_color", ActionFontColor);
		button.AddThemeColorOverride("font_disabled_color", new Color(0.82f, 0.79f, 0.73f, 0.6f));
		button.AddThemeConstantOverride("outline_size", 8);
		button.AddThemeConstantOverride("h_separation", 8);
		button.AddThemeConstantOverride("line_spacing", 4);
		if (_actionFont != null)
		{
			button.AddThemeFontOverride("font", _actionFont);
		}
		button.AddThemeFontSizeOverride("font_size", 30);
	}

	private void ApplySelectionHighlight(NGridCardHolder holder)
	{
		if (!_entryCardByHolder.TryGetValue(holder, out NCard? cardNode))
		{
			return;
		}

		if (!cardNode.IsNodeReady())
		{
			cardNode.Connect(Node.SignalName.Ready, Callable.From(() =>
			{
				if (!GodotObject.IsInstanceValid(this) || !GodotObject.IsInstanceValid(holder))
				{
					return;
				}

				ApplySelectionHighlight(holder);
			}));
			return;
		}

		if (!holder.Visible)
		{
			cardNode.CardHighlight.AnimHideInstantly();
			return;
		}

		// 对齐官方多选高亮：选中即显示 CardHighlight，取消则隐藏。
		if (_selectedHolders.Contains(holder))
		{
			cardNode.CardHighlight.AnimShow();
		}
		else
		{
			cardNode.CardHighlight.AnimHide();
		}
	}

	private void TryCompleteSelectionIfValid()
	{
		if (_completionSource.Task.IsCompleted)
		{
			return;
		}

		List<NGridCardHolder> selectedButtons = GetSelectedButtons();
		if (selectedButtons.Count < _minSelect || selectedButtons.Count > _maxSelect)
		{
			return;
		}

		CompleteWithCurrentSelectionOrCancel(forceCancel: false);
	}

	private void ApplyBannerLabelStyle(Label label)
	{
		label.AddThemeColorOverride("font_color", BannerFontColor);
		label.AddThemeColorOverride("font_outline_color", BannerOutlineColor);
		label.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 0.12549f));
		label.AddThemeConstantOverride("outline_size", 16);
		label.AddThemeConstantOverride("shadow_offset_x", 4);
		label.AddThemeConstantOverride("shadow_offset_y", 4);
		if (_bannerFont != null)
		{
			label.AddThemeFontOverride("font", _bannerFont);
		}
		label.AddThemeFontSizeOverride("font_size", 31);
	}

	private void ApplySummaryLabelStyle(Label label)
	{
		label.AddThemeColorOverride("font_color", SummaryFontColor);
		label.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.5f));
		label.AddThemeConstantOverride("outline_size", 10);
		if (_actionFont != null)
		{
			label.AddThemeFontOverride("font", _actionFont);
		}
		label.AddThemeFontSizeOverride("font_size", 24);
	}

	private void ApplyActionLabelStyle(Control control)
	{
		control.AddThemeColorOverride("font_color", ActionFontColor);
		control.AddThemeColorOverride("font_outline_color", ActionOutlineColor);
		control.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 0.25098f));
		control.AddThemeConstantOverride("outline_size", 12);
		control.AddThemeConstantOverride("shadow_offset_x", 5);
		control.AddThemeConstantOverride("shadow_offset_y", 3);
		if (_actionFont != null)
		{
			control.AddThemeFontOverride("font", _actionFont);
		}
		control.AddThemeFontSizeOverride("font_size", 34);
	}

	private static void ApplyActionButtonDisabledState(BaseButton button, bool isDisabled)
	{
		if (button is TextureButton)
		{
			button.Modulate = isDisabled ? new Color(1f, 1f, 1f, 0.45f) : Colors.White;
			return;
		}

		button.Modulate = isDisabled ? new Color(1f, 1f, 1f, 0.7f) : Colors.White;
	}
}
