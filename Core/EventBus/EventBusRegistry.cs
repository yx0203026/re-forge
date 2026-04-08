#nullable enable

using System;
using System.Collections.Generic;

namespace ReForgeFramework.EventBus;

internal sealed class EventBusRegistry
{
	private readonly object _syncRoot = new();
	private readonly Dictionary<string, Dictionary<string, EventSubscription>> _subscriptionsByEvent =
		new(StringComparer.Ordinal);

	public bool Upsert(EventSubscription subscription)
	{
		ArgumentNullException.ThrowIfNull(subscription);

		lock (_syncRoot)
		{
			if (!_subscriptionsByEvent.TryGetValue(subscription.EventId, out Dictionary<string, EventSubscription>? listeners))
			{
				listeners = new Dictionary<string, EventSubscription>(StringComparer.Ordinal);
				_subscriptionsByEvent[subscription.EventId] = listeners;
			}

			bool replaced = listeners.ContainsKey(subscription.BusId);
			listeners[subscription.BusId] = subscription;
			return replaced;
		}
	}

	public int RemoveByBusId(string busId)
	{
		ArgumentNullException.ThrowIfNull(busId);

		lock (_syncRoot)
		{
			int removed = 0;
			List<string>? emptyEventIds = null;

			foreach ((string eventId, Dictionary<string, EventSubscription> listeners) in _subscriptionsByEvent)
			{
				if (listeners.Remove(busId))
				{
					removed++;
				}

				if (listeners.Count == 0)
				{
					emptyEventIds ??= new List<string>();
					emptyEventIds.Add(eventId);
				}
			}

			if (emptyEventIds != null)
			{
				for (int i = 0; i < emptyEventIds.Count; i++)
				{
					_subscriptionsByEvent.Remove(emptyEventIds[i]);
				}
			}

			return removed;
		}
	}

	public IReadOnlyList<EventSubscription> Snapshot(string eventId)
	{
		ArgumentNullException.ThrowIfNull(eventId);

		lock (_syncRoot)
		{
			if (!_subscriptionsByEvent.TryGetValue(eventId, out Dictionary<string, EventSubscription>? listeners) || listeners.Count == 0)
			{
				return Array.Empty<EventSubscription>();
			}

			EventSubscription[] snapshot = new EventSubscription[listeners.Count];
			int index = 0;
			foreach ((_, EventSubscription subscription) in listeners)
			{
				snapshot[index++] = subscription;
			}

			return snapshot;
		}
	}
}
