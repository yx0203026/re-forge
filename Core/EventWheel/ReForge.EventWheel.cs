#nullable enable

using System;
using System.Collections.Generic;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Models;
using ReForgeFramework.Api.Events;
using ReForgeFramework.EventBus;
using ReForgeFramework.ModLoading;

public static partial class ReForge
{
	public static class EventWheel
	{
		private static readonly object SyncRoot = new();
		private static readonly HashSet<string> ScannedAssemblies = new(StringComparer.Ordinal);

		private static bool _initialized;
		private static bool _degraded;
		private static string _lastInitializationError = string.Empty;
		private static ReForgeFramework.EventWheel.EventDefinitionRegistry? _registry;
		private static ReForgeFramework.EventWheel.EventMutationPlanner? _planner;
		private static ReForgeFramework.EventWheel.EventMutationExecutor? _executor;
		private static ReForgeFramework.EventWheel.EventWheelDiagnostics? _diagnostics;
		private static readonly Dictionary<string, Func<EventModel, IEventOptionDefinition, EventOption>> OptionFactories = new(StringComparer.Ordinal);

		public static bool IsInitialized => _initialized;

		public static bool IsDegraded => _degraded;

		public static string LastInitializationError => _lastInitializationError;

		public static void Initialize()
		{
			lock (SyncRoot)
			{
				if (_initialized)
				{
					return;
				}

				_initialized = true;
				_degraded = false;
				_lastInitializationError = string.Empty;
			}

			PublishLifecycle(
				EventWheelLifecycleEventIds.InitializeStarted,
				success: true,
				message: "EventWheel initialization started.",
				exceptionSummary: null,
				registeredDefinitions: 0,
				registeredMutationRules: 0,
				scannedAssemblies: 0);

			try
			{
				ReForgeFramework.EventWheel.EventWheelDiagnostics diagnostics = new(maxDiagnostics: 512);
				ReForgeFramework.EventWheel.EventDefinitionRegistry registry = new(diagnostics: diagnostics);
				ReForgeFramework.EventWheel.EventMutationPlanner planner = new(diagnostics: diagnostics);
				ReForgeFramework.EventWheel.EventMutationExecutor executor = new(diagnostics: diagnostics);

				int registeredDefinitions = 0;
				int registeredMutationRules = 0;
				int scannedAssemblies = 0;

				ScanRegistrationResult coreScan = ScanAndRegisterAssembly(
					assembly: typeof(ReForge).Assembly,
					registry: registry,
					sourceModId: "reforge.core");
				registeredDefinitions += coreScan.RegisteredDefinitions;
				registeredMutationRules += coreScan.RegisteredMutationRules;
				if (!coreScan.Skipped)
				{
					scannedAssemblies++;
				}

				IReadOnlyList<ReForgeModContext> loadedMods = ReForgeModManager.GetLoadedMods();
				for (int i = 0; i < loadedMods.Count; i++)
				{
					ReForgeModContext modContext = loadedMods[i];
					if (modContext.Assembly == null)
					{
						continue;
					}

					ScanRegistrationResult scan = ScanAndRegisterAssembly(
						assembly: modContext.Assembly,
						registry: registry,
						sourceModId: modContext.ModId);

					registeredDefinitions += scan.RegisteredDefinitions;
					registeredMutationRules += scan.RegisteredMutationRules;
					if (!scan.Skipped)
					{
						scannedAssemblies++;
					}
				}

				lock (SyncRoot)
				{
					_registry = registry;
					_planner = planner;
					_executor = executor;
					_diagnostics = diagnostics;
				}

				GD.Print(
					$"[ReForge.EventWheel] initialized. assemblies={scannedAssemblies}, definitions={registeredDefinitions}, rules={registeredMutationRules}.");

				PublishLifecycle(
					EventWheelLifecycleEventIds.InitializeCompleted,
					success: true,
					message: "EventWheel initialization completed.",
					exceptionSummary: null,
					registeredDefinitions: registeredDefinitions,
					registeredMutationRules: registeredMutationRules,
					scannedAssemblies: scannedAssemblies);
			}
			catch (Exception ex)
			{
				lock (SyncRoot)
				{
					_degraded = true;
					_lastInitializationError = ex.ToString();
					_registry = null;
					_planner = null;
					_executor = null;
					_diagnostics = null;
				}

				string errorMessage = $"EventWheel initialization degraded. {ex.GetType().Name}: {ex.Message}";
				GD.PrintErr($"[ReForge.EventWheel] {errorMessage}");

				PublishLifecycle(
					EventWheelLifecycleEventIds.InitializeDegraded,
					success: false,
					message: errorMessage,
					exceptionSummary: ex.ToString(),
					registeredDefinitions: 0,
					registeredMutationRules: 0,
					scannedAssemblies: 0);
			}
		}

