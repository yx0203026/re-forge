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
	/// <summary>
	/// EventWheel 事件扩展轮盘入口。
	/// 负责初始化运行时组件、注册事件定义/变更规则、管理诊断查询与选项工厂。
	/// </summary>
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

		/// <summary>
		/// 获取 EventWheel 是否已完成初始化标记。
		/// </summary>
		public static bool IsInitialized => _initialized;

		/// <summary>
		/// 获取 EventWheel 是否处于降级状态。
		/// 降级状态下不会执行运行时事件变更逻辑。
		/// </summary>
		public static bool IsDegraded => _degraded;

		/// <summary>
		/// 获取最近一次初始化异常的详细文本。
		/// 当无异常时返回空字符串。
		/// </summary>
		public static string LastInitializationError => _lastInitializationError;

		/// <summary>
		/// 初始化 EventWheel 运行时。
		/// 会扫描核心程序集与已加载模组程序集，自动注册 IEventDefinition 与 IEventMutationRule 实现。
		/// </summary>
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

		/// <summary>
		/// 注册事件定义。
		/// </summary>
		/// <param name="definition">事件定义对象；为空时返回失败结果。</param>
		/// <returns>注册结果，包含成功状态、事件标识与诊断消息。</returns>
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

		/// <summary>
		/// 注册事件选项变更规则。
		/// </summary>
		/// <param name="rule">变更规则对象；为空时返回失败结果。</param>
		/// <returns>注册结果，包含成功状态、错误码与消息。</returns>
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

		/// <summary>
		/// 尝试获取指定事件定义。
		/// 当存在池化定义时，会结合 eventModel 上下文进行稳定选择。
		/// </summary>
		/// <param name="eventId">事件标识。</param>
		/// <param name="eventModel">事件模型上下文，可空。</param>
		/// <param name="definition">输出匹配到的定义。</param>
		/// <returns>找到定义则返回 true。</returns>
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

		/// <summary>
		/// 查询 EventWheel 诊断事件。
		/// </summary>
		/// <param name="query">可选查询条件，不传则返回默认窗口内诊断。</param>
		/// <returns>按内部查询规则过滤后的诊断快照。</returns>
		public static IReadOnlyList<EventWheelDiagnosticEvent> QueryDiagnostics(EventWheelDiagnosticQuery? query = null)
		{
			if (!TryGetDiagnostics(out ReForgeFramework.EventWheel.EventWheelDiagnostics? diagnostics))
			{
				return Array.Empty<EventWheelDiagnosticEvent>();
			}

			return diagnostics!.Query(query);
		}

		/// <summary>
		/// 注册选项构建工厂。
		/// 用于将 IEventOptionDefinition.ActionKey 映射为自定义 EventOption 构造逻辑。
		/// </summary>
		/// <param name="actionKey">动作键，区分不同工厂。</param>
		/// <param name="factory">工厂委托。</param>
		/// <returns>注册成功返回 true；actionKey 非法返回 false。</returns>
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

		/// <summary>
		/// 尝试按 ActionKey 创建运行时事件选项。
		/// </summary>
		/// <param name="model">事件模型。</param>
		/// <param name="definition">选项定义。</param>
		/// <param name="option">输出创建结果。</param>
		/// <returns>创建成功返回 true。</returns>
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

		/// <summary>
		/// 获取 EventWheel 全量运行时依赖。
		/// </summary>
		/// <param name="registry">定义注册表。</param>
		/// <param name="planner">变更规划器。</param>
		/// <param name="executor">变更执行器。</param>
		/// <param name="diagnostics">诊断组件。</param>
		/// <returns>全部组件可用且未降级时返回 true。</returns>
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

		/// <summary>
		/// 确保 EventWheel 已初始化。
		/// 未初始化时会触发一次懒初始化。
		/// </summary>
		private static void EnsureInitialized()
		{
			if (_initialized)
			{
				return;
			}

			Initialize();
		}

		/// <summary>
		/// 获取定义注册表运行时实例。
		/// </summary>
		/// <param name="registry">输出注册表。</param>
		/// <param name="reason">不可用时的原因说明。</param>
		/// <returns>可用返回 true；否则返回 false 并输出 reason。</returns>
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

		/// <summary>
		/// 获取诊断系统实例。
		/// </summary>
		/// <param name="diagnostics">输出诊断实例。</param>
		/// <returns>可用返回 true。</returns>
		private static bool TryGetDiagnostics(out ReForgeFramework.EventWheel.EventWheelDiagnostics? diagnostics)
		{
			EnsureInitialized();
			lock (SyncRoot)
			{
				diagnostics = _diagnostics;
				return !_degraded && diagnostics != null;
			}
		}

		/// <summary>
		/// 扫描程序集并自动注册事件定义与变更规则。
		/// </summary>
		/// <param name="assembly">待扫描程序集。</param>
		/// <param name="registry">注册表实例。</param>
		/// <param name="sourceModId">来源模组标识。</param>
		/// <returns>扫描与注册统计结果。</returns>
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

		/// <summary>
		/// 标记程序集已扫描，避免重复扫描。
		/// </summary>
		private static bool TryMarkAssemblyScanned(Assembly assembly)
		{
			ArgumentNullException.ThrowIfNull(assembly);

			string assemblyKey = assembly.FullName ?? assembly.GetName().Name ?? assembly.ToString();
			lock (SyncRoot)
			{
				return ScannedAssemblies.Add(assemblyKey);
			}
		}

		/// <summary>
		/// 安全获取程序集可加载类型集合。
		/// 遇到 ReflectionTypeLoadException 时会返回可成功解析的子集。
		/// </summary>
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

		/// <summary>
		/// 判定类型是否可自动注册。
		/// 仅接受具备无参构造、且实现定义或规则接口的具体类型。
		/// </summary>
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

		/// <summary>
		/// 归一化并校验必填键值。
		/// </summary>
		private static bool TryNormalizeRequiredKey(string value, out string normalized)
		{
			normalized = value?.Trim() ?? string.Empty;
			return normalized.Length > 0;
		}

		/// <summary>
		/// 发布 EventWheel 生命周期事件到事件总线。
		/// </summary>
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

		/// <summary>
		/// 程序集扫描与注册统计。
		/// </summary>
		private readonly record struct ScanRegistrationResult(
			int RegisteredDefinitions,
			int RegisteredMutationRules,
			bool Skipped);

		/// <summary>
		/// EventWheel 生命周期诊断事件载荷。
		/// </summary>
		private readonly record struct EventWheelLifecycleEvent(
			DateTimeOffset TimestampUtc,
			bool Success,
			string Message,
			string? ExceptionSummary,
			int RegisteredDefinitions,
			int RegisteredMutationRules,
			int ScannedAssemblies) : IEventArg;

		/// <summary>
		/// EventWheel 生命周期事件标识常量。
		/// </summary>
		private static class EventWheelLifecycleEventIds
		{
			public const string InitializeStarted = "reforge.eventwheel.lifecycle.initialize.started";
			public const string InitializeCompleted = "reforge.eventwheel.lifecycle.initialize.completed";
			public const string InitializeDegraded = "reforge.eventwheel.lifecycle.initialize.degraded";
			public const string ScanCompleted = "reforge.eventwheel.lifecycle.scan.completed";
		}
	}
}