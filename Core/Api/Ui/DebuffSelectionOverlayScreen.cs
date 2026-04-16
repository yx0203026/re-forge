#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;

namespace ReForgeFramework.Api.Ui;

internal sealed partial class DebuffSelectionOverlayScreen : Control, IOverlayScreen, IScreenContext
{
	private const int EntriesPerPage = 2;

	private readonly TaskCompletionSource<IReadOnlyList<DebuffSelectionEntry>> _completionSource = new();
	private readonly List<DebuffSelectionEntry> _entries;
	private readonly LocString _title;
	private readonly int _minSelect;
	private readonly int _maxSelect;
	private readonly bool _cancelable;
	private readonly Dictionary<Button, DebuffSelectionEntry> _entryByButton = new();
	private readonly List<Button> _entryButtons = new();
	private int _currentPage;

	private VBoxContainer? _entryContainer;
	private Label? _selectionSummaryLabel;
	private Label? _pageLabel;
	private Button? _prevPageButton;
	private Button? _nextPageButton;
	private Button? _confirmButton;
	private Button? _cancelButton;
	private Tween? _fadeTween;

	public NetScreenType ScreenType => NetScreenType.CardSelection;

	public bool UseSharedBackstop => true;

	public Control DefaultFocusedControl =>
		(Control?)GetVisibleEntryButtons().FirstOrDefault()
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
		BuildVisualTree();
		RefreshSelectionState();
		RefreshPageVisibility();
	}

	public override void _UnhandledInput(InputEvent @event)
	{
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
		if (!_completionSource.Task.IsCompleted)
		{
			_completionSource.SetResult(Array.Empty<DebuffSelectionEntry>());
		}

		base._ExitTree();
	}

	public void AfterOverlayOpened()
	{
		Modulate = Colors.Transparent;
		_fadeTween?.Kill();
		_fadeTween = CreateTween();
		_fadeTween.TweenProperty(this, "modulate:a", 1f, 0.18);
	}

	public void AfterOverlayClosed()
	{
		_fadeTween?.Kill();
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
			Color = new Color(0f, 0f, 0f, 0.62f),
			MouseFilter = MouseFilterEnum.Stop
		};
		AddChild(dimLayer);

		CenterContainer center = new()
		{
			AnchorRight = 1f,
			AnchorBottom = 1f,
			MouseFilter = MouseFilterEnum.Ignore
		};
		AddChild(center);

		PanelContainer panel = new()
		{
			CustomMinimumSize = new Vector2(760f, 420f),
			SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
			SizeFlagsVertical = SizeFlags.ShrinkCenter,
			MouseFilter = MouseFilterEnum.Stop
		};
		center.AddChild(panel);

		MarginContainer margin = new();
		margin.AddThemeConstantOverride("margin_left", 24);
		margin.AddThemeConstantOverride("margin_right", 24);
		margin.AddThemeConstantOverride("margin_top", 22);
		margin.AddThemeConstantOverride("margin_bottom", 20);
		panel.AddChild(margin);

		VBoxContainer root = new()
		{
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			SizeFlagsVertical = SizeFlags.ExpandFill
		};
		root.AddThemeConstantOverride("separation", 12);
		margin.AddChild(root);

		Label titleLabel = new()
		{
			Text = _title.GetFormattedText(),
			HorizontalAlignment = HorizontalAlignment.Center,
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			SizeFlagsHorizontal = SizeFlags.ExpandFill
		};
		root.AddChild(titleLabel);

		_selectionSummaryLabel = new Label
		{
			HorizontalAlignment = HorizontalAlignment.Center,
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			AutowrapMode = TextServer.AutowrapMode.WordSmart
		};
		root.AddChild(_selectionSummaryLabel);

		ScrollContainer scroll = new()
		{
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			SizeFlagsVertical = SizeFlags.ExpandFill,
			CustomMinimumSize = new Vector2(0f, 250f)
		};
		root.AddChild(scroll);

		_entryContainer = new VBoxContainer
		{
			SizeFlagsHorizontal = SizeFlags.ExpandFill
		};
		_entryContainer.AddThemeConstantOverride("separation", 8);
		scroll.AddChild(_entryContainer);

		for (int index = 0; index < _entries.Count; index++)
		{
			DebuffSelectionEntry entry = _entries[index];
			Button optionButton = CreateEntryButton(entry);
			_entryContainer.AddChild(optionButton);
			_entryByButton[optionButton] = entry;
			_entryButtons.Add(optionButton);
		}

		HBoxContainer pagerBar = new()
		{
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			Alignment = BoxContainer.AlignmentMode.Center
		};
		pagerBar.AddThemeConstantOverride("separation", 8);
		root.AddChild(pagerBar);

		_prevPageButton = new Button
		{
			Text = new LocString("gameplay_ui", "REFORGE.UI.DEBUFF_SELECTION.PREV_PAGE").GetFormattedText(),
			CustomMinimumSize = new Vector2(120f, 40f),
			MouseFilter = MouseFilterEnum.Stop
		};
		_prevPageButton.Pressed += () => ChangePage(_currentPage - 1);
		pagerBar.AddChild(_prevPageButton);

		_pageLabel = new Label
		{
			HorizontalAlignment = HorizontalAlignment.Center,
			SizeFlagsHorizontal = SizeFlags.ExpandFill
		};
		pagerBar.AddChild(_pageLabel);

		_nextPageButton = new Button
		{
			Text = new LocString("gameplay_ui", "REFORGE.UI.DEBUFF_SELECTION.NEXT_PAGE").GetFormattedText(),
			CustomMinimumSize = new Vector2(120f, 40f),
			MouseFilter = MouseFilterEnum.Stop
		};
		_nextPageButton.Pressed += () => ChangePage(_currentPage + 1);
		pagerBar.AddChild(_nextPageButton);

		HBoxContainer bottomBar = new()
		{
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			Alignment = BoxContainer.AlignmentMode.End
		};
		bottomBar.AddThemeConstantOverride("separation", 10);
		root.AddChild(bottomBar);

		_cancelButton = new Button
		{
			Text = new LocString("gameplay_ui", "REFORGE.UI.DEBUFF_SELECTION.CANCEL").GetFormattedText(),
			Visible = _cancelable,
			MouseFilter = MouseFilterEnum.Stop,
			CustomMinimumSize = new Vector2(120f, 44f)
		};
		_cancelButton.Pressed += () => CompleteWithCurrentSelectionOrCancel(forceCancel: true);
		bottomBar.AddChild(_cancelButton);

		_confirmButton = new Button
		{
			Text = new LocString("gameplay_ui", "REFORGE.UI.DEBUFF_SELECTION.CONFIRM").GetFormattedText(),
			MouseFilter = MouseFilterEnum.Stop,
			CustomMinimumSize = new Vector2(180f, 44f)
		};
		_confirmButton.Pressed += () => CompleteWithCurrentSelectionOrCancel(forceCancel: false);
		bottomBar.AddChild(_confirmButton);
	}

	private Button CreateEntryButton(DebuffSelectionEntry entry)
	{
		Button button = new()
		{
			ToggleMode = true,
			Text = BuildEntryText(entry),
			Alignment = HorizontalAlignment.Left,
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			CustomMinimumSize = new Vector2(0f, 52f),
			FocusMode = FocusModeEnum.All,
			MouseFilter = MouseFilterEnum.Stop
		};

		button.Pressed += () =>
		{
			if (!button.ButtonPressed)
			{
				RefreshSelectionState();
				return;
			}

			if (_maxSelect == 1)
			{
				foreach (Button sibling in _entryByButton.Keys)
				{
					if (sibling != button)
					{
						sibling.ButtonPressed = false;
					}
				}
			}

			RefreshSelectionState();
		};

		return button;
	}

	private void RefreshSelectionState()
	{
		int selectedCount = GetSelectedButtons().Count;
		bool canConfirm = selectedCount >= _minSelect && selectedCount <= _maxSelect;

		if (_confirmButton != null)
		{
			_confirmButton.Disabled = !canConfirm;
		}

		if (_selectionSummaryLabel != null)
		{
			string summaryTemplate = new LocString("gameplay_ui", "REFORGE.UI.DEBUFF_SELECTION.SUMMARY").GetFormattedText();
			_selectionSummaryLabel.Text = string.Format(summaryTemplate, selectedCount, _minSelect, _maxSelect);
		}

		bool reachedMax = selectedCount >= _maxSelect;
		foreach (Button button in _entryByButton.Keys)
		{
			if (!button.ButtonPressed)
			{
				button.Disabled = reachedMax || !button.Visible;
			}
			else
			{
				button.Disabled = !button.Visible;
			}
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
			.Select(button => _entryByButton[button])
			.ToList();

		if (selected.Count < _minSelect || selected.Count > _maxSelect)
		{
			RefreshSelectionState();
			return;
		}

		_completionSource.SetResult(selected);
	}

	private List<Button> GetSelectedButtons()
	{
		return _entryByButton.Keys
			.Where(static button => button.ButtonPressed)
			.ToList();
	}

	private List<Button> GetVisibleEntryButtons()
	{
		return _entryButtons.Where(static button => button.Visible).ToList();
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
		ActiveScreenContext.Instance.Update();
	}

	private void RefreshPageVisibility()
	{
		(int start, int endExclusive) = GetPageRange(_currentPage);
		for (int i = 0; i < _entryButtons.Count; i++)
		{
			Button button = _entryButtons[i];
			bool visible = i >= start && i < endExclusive;
			button.Visible = visible;
		}

		UpdateFocusNeighborsForVisibleEntries();
		RefreshPaginationState();
	}

	private void UpdateFocusNeighborsForVisibleEntries()
	{
		List<Button> visibleButtons = GetVisibleEntryButtons();
		for (int i = 0; i < visibleButtons.Count; i++)
		{
			Button current = visibleButtons[i];
			Button left = visibleButtons[(i - 1 + visibleButtons.Count) % visibleButtons.Count];
			Button right = visibleButtons[(i + 1) % visibleButtons.Count];
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
		}

		if (_nextPageButton != null)
		{
			_nextPageButton.Visible = TotalPages > 1;
			_nextPageButton.Disabled = _currentPage >= TotalPages - 1;
		}

		if (_pageLabel != null)
		{
			if (TotalPages <= 1)
			{
				_pageLabel.Text = string.Empty;
			}
			else
			{
				string pageTemplate = new LocString("gameplay_ui", "REFORGE.UI.DEBUFF_SELECTION.PAGE_STATUS").GetFormattedText();
				_pageLabel.Text = string.Format(pageTemplate, _currentPage + 1, TotalPages);
			}
		}
	}

	private (int Start, int EndExclusive) GetPageRange(int page)
	{
		int start = page * EntriesPerPage;
		int endExclusive = Math.Min(start + EntriesPerPage, _entryButtons.Count);
		return (start, endExclusive);
	}

	private int TotalPages => Math.Max(1, (_entryButtons.Count + EntriesPerPage - 1) / EntriesPerPage);

	private static string BuildEntryText(DebuffSelectionEntry entry)
	{
		string title = entry.Debuff.Title.GetFormattedText();
		if (string.IsNullOrWhiteSpace(title))
		{
			title = entry.Debuff.Id.Entry;
		}

		string entryTemplate = new LocString("gameplay_ui", "REFORGE.UI.DEBUFF_SELECTION.ENTRY").GetFormattedText();
		return string.Format(entryTemplate, title, entry.Amount);
	}
}
