#nullable enable

using System;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Runs;
using ReForgeFramework.Api.Events;
using ReForgeFramework.Networking;

namespace ReForgeFramework.BattleEvents;

internal sealed class CombatTimelyEventRuntime
{
	private readonly CombatTimelyEventRegistry _registry;
	private readonly object _syncRoot = new();

	private bool _isInCombat;
	private long _sequence;

	public CombatTimelyEventRuntime(CombatTimelyEventRegistry registry)
	{
		_registry = registry ?? throw new ArgumentNullException(nameof(registry));
	}

	public bool IsInCombat
	{
		get
		{
			lock (_syncRoot)
			{
				return _isInCombat;
			}
		}
	}

	public void EnterCombat(IRunState? runState, CombatState? combatState)
	{
		lock (_syncRoot)
		{
			if (_isInCombat)
			{
				return;
			}

			_isInCombat = true;
		}

		DispatchAll(CreateContext(runState, combatState, CombatTimelyEventPhase.Enter));
	}

	public void LeaveCombat(IRunState? runState, CombatState? combatState)
	{
		bool shouldDispatch;
		lock (_syncRoot)
		{
			shouldDispatch = _isInCombat;
			_isInCombat = false;
		}

		if (!shouldDispatch)
		{
			return;
		}

		DispatchAll(CreateContext(runState, combatState, CombatTimelyEventPhase.Leave));
	}

	public bool TryTrigger(
		string eventId,
		IRunState? runState,
		CombatState? combatState,
		bool broadcastIfAuthority)
	{
		if (!IsInCombat)
		{
			return false;
		}

		if (!_registry.TryGet(eventId, out CombatTimelyEventBase @event))
		{
			GD.PrintErr($"[ReForge.BattleEvents] trigger ignored. event not found. eventId='{eventId}'.");
			return false;
		}

		CombatTimelyEventContext context = CreateContext(runState, combatState, CombatTimelyEventPhase.Trigger);
		bool triggered;
		try
		{
			triggered = @event.TryDispatch(context);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[ReForge.BattleEvents] trigger failed. eventId='{@event.EventId}'. {ex}");
			return false;
		}

		if (!triggered)
		{
			return false;
		}

		if (!broadcastIfAuthority || !ReForge.Network.IsConnected || !ReForge.BattleEvents.IsAuthority())
		{
			return true;
		}

		long sequence;
		lock (_syncRoot)
		{
			_sequence++;
			sequence = _sequence;
		}

		ReForge.Network.Send(new ReForgeCombatTimelyEventSyncMessage
		{
			Sequence = sequence,
			UtcNowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
			EventId = @event.EventId
		});

		return true;
	}

	public void OnNetworkSyncReceived(ReForgeCombatTimelyEventSyncMessage message, ulong senderId)
	{
		ArgumentNullException.ThrowIfNull(message);
		if (string.IsNullOrWhiteSpace(message.EventId))
		{
			return;
		}

		CombatState? combatState = CombatManager.Instance.DebugOnlyGetState();
		IRunState? runState = combatState?.RunState;
		bool triggered = TryTrigger(message.EventId, runState, combatState, broadcastIfAuthority: false);
		if (!triggered)
		{
			GD.Print($"[ReForge.BattleEvents] network trigger ignored. sender={senderId}, eventId='{message.EventId}'.");
			return;
		}

		GD.Print($"[ReForge.BattleEvents] network trigger executed. sender={senderId}, sequence={message.Sequence}, eventId='{message.EventId}'.");
	}

	private void DispatchAll(in CombatTimelyEventContext context)
	{
		CombatTimelyEventBase[] events = _registry.SnapshotOrdered();
		for (int i = 0; i < events.Length; i++)
		{
			CombatTimelyEventBase @event = events[i];
			try
			{
				@event.TryDispatch(context);
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[ReForge.BattleEvents] dispatch failed. eventId='{@event.EventId}', phase={context.Phase}. {ex}");
			}
		}
	}

	private CombatTimelyEventContext CreateContext(
		IRunState? runState,
		CombatState? combatState,
		CombatTimelyEventPhase phase)
	{
		return new CombatTimelyEventContext(
			RunState: runState,
			CombatState: combatState,
			UtcNowMs: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
			IsAuthority: ReForge.BattleEvents.IsAuthority(),
			IsMultiplayer: ReForge.Network.IsConnected,
			Phase: phase
		);
	}
}
