#nullable enable

using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;

namespace ReForgeFramework.EventBus;

/// <summary>
/// 战斗生命周期事件 ID 定义。
/// </summary>
public static class CombatLifecycleEventIds
{
	// 战斗流程相关
	public const string CombatStartBefore = "reforge.combat.start.before";
	public const string CombatEndAfter = "reforge.combat.end.after";
	public const string CombatVictoryAfter = "reforge.combat.victory.after";

	// 卡牌打出相关
	public const string CardAutoPlayBefore = "reforge.combat.card.auto-play.before";
	public const string CardPlayBefore = "reforge.combat.card.play.before";
	public const string CardPlayAfter = "reforge.combat.card.play.after";

	// 回合流程相关
	public const string TurnStartBefore = "reforge.combat.turn.start.before";
	public const string TurnStartAfter = "reforge.combat.turn.start.after";
	public const string TurnEndBefore = "reforge.combat.turn.end.before";
	public const string TurnEndAfter = "reforge.combat.turn.end.after";

	// 伤害相关（按受伤对象类型拆分）
	public const string PlayerDamageBefore = "reforge.combat.damage.player.before";
	public const string PlayerDamageAfter = "reforge.combat.damage.player.after";
	public const string MonsterDamageBefore = "reforge.combat.damage.monster.before";
	public const string MonsterDamageAfter = "reforge.combat.damage.monster.after";
}

/// <summary>
/// 战斗开始前事件。
/// </summary>
public readonly record struct CombatStartBeforeEvent(
	IRunState RunState,
	CombatState? CombatState
) : IEventArg;

/// <summary>
/// 战斗结束事件（通用结束，含胜负）。
/// </summary>
public readonly record struct CombatEndAfterEvent(
	IRunState RunState,
	CombatState? CombatState,
	CombatRoom Room
) : IEventArg;

/// <summary>
/// 战斗胜利事件。
/// </summary>
public readonly record struct CombatVictoryAfterEvent(
	IRunState RunState,
	CombatState? CombatState,
	CombatRoom Room
) : IEventArg;

/// <summary>
/// 自动打牌前事件。
/// </summary>
public readonly record struct CardAutoPlayBeforeEvent(
	CombatState CombatState,
	CardModel Card,
	Creature? Target,
	AutoPlayType AutoPlayType
) : IEventArg;

/// <summary>
/// 卡牌打出前事件。
/// </summary>
public readonly record struct CardPlayBeforeEvent(
	CombatState CombatState,
	CardPlay CardPlay
) : IEventArg;

/// <summary>
/// 卡牌打出后事件。
/// </summary>
public readonly record struct CardPlayAfterEvent(
	CombatState CombatState,
	CardPlay CardPlay
) : IEventArg;

/// <summary>
/// 回合阶段事件（开始/结束，前后阶段）。
/// </summary>
public readonly record struct TurnPhaseEvent(
	CombatState CombatState,
	CombatSide Side
) : IEventArg;

/// <summary>
/// 玩家受伤前事件。
/// </summary>
public readonly record struct PlayerDamageBeforeEvent(
	CombatState? CombatState,
	Creature Target,
	Player Player,
	Creature? Dealer,
	CardModel? CardSource,
	decimal Amount,
	ValueProp Props
) : IEventArg;

/// <summary>
/// 玩家受伤后事件。
/// </summary>
public readonly record struct PlayerDamageAfterEvent(
	CombatState? CombatState,
	Creature Target,
	Player Player,
	Creature? Dealer,
	CardModel? CardSource,
	DamageResult Result,
	ValueProp Props
) : IEventArg;

/// <summary>
/// 怪物受伤前事件。
/// </summary>
public readonly record struct MonsterDamageBeforeEvent(
	CombatState? CombatState,
	Creature Target,
	MonsterModel Monster,
	Creature? Dealer,
	CardModel? CardSource,
	decimal Amount,
	ValueProp Props
) : IEventArg;

/// <summary>
/// 怪物受伤后事件。
/// </summary>
public readonly record struct MonsterDamageAfterEvent(
	CombatState? CombatState,
	Creature Target,
	MonsterModel Monster,
	Creature? Dealer,
	CardModel? CardSource,
	DamageResult Result,
	ValueProp Props
) : IEventArg;
