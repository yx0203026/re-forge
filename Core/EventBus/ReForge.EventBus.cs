#nullable enable

using System;
using System.Reflection;
using Godot;
using ReForgeFramework.EventBus;

public static partial class ReForge
{
	public static class EventBus
	{
		private static readonly object SyncRoot = new();
		private static readonly EventBusRegistry Registry = new();
		private static readonly EventBusDispatcher Dispatcher = new(Registry);
		private static readonly EventBusAttributeScanner AttributeScanner = new(Registry);

		private static bool _initialized;

		[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
		public sealed class ListenerAttribute : Attribute
		{
			public string BusId { get; set; } = string.Empty;

			public string Id { get; set; } = string.Empty;
		}

		public static void Initialize()
		{
			bool shouldInitialize;
			lock (SyncRoot)
			{
				shouldInitialize = !_initialized;
				if (shouldInitialize)
				{
					_initialized = true;
				}
			}

			if (!shouldInitialize)
			{
				return;
			}

			AttributeScanner.ScanAndRegister(typeof(ReForge).Assembly);
			GD.Print("[ReForge.EventBus] initialized.");
		}

		public static void Publish<TEventArg>(string id, TEventArg eventArg) where TEventArg : IEventArg
		{
			ValidateRequiredKey(id, nameof(id));
			EnsureInitialized();
			Dispatcher.Publish(id, eventArg);
		}

		public static void RegisterListener<TEventArg>(string eventId, string busId, Action<TEventArg> handler) where TEventArg : IEventArg
		{
			ValidateRequiredKey(eventId, nameof(eventId));
			ValidateRequiredKey(busId, nameof(busId));
			ArgumentNullException.ThrowIfNull(handler);
			EnsureInitialized();

			bool replaced = Registry.Upsert(
				new EventSubscription(
					eventId,
					busId,
					typeof(TEventArg),
					payload => handler((TEventArg)payload!),
					source: "Manual"
				)
			);

			if (replaced)
			{
				GD.Print($"[ReForge.EventBus] Listener replaced. eventId='{eventId}', busId='{busId}'.");
			}
		}

		public static void RegisterListener(string eventId, string busId, Action<IEventArg?> handler)
		{
			ValidateRequiredKey(eventId, nameof(eventId));
			ValidateRequiredKey(busId, nameof(busId));
			ArgumentNullException.ThrowIfNull(handler);
			EnsureInitialized();

			bool replaced = Registry.Upsert(
				new EventSubscription(
					eventId,
					busId,
					parameterType: typeof(IEventArg),
					handler,
					source: "Manual"
				)
			);

			if (replaced)
			{
				GD.Print($"[ReForge.EventBus] Listener replaced. eventId='{eventId}', busId='{busId}'.");
			}
		}

		public static void UnregisterListener(string busId)
		{
			ValidateRequiredKey(busId, nameof(busId));
			EnsureInitialized();

			int removed = Registry.RemoveByBusId(busId);
			if (removed > 0)
			{
				GD.Print($"[ReForge.EventBus] Listener removed. busId='{busId}', count={removed}.");
			}
		}

		public static void RegisterAttributedListeners(params Type[] rootTypes)
		{
			ArgumentNullException.ThrowIfNull(rootTypes);
			EnsureInitialized();

			for (int i = 0; i < rootTypes.Length; i++)
			{
				Type? rootType = rootTypes[i];
				if (rootType == null)
				{
					continue;
				}

				AttributeScanner.ScanAndRegister(rootType);
			}
		}

		public static void RegisterAttributedListeners(Assembly assembly)
		{
			ArgumentNullException.ThrowIfNull(assembly);
			EnsureInitialized();
			AttributeScanner.ScanAndRegister(assembly);
		}

		private static void EnsureInitialized()
		{
			if (_initialized)
			{
				return;
			}

			Initialize();
		}

		private static void ValidateRequiredKey(string value, string paramName)
		{
			ArgumentNullException.ThrowIfNull(value);
			if (string.IsNullOrWhiteSpace(value))
			{
				throw new ArgumentException("Value cannot be empty.", paramName);
			}
		}
	}
}
