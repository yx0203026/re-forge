#nullable enable

using System;
using Godot;
using ReForgeFramework.Api.Events;

namespace ReForgeFramework.BattleEvents.Events;

/// <summary>
/// 通用可配置战斗及时事件。
/// 支持通过委托注入完全自定义的触发条件与触发行为。
/// </summary>
public sealed class ConfigurableCombatTimelyEvent : CombatTimelyEventBase
{
	private readonly string _eventId;
	private readonly Func<CombatTimelyEventContext, bool>? _canTrigger;
	private readonly Action<CombatTimelyEventContext>? _onEnter;
	private readonly Action<CombatTimelyEventContext>? _onTriggered;
	private readonly Action<CombatTimelyEventContext>? _onLeave;

	public ConfigurableCombatTimelyEvent(
		string eventId,
		Func<CombatTimelyEventContext, bool>? canTrigger = null,
		Action<CombatTimelyEventContext>? onEnter = null,
		Action<CombatTimelyEventContext>? onTriggered = null,
		Action<CombatTimelyEventContext>? onLeave = null)
	{
		if (string.IsNullOrWhiteSpace(eventId))
		{
			throw new ArgumentException("eventId cannot be null or empty.", nameof(eventId));
		}

		_eventId = eventId.Trim();
		_canTrigger = canTrigger;
		_onEnter = onEnter;
		_onTriggered = onTriggered;
		_onLeave = onLeave;
	}

	public override string EventId => _eventId;

	protected override bool CanTrigger(in CombatTimelyEventContext context)
	{
		if (_canTrigger == null)
		{
			return true;
		}

		try
		{
			return _canTrigger(context);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[ReForge.BattleEvents] custom trigger condition failed. eventId='{EventId}'. {ex}");
			return false;
		}
	}

	protected override void OnEnter(in CombatTimelyEventContext context)
	{
		_onEnter?.Invoke(context);
	}

	protected override void OnTriggered(in CombatTimelyEventContext context)
	{
		_onTriggered?.Invoke(context);
	}

	protected override void OnLeave(in CombatTimelyEventContext context)
	{
		_onLeave?.Invoke(context);
	}
}