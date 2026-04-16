#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Runs;

namespace ReForgeFramework.Api.Ui;

/// <summary>
/// 卡牌选择面板构建器：统一封装“给定候选卡选择”与“从牌库删牌”两类常见页面。
/// </summary>
public sealed class CardSelectionPanelBuilder
{
	private readonly List<CardModel> _cards = new();
	private readonly HashSet<ModelId> _cardIds = new();
	private LocString _tip = CardSelectorPrefs.RemoveSelectionPrompt;
	private int _minSelect = 1;
	private int _maxSelect = 1;
	private bool _cancelable = true;
	private bool? _requireManualConfirmation;
	private Comparison<CardModel>? _comparison;
	private Func<CardModel, bool>? _shouldGlowGold;
	private bool _unpoweredPreviews;
	private bool _pretendCardsCanBePlayed;
	private bool _useNetworkChoiceSync = true;
	private CardSelectionMode _mode = CardSelectionMode.CustomCards;
	private Func<CardModel, bool>? _deckRemovalFilter;

	/// <summary>
	/// 指定底部提示文本（支持本地化参数与嵌入式键格式）。
	/// </summary>
	public CardSelectionPanelBuilder WithTip(LocString tip)
	{
		_tip = tip;
		return this;
	}

	/// <summary>
	/// 通过本地化表与键设置底部提示文本。
	/// </summary>
	public CardSelectionPanelBuilder WithTip(string table, string key)
	{
		if (string.IsNullOrWhiteSpace(table))
		{
			throw new ArgumentException("Localization table cannot be null or whitespace.", nameof(table));
		}

		if (string.IsNullOrWhiteSpace(key))
		{
			throw new ArgumentException("Localization key cannot be null or whitespace.", nameof(key));
		}

		_tip = new LocString(table, key);
		return this;
	}

	/// <summary>
	/// 配置可选择数量区间。
	/// </summary>
	public CardSelectionPanelBuilder WithSelectCount(int minSelect, int maxSelect)
	{
		if (minSelect < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(minSelect), minSelect, "minSelect must be >= 0.");
		}

