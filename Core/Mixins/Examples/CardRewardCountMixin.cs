#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Rewards;

namespace ReForgeFramework.Mixins.Examples;

/// <summary>
/// 将常规卡牌奖励候选数量从 3 提升到 4。
/// 实现方式：在 CardReward.Populate() 结束后补充 1 张额外候选牌。
/// </summary>
[global::ReForge.Mixin(typeof(CardReward), Id = "reforge.card-reward-count")]
public static class CardRewardCountMixin
{
	private const int OriginalCount = 3;
	private const int TargetCount = 4;

	private static readonly FieldInfo CardsField = typeof(CardReward)
		.GetField("_cards", BindingFlags.Instance | BindingFlags.NonPublic)
		?? throw new MissingFieldException(typeof(CardReward).FullName, "_cards");

	private static readonly PropertyInfo OptionsProperty = typeof(CardReward)
		.GetProperty("Options", BindingFlags.Instance | BindingFlags.NonPublic)
		?? throw new MissingMemberException(typeof(CardReward).FullName, "Options");

	private static readonly MethodInfo CreateForRewardMethod = typeof(CardFactory)
		.GetMethods(BindingFlags.Public | BindingFlags.Static)
		.FirstOrDefault(m => m.Name == "CreateForReward" && m.GetParameters().Length == 3)
		?? throw new MissingMethodException(typeof(CardFactory).FullName, "CreateForReward(Player,int,options)");

	private static readonly PropertyInfo OptionCountProperty = typeof(CardReward)
		.GetProperty("OptionCount", BindingFlags.Instance | BindingFlags.NonPublic)
		?? throw new MissingMemberException(typeof(CardReward).FullName, "OptionCount");

	[global::ReForge.Postfix("Populate")]
	private static void PopulatePostfix(CardReward __instance)
	{
		if (__instance == null)
		{
			return;
		}

		try
		{
			if (!ShouldExpandReward(__instance, out List<CardCreationResult>? cards, out object? options))
			{
				return;
			}

			if (cards == null || options == null)
			{
				return;
			}

			// 统一把所有“原始 3 选 1”扩成 4 选 1（含手动构造奖励）。
			int beforeCount = cards.Count;
			int generationAttempts = 0;

			while (cards.Count < TargetCount && generationAttempts < 6)
			{
				generationAttempts++;
				CardCreationResult? extra = InvokeCreateForReward(__instance, options).FirstOrDefault();
				if (extra == null)
				{
					continue;
				}

				if (cards.Any(c => c.Card == extra.Card))
				{
					continue;
				}

				cards.Add(extra);
			}

			// 兜底：如果卡池环境无法生成额外新牌，至少补齐为 4 个选项。
			while (cards.Count < TargetCount && cards.Count > 0)
			{
				cards.Add(new CardCreationResult(cards[0].Card));
			}

			if (cards.Count > beforeCount)
			{
				GD.Print($"[ReForge.Mixins] CardReward options expanded from {beforeCount} to {cards.Count}.");
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[ReForge.Mixins] Failed to expand card reward options. {ex}");
		}
	}

	private static bool ShouldExpandReward(
		CardReward reward,
		out List<CardCreationResult>? cards,
		out object? options)
	{
		cards = null;
		options = null;

		int optionCount = Convert.ToInt32(OptionCountProperty.GetValue(reward));
		if (optionCount != OriginalCount)
		{
			return false;
		}

		cards = CardsField.GetValue(reward) as List<CardCreationResult>;
		if (cards == null || cards.Count <= 0 || cards.Count >= TargetCount)
		{
			return false;
		}

		options = OptionsProperty.GetValue(reward);
		if (options == null)
		{
			return false;
		}

		return true;
	}

	private static IEnumerable<CardCreationResult> InvokeCreateForReward(CardReward reward, object options)
	{
		object? result = CreateForRewardMethod.Invoke(obj: null, new object[] { reward.Player, 1, options });
		if (result is IEnumerable<CardCreationResult> typed)
		{
			return typed;
		}

		return Array.Empty<CardCreationResult>();
	}
}
