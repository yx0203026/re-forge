#nullable enable

using System;
using ReForgeFramework.Api.Events;

namespace ReForgeFramework.BattleEvents;

/// <summary>
/// 战斗及时事件基类。
/// 
/// 设计目标：
/// 1. 提供进入/触发/离开三个生命周期钩子。
/// 2. 使用显式 Trigger 调用，避免每帧轮询。
/// 3. 支持服务端触发后广播到客户端本地执行。
/// </summary>
public abstract class CombatTimelyEventBase
{
	private readonly object _syncRoot = new();
	private int _enterCount;
	private int _triggerCount;
	private int _leaveCount;

	/// <summary>
	/// 事件唯一 ID。建议使用反向域名风格，确保跨模组不冲突。
	/// </summary>
	public abstract string EventId { get; }

	/// <summary>
	/// 调度优先级，值越大越先执行。
	/// </summary>
	public virtual int Priority => 0;

	public int EnterCount
	{
		get
		{
			lock (_syncRoot)
			{
				return _enterCount;
			}
		}
	}

	public int TriggerCount
	{
		get
		{
			lock (_syncRoot)
			{
				return _triggerCount;
			}
		}
	}

	public int LeaveCount
	{
		get
		{
			lock (_syncRoot)
			{
				return _leaveCount;
			}
		}
	}

	internal bool TryDispatch(in CombatTimelyEventContext context)
	{
		switch (context.Phase)
		{
			case CombatTimelyEventPhase.Enter:
				MarkEnter();
				OnEnter(context);
				return true;
			case CombatTimelyEventPhase.Leave:
				MarkLeave();
				OnLeave(context);
				return true;
			case CombatTimelyEventPhase.Trigger:
				if (!CanTrigger(context))
				{
					return false;
				}
				MarkTrigger();
				OnTriggered(context);
				return true;
			default:
				return false;
		}
	}

	internal bool TryDispatchNetworkTrigger(in CombatTimelyEventContext context)
	{
		if (context.Phase != CombatTimelyEventPhase.Trigger)
		{
			return false;
		}

		// 联机同步消息由权威端先完成 CanTrigger 判定，客户端按同步结果强制重放。
		MarkTrigger();
		OnTriggered(context);
		return true;
	}

	protected virtual void OnEnter(in CombatTimelyEventContext context)
	{
	}

	protected virtual void OnTriggered(in CombatTimelyEventContext context)
	{
	}

	protected virtual void OnLeave(in CombatTimelyEventContext context)
	{
	}

	/// <summary>
	/// Trigger 判定入口：返回 true 时才允许执行事件。
	/// </summary>
	protected virtual bool CanTrigger(in CombatTimelyEventContext context)
	{
		return true;
	}

	private void MarkEnter()
	{
		lock (_syncRoot)
		{
			_enterCount++;
		}
	}

	private void MarkTrigger()
	{
		lock (_syncRoot)
		{
			_triggerCount++;
		}
	}

	private void MarkLeave()
	{
		lock (_syncRoot)
		{
			_leaveCount++;
		}
	}

}
