#nullable enable

using System;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Runs;
using ReForgeFramework.EventBus;

/// <summary>
/// ReForge Ascension 模型基类：
/// 模组开发者仅需继承此类并实现基础定义，即可通过 ReForge.Models 完成注册。
/// </summary>
public abstract class ReForgeAscensionModel
{
	/// <summary>
	/// 扩展难度等级，必须大于 10。
	/// </summary>
	public abstract int Level { get; }

	/// <summary>
	/// 难度标题。
	/// </summary>
	public abstract string Title { get; }

	/// <summary>
	/// 难度描述。
	/// </summary>
	public abstract string Description { get; }

	/// <summary>
	/// 是否自动将当前模型的效果注册到 ReForge.Ascension。
	/// 默认为 true。
	/// </summary>
	public virtual bool AutoRegisterLevelEffect => true;

	/// <summary>
	/// 效果生效最低难度阈值。
	/// 默认为当前 Level。
	/// </summary>
	public virtual int EffectMinimumAscension => Level;

	/// <summary>
	/// 当运行满足阈值时触发的难度效果。
	/// </summary>
	public virtual void OnLevelEffect(RunState runState, int currentAscension)
	{
	}

	/// <summary>
	/// 注册完成后的扩展钩子，可用于绑定事件总线或额外初始化。
	/// </summary>
	public virtual void OnRegistered()
	{
	}

	/// <summary>
	/// 是否启用 RunStarted 钩子。
	/// </summary>
	public virtual bool EnableRunStartedHook => true;

	/// <summary>
	/// 运行是否满足当前 Ascension 模型生效条件。
	/// </summary>
	protected virtual bool ShouldHandleRun(IRunState runState, int currentAscension)
	{
		return currentAscension >= Level;
	}

	/// <summary>
	/// 运行开始钩子。
	/// </summary>
	protected virtual void OnRunStarted(RunState runState, int currentAscension)
	{
	}

	/// <summary>
	/// 战斗开始前。
	/// </summary>
	protected virtual void OnCombatStartBefore(CombatStartBeforeEvent evt, IRunState runState, int currentAscension)
	{
	}

	/// <summary>
	/// 战斗结束后（含胜负）。
	/// </summary>
	protected virtual void OnCombatEndAfter(CombatEndAfterEvent evt, IRunState runState, int currentAscension)
	{
	}

	/// <summary>
	/// 战斗胜利后。
	/// </summary>
	protected virtual void OnCombatVictoryAfter(CombatVictoryAfterEvent evt, IRunState runState, int currentAscension)
	{
	}

	/// <summary>
	/// 回合开始前。
	/// </summary>
	protected virtual void OnTurnStartBefore(TurnPhaseEvent evt, IRunState runState, int currentAscension)
	{
	}

	/// <summary>
	/// 回合开始后。
	/// </summary>
	protected virtual void OnTurnStartAfter(TurnPhaseEvent evt, IRunState runState, int currentAscension)
	{
	}

	/// <summary>
	/// 回合结束前。
	/// </summary>
	protected virtual void OnTurnEndBefore(TurnPhaseEvent evt, IRunState runState, int currentAscension)
	{
	}

	/// <summary>
	/// 回合结束后。
	/// </summary>
	protected virtual void OnTurnEndAfter(TurnPhaseEvent evt, IRunState runState, int currentAscension)
	{
	}

	/// <summary>
	/// 自动打牌前。
	/// </summary>
	protected virtual void OnCardAutoPlayBefore(CardAutoPlayBeforeEvent evt, IRunState runState, int currentAscension)
	{
	}

	/// <summary>
	/// 卡牌打出前。
	/// </summary>
	protected virtual void OnCardPlayBefore(CardPlayBeforeEvent evt, IRunState runState, int currentAscension)
	{
	}

	/// <summary>
	/// 卡牌打出后。
	/// </summary>
	protected virtual void OnCardPlayAfter(CardPlayAfterEvent evt, IRunState runState, int currentAscension)
	{
	}

	/// <summary>
	/// 玩家受伤前。
	/// </summary>
	protected virtual void OnPlayerDamageBefore(PlayerDamageBeforeEvent evt, IRunState runState, int currentAscension)
	{
	}

	/// <summary>
	/// 玩家受伤后。
	/// </summary>
	protected virtual void OnPlayerDamageAfter(PlayerDamageAfterEvent evt, IRunState runState, int currentAscension)
	{
	}

	/// <summary>
	/// 怪物受伤前。
	/// </summary>
	protected virtual void OnMonsterDamageBefore(MonsterDamageBeforeEvent evt, IRunState runState, int currentAscension)
	{
	}

	/// <summary>
	/// 怪物受伤后。
	/// </summary>
	protected virtual void OnMonsterDamageAfter(MonsterDamageAfterEvent evt, IRunState runState, int currentAscension)
	{
	}

