#nullable enable

using System;
using System.Collections.Generic;
using Godot;

namespace ReForgeFramework.EventBus;

internal sealed class EventBusDispatcher
{
	private readonly EventBusRegistry _registry;

	public EventBusDispatcher(EventBusRegistry registry)
	{
		_registry = registry;
	}

	public int Publish(string eventId, IEventArg? eventArg)
	{
		ArgumentNullException.ThrowIfNull(eventId);

		IReadOnlyList<EventSubscription> listeners = _registry.Snapshot(eventId);
		if (listeners.Count == 0)
		{
			return 0;
		}

		int invoked = 0;
		for (int i = 0; i < listeners.Count; i++)
		{
			EventSubscription subscription = listeners[i];
			if (!CanHandle(subscription.ParameterType, eventArg))
			{
				string actualType = eventArg?.GetType().FullName ?? "<null>";
				string expectedType = subscription.ParameterType?.FullName ?? "<none>";
				GD.Print($"[ReForge.EventBus] Type mismatch. eventId='{eventId}', busId='{subscription.BusId}', expected='{expectedType}', actual='{actualType}'.");
				continue;
			}

			try
			{
				subscription.Handler(eventArg);
				invoked++;
			}
			catch (Exception exception)
			{
				GD.PrintErr($"[ReForge.EventBus] Listener failed. eventId='{eventId}', busId='{subscription.BusId}', source='{subscription.Source}'. {exception}");
			}
		}

		return invoked;
	}

	private static bool CanHandle(Type? parameterType, IEventArg? eventArg)
	{
		if (parameterType == null)
		{
			return true;
		}

		if (eventArg == null)
		{
			if (!parameterType.IsValueType)
			{
				return true;
			}

			return Nullable.GetUnderlyingType(parameterType) != null;
		}

		return parameterType.IsInstanceOfType(eventArg);
	}
}
