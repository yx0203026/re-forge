#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

namespace ReForgeFramework.Api.Ui;

/// <summary>
/// 选牌并授予卡牌到牌组的请求参数。
/// </summary>
public sealed class CardGrantSelectionRequest
{
	public required RunState RunState { get; init; }

	public required Player Player { get; init; }

	public required IReadOnlyList<CardModel> CandidateCards { get; init; }

	public string TipTable { get; init; } = "card_reward_ui";

	public string TipKey { get; init; } = "A11_PICK_ONE_GOLD_TO_HAND_PROMPT";

	public int MinSelect { get; init; } = 1;

	public int MaxSelect { get; init; } = 1;

	public bool Cancelable { get; init; }

	public bool RequireManualConfirmation { get; init; }

	public int MaxFramesToWait { get; init; } = 900;

	public bool MarkSeenAfterGrant { get; init; } = true;

	public bool PlayAddAnimation { get; init; } = true;

	public bool UseNetworkChoiceSync { get; init; } = true;

	public Func<CardModel, bool>? ShouldGlowGold { get; init; }

	public AbstractModel? Source { get; init; }
}

/// <summary>
/// 选牌并授牌执行结果。
/// </summary>
public sealed class CardGrantSelectionResult
{
	public required IReadOnlyList<CardModel> SelectedCards { get; init; }

	public required CardModel SelectedCanonicalCard { get; init; }

	public required CardModel GrantedCard { get; init; }

	public required bool UsedFallbackSelection { get; init; }

	public required bool UsedFallbackInsertion { get; init; }

	public required bool GrantSucceeded { get; init; }

	public required bool AnimationRequested { get; init; }

	public required bool AnimationApplied { get; init; }

	public required long SelectionElapsedMs { get; init; }

	public required long TotalElapsedMs { get; init; }
}

/// <summary>
/// 通用“选卡并加入牌组”服务：
/// - 统一 UI 选择流程；
/// - 优先走原版加牌动画；
/// - 失败时兜底插入牌组；
/// - 可选同步图鉴已发现状态。
/// </summary>
public static class CardGrantService
{
	public static async Task<CardGrantSelectionResult> SelectAndGrantToDeckAsync(CardGrantSelectionRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);
		ArgumentNullException.ThrowIfNull(request.RunState);
		ArgumentNullException.ThrowIfNull(request.Player);
		ArgumentNullException.ThrowIfNull(request.CandidateCards);

		if (request.CandidateCards.Count == 0)
		{
			throw new InvalidOperationException("CandidateCards must contain at least one card.");
		}

		Stopwatch stopwatch = Stopwatch.StartNew();
		var panel = ReForge.UI.CreateCardSelectionPanel()
			.WithTip(request.TipTable, request.TipKey)
			.WithSelectCount(request.MinSelect, request.MaxSelect)
			.WithCancelable(request.Cancelable)
			.WithRequireManualConfirmation(request.RequireManualConfirmation)
			.WithNetworkChoiceSync(request.UseNetworkChoiceSync);

		if (request.ShouldGlowGold != null)
		{
			panel.WithShouldGlowGold(request.ShouldGlowGold);
		}

		for (int i = 0; i < request.CandidateCards.Count; i++)
		{
			panel.AddCard(request.CandidateCards[i]);
		}

		IReadOnlyList<CardModel> selectedCards = await panel.BuildShowWhenReady(
			request.Player,
			new BlockingPlayerChoiceContext(),
			request.MaxFramesToWait);

		long selectionElapsedMs = stopwatch.ElapsedMilliseconds;
		CardModel selectedCard = selectedCards.Count > 0 ? selectedCards[0] : request.CandidateCards[0];
		bool usedFallbackSelection = selectedCards.Count == 0;

		CardModel grantedCard = request.RunState.CreateCard(selectedCard, request.Player);
		DeckGrantResult deckResult = await AddCardToDeckWithFallbackAsync(
			request.RunState,
			request.Player,
			grantedCard,
			request.Source,
			request.PlayAddAnimation,
			request.MarkSeenAfterGrant);

