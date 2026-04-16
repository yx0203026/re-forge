#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using ReForgeFramework.Api.Events;

namespace ReForgeFramework.BattleEvents;

internal sealed class CombatTimelyEventRegistry
{
	private readonly object _syncRoot = new();
	private readonly Dictionary<string, CombatTimelyEventBase> _eventsById = new(StringComparer.Ordinal);
	private CombatTimelyEventBase[] _cachedOrdered = Array.Empty<CombatTimelyEventBase>();
	private bool _dirty = true;

	public CombatTimelyEventRegistrationResult Register(CombatTimelyEventBase @event)
	{
		ArgumentNullException.ThrowIfNull(@event);
		string eventId = NormalizeRequired(@event.EventId, nameof(@event.EventId));

		lock (_syncRoot)
		{
			bool replaced = _eventsById.ContainsKey(eventId);
			_eventsById[eventId] = @event;
			_dirty = true;

			return new CombatTimelyEventRegistrationResult(
				Success: true,
				Replaced: replaced,
				EventId: eventId,
				Message: replaced ? "replaced" : "registered"
			);
		}
	}

	public bool Unregister(string eventId)
	{
		string normalizedId = NormalizeRequired(eventId, nameof(eventId));
		lock (_syncRoot)
		{
			bool removed = _eventsById.Remove(normalizedId);
			if (removed)
			{
				_dirty = true;
			}

			return removed;
		}
	}

	public CombatTimelyEventBase[] SnapshotOrdered()
	{
		lock (_syncRoot)
		{
			if (!_dirty)
			{
				return _cachedOrdered;
			}

			_cachedOrdered = _eventsById.Values
				.OrderByDescending(static e => e.Priority)
				.ThenBy(static e => e.EventId, StringComparer.Ordinal)
				.ToArray();
			_dirty = false;
			return _cachedOrdered;
		}
	}

	public bool TryGet(string eventId, out CombatTimelyEventBase @event)
	{
		string normalizedId = NormalizeRequired(eventId, nameof(eventId));
		lock (_syncRoot)
		{
			return _eventsById.TryGetValue(normalizedId, out @event!);
		}
	}

	public int Count
	{
		get
		{
			lock (_syncRoot)
			{
				return _eventsById.Count;
			}
		}
	}

	private static string NormalizeRequired(string value, string paramName)
	{
		ArgumentNullException.ThrowIfNull(value);
		string normalized = value.Trim();
		if (normalized.Length == 0)
		{
			throw new ArgumentException("Value cannot be empty.", paramName);
		}

		return normalized;
	}
}
