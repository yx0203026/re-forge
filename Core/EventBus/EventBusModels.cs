#nullable enable

using System;

namespace ReForgeFramework.EventBus;

internal sealed class EventSubscription
{
	public EventSubscription(string eventId, string busId, Type? parameterType, Action<IEventArg?> handler, string source)
	{
		ArgumentNullException.ThrowIfNull(eventId);
		ArgumentNullException.ThrowIfNull(busId);
		ArgumentNullException.ThrowIfNull(handler);
		ArgumentNullException.ThrowIfNull(source);

		EventId = eventId;
		BusId = busId;
		ParameterType = parameterType;
		Handler = handler;
		Source = source;
	}

	public string EventId { get; }

	public string BusId { get; }

	public Type? ParameterType { get; }

	public Action<IEventArg?> Handler { get; }

	public string Source { get; }
}
