#nullable enable

using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;

namespace ReForgeFramework.Mixins.Examples;

/// <summary>
/// 在怪物创建流程结束后，统一为敌方怪物增加 5 点生命值。
/// </summary>
// [global::ReForge.Mixin(typeof(CombatState), Id = "reforge.monster-hp-plus-five")]
// public static class MonsterHpPlusFiveMixin
// {
// 	private const int BonusHp = 5;

// 	[global::ReForge.Postfix("CreateCreature")]
// 	private static void CreateCreaturePostfix(CombatSide side, Creature __result)
// 	{
// 		if (__result == null || side != CombatSide.Enemy)
// 		{
// 			return;
// 		}

// 		// 先抬高上限，再补当前值，确保表现为“总 HP +5”。
// 		__result.SetMaxHpInternal(__result.MaxHp + BonusHp);
// 		__result.SetCurrentHpInternal(__result.CurrentHp + BonusHp);
// 	}
// }