		if (maxSelect <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(maxSelect), maxSelect, "maxSelect must be > 0.");
		}

		if (minSelect > maxSelect)
		{
			throw new ArgumentException("minSelect must be <= maxSelect.");
		}

		_minSelect = minSelect;
		_maxSelect = maxSelect;
		return this;
	}

	public CardSelectionPanelBuilder WithCancelable(bool cancelable)
	{
		_cancelable = cancelable;
		return this;
	}

	public CardSelectionPanelBuilder WithRequireManualConfirmation(bool requireManualConfirmation)
	{
		_requireManualConfirmation = requireManualConfirmation;
		return this;
	}

	public CardSelectionPanelBuilder WithComparison(Comparison<CardModel> comparison)
	{
		ArgumentNullException.ThrowIfNull(comparison);
		_comparison = comparison;
		return this;
	}

	public CardSelectionPanelBuilder WithShouldGlowGold(Func<CardModel, bool> shouldGlowGold)
	{
		ArgumentNullException.ThrowIfNull(shouldGlowGold);
		_shouldGlowGold = shouldGlowGold;
		return this;
	}

	public CardSelectionPanelBuilder WithUnpoweredPreviews(bool unpoweredPreviews = true)
	{
		_unpoweredPreviews = unpoweredPreviews;
		return this;
	}

	public CardSelectionPanelBuilder WithPretendCardsCanBePlayed(bool pretendCardsCanBePlayed = true)
	{
		_pretendCardsCanBePlayed = pretendCardsCanBePlayed;
		return this;
	}

	/// <summary>
	/// 是否使用官方联机选择同步流程（PlayerChoiceSynchronizer）。
	/// 在“开局本地选牌 + 奖励消息同步”场景可关闭，避免额外 choiceId 漂移。
	/// </summary>
	public CardSelectionPanelBuilder WithNetworkChoiceSync(bool useNetworkChoiceSync = true)
	{
		_useNetworkChoiceSync = useNetworkChoiceSync;
		return this;
	}

	/// <summary>
	/// 添加候选卡（去重规则为 ModelId）。
	/// </summary>
	public CardSelectionPanelBuilder AddCard(CardModel card)
	{
		ArgumentNullException.ThrowIfNull(card);

		if (_cardIds.Add(card.Id))
		{
			_cards.Add(card);
		}

		return this;
	}

	/// <summary>
	/// 按卡牌类型添加候选卡（来自 ModelDb 规范实例）。
	/// </summary>
	public CardSelectionPanelBuilder AddCard<TCard>() where TCard : CardModel
	{
		return AddCard(ModelDb.Card<TCard>());
	}

	/// <summary>
	/// 按卡牌 Entry 添加候选卡，大小写不敏感。
	/// </summary>
	public CardSelectionPanelBuilder AddCard(string cardEntry)
	{
		if (string.IsNullOrWhiteSpace(cardEntry))
		{
			throw new ArgumentException("cardEntry cannot be null or whitespace.", nameof(cardEntry));
		}

		CardModel? card = ModelDb.AllCards.FirstOrDefault(c => string.Equals(c.Id.Entry, cardEntry, StringComparison.OrdinalIgnoreCase));
		if (card == null)
		{
			throw new InvalidOperationException($"Card entry '{cardEntry}' was not found in ModelDb.AllCards.");
		}

		return AddCard(card);
	}

	/// <summary>
	/// 按完整 ModelId 添加候选卡。
	/// </summary>
	public CardSelectionPanelBuilder AddCard(ModelId id)
	{
		CardModel? card = ModelDb.GetByIdOrNull<CardModel>(id);
		if (card == null)
		{
			throw new InvalidOperationException($"Card model '{id}' was not found in ModelDb.");
		}

		return AddCard(card);
	}

	/// <summary>
	/// 添加整个卡池的所有卡到候选列表。
	/// </summary>
	public CardSelectionPanelBuilder AddPool(CardPoolModel pool)
	{
		ArgumentNullException.ThrowIfNull(pool);

		foreach (CardModel card in pool.AllCards)
		{
			AddCard(card);
		}

		return this;
	}

	/// <summary>
	/// 按卡池类型添加候选卡池。
	/// </summary>
	public CardSelectionPanelBuilder AddPool<TPool>() where TPool : CardPoolModel
	{
		return AddPool(ModelDb.CardPool<TPool>());
	}

	/// <summary>
	/// 切换为“从牌库删牌”页面模式。
	/// </summary>
	public CardSelectionPanelBuilder UseDeckRemovalMode(Func<CardModel, bool>? filter = null)
	{
		_mode = CardSelectionMode.DeckRemoval;
		_deckRemovalFilter = filter;
		return this;
	}

	/// <summary>
	/// 构建并显示页面，返回玩家最终选中的卡牌列表。
	/// </summary>
	public async Task<IReadOnlyList<CardModel>> BuildShow(Player? player = null, PlayerChoiceContext? choiceContext = null)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		Player resolvedPlayer = ResolvePlayer(player);
		GD.Print($"[ReForge.UI.CardSelection] BuildShow start. mode={_mode}, playerNetId={resolvedPlayer.NetId}, candidateCount={_cards.Count}, selectRange={_minSelect}-{_maxSelect}, cancelable={_cancelable}.");
		ReForge.LifecycleSafety.EnsurePlayerChoiceReady(resolvedPlayer, requireOverlayForLocal: true);
		CardSelectorPrefs prefs = BuildPrefs();

		if (_mode == CardSelectionMode.DeckRemoval)
		{
			IEnumerable<CardModel> selectedFromDeck = await CardSelectCmd.FromDeckForRemoval(resolvedPlayer, prefs, _deckRemovalFilter);
			IReadOnlyList<CardModel> deckSelection = selectedFromDeck.ToList();
			GD.Print($"[ReForge.UI.CardSelection] BuildShow finished (deck-removal). selected={deckSelection.Count}, elapsedMs={stopwatch.ElapsedMilliseconds}.");
			return deckSelection;
		}

		if (_cards.Count == 0)
		{
			throw new InvalidOperationException("No candidate cards were provided. Call AddCard/AddPool before BuildShow.");
		}

		PlayerChoiceContext context = ResolveChoiceContext(choiceContext);
		IEnumerable<CardModel> selected;
		if (_useNetworkChoiceSync)
		{
			GD.Print("[ReForge.UI.CardSelection] Invoking CardSelectCmd.FromSimpleGrid.");
			selected = await CardSelectCmd.FromSimpleGrid(context, _cards, resolvedPlayer, prefs);
		}
		else
		{
			GD.Print("[ReForge.UI.CardSelection] Invoking local-only simple-grid selection (network choice sync disabled).");
			selected = await SelectLocalOnlyFromSimpleGrid(prefs);
		}

		IReadOnlyList<CardModel> selection = selected.ToList();
		GD.Print($"[ReForge.UI.CardSelection] BuildShow finished (simple-grid). selected={selection.Count}, elapsedMs={stopwatch.ElapsedMilliseconds}.");
		return selection;
	}

	private async Task<IReadOnlyList<CardModel>> SelectLocalOnlyFromSimpleGrid(CardSelectorPrefs prefs)
	{
		if (CardSelectCmd.Selector != null)
		{
			return (await CardSelectCmd.Selector.GetSelectedCards(_cards, prefs.MinSelect, prefs.MaxSelect)).ToList();
		}

		NPlayerHand.Instance?.CancelAllCardPlay();
		NSimpleCardSelectScreen screen = NSimpleCardSelectScreen.Create(_cards, prefs);
		NOverlayStack? overlayStack = NOverlayStack.Instance;
		if (overlayStack == null)
		{
			throw new InvalidOperationException("NOverlayStack is not ready for local-only card selection.");
		}

		overlayStack.Push(screen);
		return (await screen.CardsSelected()).ToList();
	}

	/// <summary>
	/// 等待选择运行时就绪后再显示页面。
	/// 适用于开局早期阶段，避免 PlayerChoiceSynchronizer/NOverlayStack 尚未初始化导致空引用。
	/// </summary>
	public async Task<IReadOnlyList<CardModel>> BuildShowWhenReady(Player? player = null, PlayerChoiceContext? choiceContext = null, int maxFramesToWait = 600)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		Player resolvedPlayer = ResolvePlayer(player);
		GD.Print($"[ReForge.UI.CardSelection] BuildShowWhenReady waiting. playerNetId={resolvedPlayer.NetId}, maxFrames={maxFramesToWait}.");
		bool ready = await ReForge.LifecycleSafety.WaitForPlayerChoiceReadyAsync(
			resolvedPlayer,
			maxFramesToWait,
			requireOverlayForLocal: true);

		GD.Print($"[ReForge.UI.CardSelection] BuildShowWhenReady wait finished. ready={ready}, elapsedMs={stopwatch.ElapsedMilliseconds}.");

		ReForge.LifecycleSafety.EnsurePlayerChoiceReady(resolvedPlayer, requireOverlayForLocal: true);
		GD.Print("[ReForge.UI.CardSelection] BuildShowWhenReady entering BuildShow.");
		return await BuildShow(resolvedPlayer, choiceContext);
	}

	private CardSelectorPrefs BuildPrefs()
	{
		CardSelectorPrefs prefs = new(_tip, _minSelect, _maxSelect)
		{
			Cancelable = _cancelable,
			Comparison = _comparison,
			UnpoweredPreviews = _unpoweredPreviews,
			PretendCardsCanBePlayed = _pretendCardsCanBePlayed
		};

		if (_requireManualConfirmation.HasValue)
		{
			prefs = prefs with
			{
				RequireManualConfirmation = _requireManualConfirmation.Value
			};
		}

		if (_shouldGlowGold != null)
		{
			prefs.ShouldGlowGold = _shouldGlowGold;
		}

		return prefs;
	}

	private static Player ResolvePlayer(Player? player)
	{
		if (player != null)
		{
			return player;
		}

		RunState? runState = RunManager.Instance?.DebugOnlyGetState();
		if (runState == null)
		{
			throw new InvalidOperationException("Run is not active. Provide an explicit player when outside a run.");
		}

		Player? localPlayer = LocalContext.GetMe(runState);
		if (localPlayer == null)
		{
			throw new InvalidOperationException("Unable to resolve local player from current run state.");
		}

		return localPlayer;
	}

	private static PlayerChoiceContext ResolveChoiceContext(PlayerChoiceContext? explicitContext)
	{
		if (explicitContext != null)
		{
			return explicitContext;
		}

		RunManager? runManager = RunManager.Instance;
		if (runManager?.ActionExecutor?.CurrentlyRunningAction is { } runningAction)
		{
			return new GameActionPlayerChoiceContext(runningAction);
		}

		// 非 GameAction 执行期不能自动走 Hook 上下文；否则会在 SignalPlayerChoiceBegun 中等待
		// AssignTaskAndWaitForPauseOrCompletion 的任务绑定，导致选择 UI 无法弹出。
		return new BlockingPlayerChoiceContext();
	}

	private enum CardSelectionMode
	{
		CustomCards,
		DeckRemoval
	}
}