		public static EventRegistrationResult RegisterDefinition(IEventDefinition? definition)
		{
			EnsureInitialized();
			if (!TryGetRuntime(out ReForgeFramework.EventWheel.EventDefinitionRegistry? registry, out string unavailableReason))
			{
				return new EventRegistrationResult(
					Success: false,
					EventId: definition?.EventId?.Trim() ?? string.Empty,
					SourceModId: definition?.SourceModId?.Trim() ?? string.Empty,
					Replaced: false,
					Message: unavailableReason);
			}

			return registry!.RegisterDefinition(definition);
		}

		public static EventWheelResult RegisterMutationRule(IEventMutationRule? rule)
		{
			EnsureInitialized();
			if (!TryGetRuntime(out ReForgeFramework.EventWheel.EventDefinitionRegistry? registry, out string unavailableReason))
			{
				return new EventWheelResult(
					Success: false,
					Code: "eventwheel.unavailable",
					Message: unavailableReason,
					EventId: rule?.EventId?.Trim() ?? string.Empty,
					SourceModId: rule?.SourceModId?.Trim() ?? string.Empty,
					Details: null);
			}

			return registry!.RegisterMutationRule(rule);
		}

		public static bool TryGetDefinition(string eventId, EventModel? eventModel, out IEventDefinition? definition)
		{
			EnsureInitialized();
			definition = null;
			if (!TryGetRuntime(out ReForgeFramework.EventWheel.EventDefinitionRegistry? registry, out string unavailableReason))
			{
				GD.PrintErr($"[ReForge.EventWheel] TryGetDefinition unavailable. reason='{unavailableReason}'.");
				return false;
			}

			return registry!.TryGetDefinition(eventId, eventModel, out definition);
		}

		public static IReadOnlyList<EventWheelDiagnosticEvent> QueryDiagnostics(EventWheelDiagnosticQuery? query = null)
		{
			if (!TryGetDiagnostics(out ReForgeFramework.EventWheel.EventWheelDiagnostics? diagnostics))
			{
				return Array.Empty<EventWheelDiagnosticEvent>();
			}

			return diagnostics!.Query(query);
		}

		public static bool RegisterOptionFactory(string actionKey, Func<EventModel, IEventOptionDefinition, EventOption> factory)
		{
			EnsureInitialized();
			if (!TryNormalizeRequiredKey(actionKey, out string normalizedActionKey))
			{
				return false;
			}

			ArgumentNullException.ThrowIfNull(factory);
			lock (SyncRoot)
			{
				OptionFactories[normalizedActionKey] = factory;
				return true;
			}
		}

		internal static bool TryCreateOption(EventModel model, IEventOptionDefinition definition, out EventOption? option)
		{
			ArgumentNullException.ThrowIfNull(model);
			ArgumentNullException.ThrowIfNull(definition);

			string actionKey = definition.ActionKey?.Trim() ?? string.Empty;
			if (actionKey.Length == 0)
			{
				option = null;
				return false;
			}

			Func<EventModel, IEventOptionDefinition, EventOption>? factory;
			lock (SyncRoot)
			{
				OptionFactories.TryGetValue(actionKey, out factory);
			}

			if (factory == null)
			{
				option = null;
				return false;
			}

			option = factory(model, definition);
			return option != null;
		}

		internal static bool TryGetRuntime(
			out ReForgeFramework.EventWheel.EventDefinitionRegistry? registry,
			out ReForgeFramework.EventWheel.EventMutationPlanner? planner,
			out ReForgeFramework.EventWheel.EventMutationExecutor? executor,
			out ReForgeFramework.EventWheel.EventWheelDiagnostics? diagnostics)
		{
			EnsureInitialized();
			lock (SyncRoot)
			{
				registry = _registry;
				planner = _planner;
				executor = _executor;
				diagnostics = _diagnostics;
				return !_degraded
					&& registry != null
					&& planner != null
					&& executor != null
					&& diagnostics != null;
			}
		}

		private static void EnsureInitialized()
		{
			if (_initialized)
			{
				return;
			}

			Initialize();
		}

		private static bool TryGetRuntime(
			out ReForgeFramework.EventWheel.EventDefinitionRegistry? registry,
			out string reason)
		{
			lock (SyncRoot)
			{
				registry = _registry;
				if (!_initialized)
				{
					reason = "EventWheel not initialized.";
					return false;
				}

				if (_degraded)
				{
					reason = "EventWheel is degraded and disabled."
						+ (string.IsNullOrWhiteSpace(_lastInitializationError)
							? string.Empty
							: $" lastError='{_lastInitializationError}'.");
					return false;
				}

				if (registry == null)
				{
					reason = "EventWheel runtime registry is unavailable.";
					return false;
				}

				reason = string.Empty;
				return true;
			}
		}

		private static bool TryGetDiagnostics(out ReForgeFramework.EventWheel.EventWheelDiagnostics? diagnostics)
		{
			EnsureInitialized();
			lock (SyncRoot)
			{
				diagnostics = _diagnostics;
				return !_degraded && diagnostics != null;
			}
		}