	internal void RegisterRuntimeHooks(string logOwner)
	{
		string owner = string.IsNullOrWhiteSpace(logOwner) ? "ReForge.AscensionModel" : logOwner;
		string busPrefix = BuildBusPrefix();

		if (EnableRunStartedHook)
		{
			_ = ReForge.Mods.TryHookRunStartedWithRetry(HandleRunStarted, $"{owner}:{busPrefix}:run-started");
		}

		ReForge.EventBus.RegisterListener<CombatStartBeforeEvent>(
			CombatLifecycleEventIds.CombatStartBefore,
			$"{busPrefix}.combat-start-before",
			evt => InvokeIfEligible(evt.RunState, asc => OnCombatStartBefore(evt, evt.RunState, asc))
		);

		ReForge.EventBus.RegisterListener<CombatEndAfterEvent>(
			CombatLifecycleEventIds.CombatEndAfter,
			$"{busPrefix}.combat-end-after",
			evt =>
			{
				if (evt.CombatState == null)
				{
					return;
				}

				InvokeIfEligible(evt.RunState, asc => OnCombatEndAfter(evt, evt.RunState, asc));
			}
		);

		ReForge.EventBus.RegisterListener<CombatVictoryAfterEvent>(
			CombatLifecycleEventIds.CombatVictoryAfter,
			$"{busPrefix}.combat-victory-after",
			evt =>
			{
				if (evt.CombatState == null)
				{
					return;
				}

				InvokeIfEligible(evt.RunState, asc => OnCombatVictoryAfter(evt, evt.RunState, asc));
			}
		);

		ReForge.EventBus.RegisterListener<TurnPhaseEvent>(
			CombatLifecycleEventIds.TurnStartBefore,
			$"{busPrefix}.turn-start-before",
			evt => InvokeFromCombat(evt.CombatState, asc => OnTurnStartBefore(evt, evt.CombatState.RunState, asc))
		);

		ReForge.EventBus.RegisterListener<TurnPhaseEvent>(
			CombatLifecycleEventIds.TurnStartAfter,
			$"{busPrefix}.turn-start-after",
			evt => InvokeFromCombat(evt.CombatState, asc => OnTurnStartAfter(evt, evt.CombatState.RunState, asc))
		);

		ReForge.EventBus.RegisterListener<TurnPhaseEvent>(
			CombatLifecycleEventIds.TurnEndBefore,
			$"{busPrefix}.turn-end-before",
			evt => InvokeFromCombat(evt.CombatState, asc => OnTurnEndBefore(evt, evt.CombatState.RunState, asc))
		);

		ReForge.EventBus.RegisterListener<TurnPhaseEvent>(
			CombatLifecycleEventIds.TurnEndAfter,
			$"{busPrefix}.turn-end-after",
			evt => InvokeFromCombat(evt.CombatState, asc => OnTurnEndAfter(evt, evt.CombatState.RunState, asc))
		);

		ReForge.EventBus.RegisterListener<CardAutoPlayBeforeEvent>(
			CombatLifecycleEventIds.CardAutoPlayBefore,
			$"{busPrefix}.card-auto-play-before",
			evt => InvokeFromCombat(evt.CombatState, asc => OnCardAutoPlayBefore(evt, evt.CombatState.RunState, asc))
		);

		ReForge.EventBus.RegisterListener<CardPlayBeforeEvent>(
			CombatLifecycleEventIds.CardPlayBefore,
			$"{busPrefix}.card-play-before",
			evt => InvokeFromCombat(evt.CombatState, asc => OnCardPlayBefore(evt, evt.CombatState.RunState, asc))
		);

		ReForge.EventBus.RegisterListener<CardPlayAfterEvent>(
			CombatLifecycleEventIds.CardPlayAfter,
			$"{busPrefix}.card-play-after",
			evt => InvokeFromCombat(evt.CombatState, asc => OnCardPlayAfter(evt, evt.CombatState.RunState, asc))
		);

		ReForge.EventBus.RegisterListener<PlayerDamageBeforeEvent>(
			CombatLifecycleEventIds.PlayerDamageBefore,
			$"{busPrefix}.player-damage-before",
			evt => InvokeIfEligible(ResolveRunState(evt.CombatState, evt.Player), asc => OnPlayerDamageBefore(evt, ResolveRunState(evt.CombatState, evt.Player)!, asc))
		);

		ReForge.EventBus.RegisterListener<PlayerDamageAfterEvent>(
			CombatLifecycleEventIds.PlayerDamageAfter,
			$"{busPrefix}.player-damage-after",
			evt => InvokeIfEligible(ResolveRunState(evt.CombatState, evt.Player), asc => OnPlayerDamageAfter(evt, ResolveRunState(evt.CombatState, evt.Player)!, asc))
		);

		ReForge.EventBus.RegisterListener<MonsterDamageBeforeEvent>(
			CombatLifecycleEventIds.MonsterDamageBefore,
			$"{busPrefix}.monster-damage-before",
			evt =>
			{
				IRunState? runState = evt.CombatState?.RunState;
				InvokeIfEligible(runState, asc => OnMonsterDamageBefore(evt, runState!, asc));
			}
		);

		ReForge.EventBus.RegisterListener<MonsterDamageAfterEvent>(
			CombatLifecycleEventIds.MonsterDamageAfter,
			$"{busPrefix}.monster-damage-after",
			evt =>
			{
				IRunState? runState = evt.CombatState?.RunState;
				InvokeIfEligible(runState, asc => OnMonsterDamageAfter(evt, runState!, asc));
			}
		);
	}

	private void HandleRunStarted(RunState runState)
	{
		InvokeIfEligible(runState, asc => OnRunStarted(runState, asc));
	}

	private void InvokeFromCombat(CombatState? combatState, Action<int> callback)
	{
		if (combatState == null)
		{
			return;
		}

		InvokeIfEligible(combatState.RunState, callback);
	}

	private void InvokeIfEligible(IRunState? runState, Action<int> callback)
	{
		if (runState == null)
		{
			return;
		}

		int currentAscension = runState.AscensionLevel;
		if (!ShouldHandleRun(runState, currentAscension))
		{
			return;
		}

		callback(currentAscension);
	}

	private static IRunState? ResolveRunState(CombatState? combatState, Player? player)
	{
		if (combatState != null)
		{
			return combatState.RunState;
		}

		return player?.RunState;
	}

	private string BuildBusPrefix()
	{
		return string.Concat(
			"reforge.ascension.model.",
			GetType().FullName ?? GetType().Name,
			".",
			Level.ToString()
		).Replace('+', '.');
	}
}