		return new CardGrantSelectionResult
		{
			SelectedCards = selectedCards,
			SelectedCanonicalCard = selectedCard,
			GrantedCard = deckResult.Card,
			UsedFallbackSelection = usedFallbackSelection,
			UsedFallbackInsertion = deckResult.UsedFallbackInsertion,
			GrantSucceeded = deckResult.GrantSucceeded,
			AnimationRequested = request.PlayAddAnimation,
			AnimationApplied = deckResult.AnimationApplied,
			SelectionElapsedMs = selectionElapsedMs,
			TotalElapsedMs = stopwatch.ElapsedMilliseconds
		};
	}

	private static async Task<DeckGrantResult> AddCardToDeckWithFallbackAsync(
		RunState runState,
		Player player,
		CardModel card,
		AbstractModel? source,
		bool playAnimation,
		bool markSeenAfterGrant)
	{
		CardPileAddResult addResult;
		bool animationApplied = false;

		try
		{
			addResult = await CardPileCmd.Add(
				card,
				PileType.Deck,
				CardPilePosition.Bottom,
				source,
				skipVisuals: !playAnimation);
			animationApplied = playAnimation;
		}
		catch (Exception ex) when (playAnimation)
		{
			GD.PrintErr($"[ReForge.UI.CardGrant] Deck add with animation failed. fallbackToNoVisuals=True, card='{card.Id}', error='{ex.Message}'.");
			addResult = await CardPileCmd.Add(
				card,
				PileType.Deck,
				CardPilePosition.Bottom,
				source,
				skipVisuals: true);
		}

		CardModel resolvedCard = addResult.cardAdded;
		bool inDeck = player.Deck.Cards.Contains(resolvedCard);

		if (addResult.success && inDeck)
		{
			if (playAnimation)
			{
				animationApplied = TryPlayDeckAddFlyAnimation(resolvedCard);
			}

			if (!animationApplied)
			{
				resolvedCard.Pile?.InvokeCardAddFinished();
			}

			TrySyncGrantedCardToRewardNetwork(runState, player, resolvedCard);

			if (markSeenAfterGrant)
			{
				TryMarkCardAsSeen(resolvedCard);
			}

			return new DeckGrantResult
			{
				Card = resolvedCard,
				GrantSucceeded = true,
				UsedFallbackInsertion = false,
				AnimationApplied = animationApplied
			};
		}

		if (!runState.ContainsCard(resolvedCard))
		{
			runState.AddCard(resolvedCard, player);
		}

		if (resolvedCard.Pile != null && resolvedCard.Pile != player.Deck)
		{
			resolvedCard.RemoveFromCurrentPile();
		}

		if (!player.Deck.Cards.Contains(resolvedCard))
		{
			player.Deck.AddInternal(resolvedCard);
			resolvedCard.FloorAddedToDeck = runState.TotalFloor;
			runState.CurrentMapPointHistoryEntry?.GetEntry(player.NetId).CardsGained.Add(resolvedCard.ToSerializable());
		}

		if (playAnimation)
		{
			animationApplied = TryPlayDeckAddFlyAnimation(resolvedCard);
		}

		if (!animationApplied)
		{
			resolvedCard.Pile?.InvokeCardAddFinished();
		}

		if (markSeenAfterGrant)
		{
			TryMarkCardAsSeen(resolvedCard);
		}

		TrySyncGrantedCardToRewardNetwork(runState, player, resolvedCard);

		return new DeckGrantResult
		{
			Card = resolvedCard,
			GrantSucceeded = player.Deck.Cards.Contains(resolvedCard),
			UsedFallbackInsertion = true,
			AnimationApplied = animationApplied
		};
	}

	/// <summary>
	/// 复用官方奖励页“拿牌飞向牌库”的表现：
	/// ReparentCard + NCardFlyVfx（isAddingToPile=true）。
	/// </summary>
	private static bool TryPlayDeckAddFlyAnimation(CardModel card)
	{
		try
		{
			NRun? runNode = NRun.Instance;
			if (runNode?.GlobalUi == null)
			{
				return false;
			}

			NCard? cardNode = NCard.Create(card);
			if (cardNode == null)
			{
				return false;
			}

			NGlobalUi globalUi = runNode.GlobalUi;
			globalUi.CardPreviewContainer.AddChild(cardNode);
			cardNode.UpdateVisuals(PileType.None, CardPreviewMode.Normal);
			cardNode.GlobalPosition = globalUi.GetViewportRect().Size * 0.5f;

			globalUi.ReparentCard(cardNode);
			Vector2 targetPosition = PileType.Deck.GetTargetPosition(cardNode);
			NCardFlyVfx? flyVfx = NCardFlyVfx.Create(cardNode, targetPosition, isAddingToPile: true, card.Owner.Character.TrailPath);
			if (flyVfx == null)
			{
				cardNode.QueueFree();
				return false;
			}

			globalUi.TopBar.TrailContainer.AddChild(flyVfx);
			return true;
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[ReForge.UI.CardGrant] Deck add fly animation failed for '{card.Id}': {ex.Message}");
			return false;
		}
	}

	private static void TrySyncGrantedCardToRewardNetwork(RunState runState, Player player, CardModel card)
	{
		try
		{
			RunManager? runManager = RunManager.Instance;
			if (runManager == null || runManager.IsSinglePlayerOrFakeMultiplayer)
			{
				return;
			}

			Player? localPlayer = LocalContext.GetMe(runState);
			if (localPlayer == null || localPlayer.NetId != player.NetId)
			{
				return;
			}

			runManager.RewardSynchronizer.SyncLocalObtainedCard(card);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[ReForge.UI.CardGrant] SyncLocalObtainedCard failed for '{card.Id}': {ex.Message}");
		}
	}

	private static void TryMarkCardAsSeen(CardModel card)
	{
		try
		{
			SaveManager? saveManager = SaveManager.Instance;
			if (saveManager == null)
			{
				GD.PrintErr($"[ReForge.UI.CardGrant] MarkCardAsSeen skipped: SaveManager unavailable. card='{card.Id}'.");
				return;
			}

			saveManager.MarkCardAsSeen(card);
			saveManager.SaveProgressFile();
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[ReForge.UI.CardGrant] MarkCardAsSeen failed for '{card.Id}': {ex.Message}");
		}
	}

	private sealed class DeckGrantResult
	{
		public required CardModel Card { get; init; }

		public required bool GrantSucceeded { get; init; }

		public required bool UsedFallbackInsertion { get; init; }

		public required bool AnimationApplied { get; init; }
	}
}