		private static ScanRegistrationResult ScanAndRegisterAssembly(
			Assembly assembly,
			ReForgeFramework.EventWheel.EventDefinitionRegistry registry,
			string sourceModId)
		{
			if (!TryMarkAssemblyScanned(assembly))
			{
				return new ScanRegistrationResult(0, 0, Skipped: true);
			}

			int registeredDefinitions = 0;
			int registeredMutationRules = 0;

			Type[] loadableTypes = GetLoadableTypes(assembly);
			for (int i = 0; i < loadableTypes.Length; i++)
			{
				Type type = loadableTypes[i];
				if (!IsAutoRegistrableType(type))
				{
					continue;
				}

				object? instance;
				try
				{
					instance = Activator.CreateInstance(type);
				}
				catch (Exception ex)
				{
					GD.PrintErr($"[ReForge.EventWheel] Failed to instantiate auto-registrable type '{type.FullName}' from mod '{sourceModId}'. {ex.GetType().Name}: {ex.Message}");
					continue;
				}

				if (instance is IEventDefinition definition)
				{
					EventRegistrationResult result = registry.RegisterDefinition(definition);
					if (result.Success)
					{
						registeredDefinitions++;
					}
				}

				if (instance is IEventMutationRule rule)
				{
					EventWheelResult result = registry.RegisterMutationRule(rule);
					if (result.Success)
					{
						registeredMutationRules++;
					}
				}
			}

			PublishLifecycle(
				EventWheelLifecycleEventIds.ScanCompleted,
				success: true,
				message: $"Scan completed for assembly '{assembly.GetName().Name}'.",
				exceptionSummary: null,
				registeredDefinitions: registeredDefinitions,
				registeredMutationRules: registeredMutationRules,
				scannedAssemblies: 1);

			return new ScanRegistrationResult(registeredDefinitions, registeredMutationRules, Skipped: false);
		}

		private static bool TryMarkAssemblyScanned(Assembly assembly)
		{
			ArgumentNullException.ThrowIfNull(assembly);

			string assemblyKey = assembly.FullName ?? assembly.GetName().Name ?? assembly.ToString();
			lock (SyncRoot)
			{
				return ScannedAssemblies.Add(assemblyKey);
			}
		}

		private static Type[] GetLoadableTypes(Assembly assembly)
		{
			try
			{
				return assembly.GetTypes();
			}
			catch (ReflectionTypeLoadException ex)
			{
				List<Type> loadable = new(ex.Types.Length);
				for (int i = 0; i < ex.Types.Length; i++)
				{
					Type? type = ex.Types[i];
					if (type != null)
					{
						loadable.Add(type);
					}
				}

				return loadable.ToArray();
			}
		}

		private static bool IsAutoRegistrableType(Type type)
		{
			if (type.IsAbstract || type.IsInterface || type.ContainsGenericParameters)
			{
				return false;
			}

			if (!typeof(IEventDefinition).IsAssignableFrom(type)
				&& !typeof(IEventMutationRule).IsAssignableFrom(type))
			{
				return false;
			}

			return type.GetConstructor(Type.EmptyTypes) != null;
		}

		private static bool TryNormalizeRequiredKey(string value, out string normalized)
		{
			normalized = value?.Trim() ?? string.Empty;
			return normalized.Length > 0;
		}

		private static void PublishLifecycle(
			string eventId,
			bool success,
			string message,
			string? exceptionSummary,
			int registeredDefinitions,
			int registeredMutationRules,
			int scannedAssemblies)
		{
			try
			{
				ReForge.EventBus.Publish(
					eventId,
					new EventWheelLifecycleEvent(
						TimestampUtc: DateTimeOffset.UtcNow,
						Success: success,
						Message: message,
						ExceptionSummary: exceptionSummary,
						RegisteredDefinitions: registeredDefinitions,
						RegisteredMutationRules: registeredMutationRules,
						ScannedAssemblies: scannedAssemblies));
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[ReForge.EventWheel] Failed to publish lifecycle event '{eventId}'. {ex.GetType().Name}: {ex.Message}");
			}
		}

		private readonly record struct ScanRegistrationResult(
			int RegisteredDefinitions,
			int RegisteredMutationRules,
			bool Skipped);

		private readonly record struct EventWheelLifecycleEvent(
			DateTimeOffset TimestampUtc,
			bool Success,
			string Message,
			string? ExceptionSummary,
			int RegisteredDefinitions,
			int RegisteredMutationRules,
			int ScannedAssemblies) : IEventArg;

		private static class EventWheelLifecycleEventIds
		{
			public const string InitializeStarted = "reforge.eventwheel.lifecycle.initialize.started";
			public const string InitializeCompleted = "reforge.eventwheel.lifecycle.initialize.completed";
			public const string InitializeDegraded = "reforge.eventwheel.lifecycle.initialize.degraded";
			public const string ScanCompleted = "reforge.eventwheel.lifecycle.scan.completed";
		}
	}
}