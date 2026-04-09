#nullable enable

using System.Collections.Generic;
using System.Reflection;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Rewards;

namespace ReForgeFramework.Mixins.Examples;

/// <summary>
/// 使用 Shadow 注解绑定目标字段：在 Populate 后把奖励候选补齐到 6（不做修改，这里只是演示）。
/// </summary>
// [global::ReForge.Mixin(typeof(CardReward), Id = "reforge.card-reward-count")]
// public static class CardRewardCountMixin
// {
// 	private const int TargetCount = 6;

// 	[global::ReForge.Shadow(targetName: "_cards")]
// 	private static FieldInfo shadow_cards = null!;

// 	[global::ReForge.Postfix("Populate")]
// 	private static void PopulatePostfix(CardReward __instance)
// 	{
// 		List<CardCreationResult>? cards = shadow_cards.GetValue(__instance) as List<CardCreationResult>;
// 		if (cards == null || cards.Count == 0)
// 		{
// 			return;
// 		}

// 		while (cards.Count < TargetCount)
// 		{
// 			cards.Add(new CardCreationResult(cards[0].Card));
// 		}
// 	}
// }
