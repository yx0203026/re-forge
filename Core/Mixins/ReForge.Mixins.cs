#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;
using Godot;
using HarmonyLib;
using ReForgeFramework.Mixins.Runtime;

public static partial class ReForge
{
	/// <summary>
	/// ReForge Mixin 系统的主入口。提供 Mixin 注册、卸载和诊断查询 API。
	/// </summary>
	/// <remarks>
	/// Mixin 系统是一个轻量级的方法补丁框架，构建在 Harmony 之上。
	/// 典型工作流程：
	/// 1. 在模组初始化时调用 <see cref="Register(MixinRegistrationOptions)"/>
	/// 2. 系统自动扫描程序集中被 <see cref="global::ReForge.MixinAttribute"/> 标记的类型
	/// 3. 验证 Mixin 定义并将其注入应用到目标类型
	/// 4. 模组卸载时可调用 <see cref="UnregisterAll"/> 移除所有补丁
	/// 
	/// 高级特性：
	/// • 冲突检测与解决
	/// • 严格模式：扫描错误导致注册中止
	/// • Shadow 字段映射：通过 <see cref="global::ReForge.ShadowAttribute"/> 访问目标类型私有字段
	/// • 诊断支持：<see cref="GetStatus"/>、<see cref="GetDiagnosticsSnapshot"/>
	/// </remarks>
	public static partial class Mixins
	{
		private static readonly object SyncRoot = new();
		private static readonly Dictionary<string, MixinRegistrationResult> Registrations = new(StringComparer.Ordinal);
		private static readonly MixinLifecycleManager LifecycleManager = new();
		private static readonly MixinDiagnostics Diagnostics = new();

		private static bool _warningPrinted;

		/// <summary>
		/// 使用指定的配置选项注册此模组的 Mixin。
		/// </summary>
		/// <remarks>
		/// 此方法执行以下步骤：
		/// 1. 验证配置选项的有效性
		/// 2. 扫描指定程序集中的 Mixin 定义
		/// 3. 验证每个 Mixin 与其注入的合法性
		/// 4. 应用为生成的 Harmony 补丁
		/// 5. 返回注册结果与统计信息
		/// 
		/// 在严格模式下，任何扫描或验证错误都会导致异常。
		/// 在非严格模式下，单个 Mixin 的错误不会中止整个注册过程，但会记录在诊断信息中。
		/// </remarks>
		/// <param name="options">包含程序集、modId、Harmony 实例等的注册配置。</param>
		/// <returns>包含注册结果与统计信息的 <see cref="MixinRegistrationResult"/> 对象。</returns>
		/// <exception cref="ArgumentNullException">当 options 为 null 时。</exception>
		/// <exception cref="InvalidOperationException">在严格模式下注册失败时。</exception>
		public static MixinRegistrationResult Register(MixinRegistrationOptions options)
		{
			ArgumentNullException.ThrowIfNull(options);
			ValidateRegistrationSource(options.Source);
			MixinLifecycleInstallResult installResult = LifecycleManager.InitializeAndInstall(options);
			MixinRegistrationResult result = BuildRegistrationResultFromInstall(options, installResult);

			lock (SyncRoot)
			{
				Registrations[options.ModId] = result;
				_warningPrinted = false;
			}

			if (result.State == MixinRegistrationState.Registered && result.Summary.Failed == 0)
			{
				GD.Print(
					$"[ReForge.Mixins] Explicit registration completed. modId='{options.ModId}', strictMode={options.StrictMode}, installed={result.Summary.Installed}, failed={result.Summary.Failed}, skipped={result.Summary.Skipped}."
				);
			}
			else
			{
				GD.PrintErr(
					$"[ReForge.Mixins] Explicit registration finished with issues. modId='{options.ModId}', strictMode={options.StrictMode}, installed={result.Summary.Installed}, failed={result.Summary.Failed}, skipped={result.Summary.Skipped}, state='{result.State}', message='{result.Message}'."
				);
			}

			if (installResult.State == MixinLifecycleState.Failed || result.Summary.Failed > 0)
			{
				throw new InvalidOperationException(
					$"Mixin registration failed. modId='{options.ModId}', strictMode={options.StrictMode}, installed={result.Summary.Installed}, failed={result.Summary.Failed}, skipped={result.Summary.Skipped}, state='{installResult.State}', message='{installResult.Message}'."
				);
			}

			return result;
		}

		/// <summary>
		/// 注册此模组的 Mixin（便捷重载）。
		/// </summary>
		/// <remarks>
		/// 这是 <see cref="Register(MixinRegistrationOptions)"/> 的便捷重载。
		/// 内部创建标准配置并调用主方法。
		/// </remarks>
		/// <param name="assembly">包含 Mixin 定义的程序集。</param>
		/// <param name="modId">模组的唯一标识符。</param>
		/// <param name="harmony">用于应用补丁的 Harmony 实例。</param>
		/// <param name="strictMode">是否启用严格模式（默认 true）。</param>
		/// <returns>注册结果对象。</returns>
		/// <exception cref="ArgumentNullException">当任何参数为 null 时。</exception>
		public static MixinRegistrationResult Register(Assembly assembly, string modId, Harmony harmony, bool strictMode = true)
		{
			MixinRegistrationOptions options = MixinRegistrationOptions.CreateMainClassOptions(
				assembly,
				modId,
				harmony,
				strictMode
			);

			return Register(options);
		}

		/// <summary>
		/// 注册此模组的 Mixin（隐式 Harmony 重载）。
		/// </summary>
		/// <remarks>
		/// 此重载会在内部创建 Harmony 实例，开发者无需显式引用 Harmony。
		/// </remarks>
		public static MixinRegistrationResult Register(Assembly assembly, string modId, bool strictMode = true)
		{
			Harmony harmony = new(modId);
			return Register(assembly, modId, harmony, strictMode);
		}

		/// <summary>
		/// 注册此模组的 Mixin（自动推断 modId + 隐式 Harmony）。
		/// </summary>
		public static MixinRegistrationResult Register(Assembly assembly, bool strictMode = true)
		{
			ArgumentNullException.ThrowIfNull(assembly);
			string inferredModId = InferModIdFromAssembly(assembly);
			return Register(assembly, inferredModId, strictMode);
		}

		/// <summary>
		/// 非抛出注册：失败时返回 false 并输出错误日志。
		/// </summary>
		public static bool TryRegister(Assembly assembly, string modId, bool strictMode, out MixinRegistrationResult result)
		{
			try
			{
				result = Register(assembly, modId, strictMode);
				return result.State == MixinRegistrationState.Registered && result.Summary.Failed == 0;
			}
			catch (Exception ex)
			{
				result = new MixinRegistrationResult(
					modId,
					MixinRegistrationSource.MainClassExplicit,
					MixinRegistrationState.NotRegistered,
					strictMode,
					new MixinRegistrationSummary(0, 1, 0),
					ex.Message,
					DateTimeOffset.UtcNow
				);
				GD.PrintErr($"[ReForge.Mixins] TryRegister failed. modId='{modId}', strictMode={strictMode}. {ex}");
				return false;
			}
		}

		/// <summary>
		/// 非抛出注册：自动推断 modId。
		/// </summary>
		public static bool TryRegister(Assembly assembly, bool strictMode, out MixinRegistrationResult result)
		{
			ArgumentNullException.ThrowIfNull(assembly);
			return TryRegister(assembly, InferModIdFromAssembly(assembly), strictMode, out result);
		}

		/// <summary>
		/// 非抛出注册（隐式结果）：内部自动完成日志记录与异常隔离。
		/// </summary>
		public static bool TryRegister(Assembly assembly, string modId, bool strictMode = true)
		{
			return TryRegister(assembly, modId, strictMode, out _);
		}

		/// <summary>
		/// 非抛出注册（自动推断 modId + 隐式结果）。
		/// </summary>
		public static bool TryRegister(Assembly assembly, bool strictMode = true)
		{
			return TryRegister(assembly, strictMode, out _);
		}

		/// <summary>
		/// 卸载指定模组的所有 Mixin 补丁。
		/// </summary>
		/// <remarks>
		/// 此方法移除已应用的 Harmony 补丁，允许模组的代码恢复到原始行为。
		/// 如果模组未曾注册，此方法会记录信息但不会报错。
		/// </remarks>
		/// <param name="modId">要卸载的模组 ID。</param>
		/// <returns>包含卸载结果的 <see cref="MixinUnregisterResult"/> 对象。</returns>
		/// <exception cref="ArgumentNullException">当 modId 为 null。</exception>
		/// <exception cref="ArgumentException">当 modId 为空或仅为空白。</exception>
		public static MixinUnregisterResult UnregisterAll(string modId)
		{
			ValidateRequiredKey(modId, nameof(modId));
			MixinLifecycleUnloadResult unloadResult = LifecycleManager.Unload(modId);
			MixinModLifecycleStatus? status = LifecycleManager.GetStatus(modId);

			MixinRegistrationSummary summary = status == null
				? new MixinRegistrationSummary(0, 0, 0)
				: new MixinRegistrationSummary(status.Counters.Installed, status.Counters.Failed, status.Counters.Skipped);

			MixinRegistrationResult registrationResult = new(
				modId,
				MixinRegistrationSource.MainClassExplicit,
				MapLifecycleStateToRegistrationState(unloadResult.State),
				status?.StrictMode ?? false,
				summary,
				unloadResult.Message,
				unloadResult.TimestampUtc
			);

			lock (SyncRoot)
			{
				Registrations[modId] = registrationResult;
				_warningPrinted = false;
			}

			bool removed = unloadResult.RemovedCount > 0 || unloadResult.State == MixinLifecycleState.Unloaded;
			if (removed)
			{
				GD.Print($"[ReForge.Mixins] Unregistered mixins. modId='{modId}', removed={unloadResult.RemovedCount}, unpatchFailures={unloadResult.UnpatchFailures}.");
			}
			else
			{
				GD.Print($"[ReForge.Mixins] Unregister skipped. modId='{modId}', message='{unloadResult.Message}'.");
			}

			return new MixinUnregisterResult(
				modId,
				Removed: removed,
				RemovedInstalledCount: unloadResult.RemovedCount,
				RemovedFailedCount: unloadResult.UnpatchFailures,
				unloadResult.Message,
				unloadResult.TimestampUtc
			);
		}

		/// <summary>
		/// 获取所有已注册模组的当前状态快照。
		/// </summary>
		/// <remarks>
		/// 快照包含所有已显式注册和生命周期追踪的模组的状态。
		/// 若快照为空（未作任何显式注册），系统会输出一条警告消息。
		/// </remarks>
		/// <returns>包含所有模组状态的 <see cref="MixinStatusSnapshot"/> 对象。</returns>
		public static MixinStatusSnapshot GetStatus()
		{
			bool shouldWarn = false;
			IReadOnlyDictionary<string, MixinRegistrationResult> snapshot;
			Dictionary<string, MixinRegistrationResult> combined = new(StringComparer.Ordinal);

			lock (SyncRoot)
			{
				foreach (KeyValuePair<string, MixinRegistrationResult> pair in Registrations)
				{
					combined[pair.Key] = pair.Value;
				}
			}

			MixinLifecycleSnapshot lifecycleSnapshot = LifecycleManager.Snapshot();
			foreach (KeyValuePair<string, MixinModLifecycleStatus> pair in lifecycleSnapshot.Mods)
			{
				MixinRegistrationSource source = MixinRegistrationSource.MainClassExplicit;
				if (combined.TryGetValue(pair.Key, out MixinRegistrationResult? existing))
				{
					source = existing.Source;
				}

				combined[pair.Key] = BuildRegistrationResultFromLifecycleStatus(pair.Key, source, pair.Value);
			}

			lock (SyncRoot)
			{
				snapshot = new ReadOnlyDictionary<string, MixinRegistrationResult>(combined);
				if (snapshot.Count == 0 && !_warningPrinted)
				{
					_warningPrinted = true;
					shouldWarn = true;
				}
			}

			if (shouldWarn)
			{
				GD.Print("[ReForge.Mixins] Warning: no explicit registration found. Please call ReForge.Mixins.Register(...) in mod main initializer.");
			}

			return new MixinStatusSnapshot(
				isExplicitlyRegistered: snapshot.Count > 0,
				registeredModCount: snapshot.Count,
				registrations: snapshot
			);
		}

		/// <summary>
		/// 获取详细的诊断信息快照（包括所有补丁应用记录）。
		/// </summary>
		/// <remarks>
		/// 此快照包含注册信息、生命周期状态、已应用补丁的完整列表等详细诊断数据。
		/// 适用于深度调试与问题诊断。
		/// </remarks>
		/// <returns>包含完整诊断信息的 <see cref="MixinDiagnosticsSnapshot"/> 对象。</returns>
		public static MixinDiagnosticsSnapshot GetDiagnosticsSnapshot()
		{
			MixinStatusSnapshot registrationStatus = GetStatus();
			MixinLifecycleSnapshot lifecycleSnapshot = LifecycleManager.Snapshot();
			IReadOnlyList<MixinAppliedEntry> appliedEntries = LifecycleManager.GetAppliedEntries();
			return Diagnostics.BuildSnapshot(
				registrationStatus,
				lifecycleSnapshot,
				appliedEntries,
				LifecycleManager.GetReflectionRuntimeSnapshot()
			);
		}

		/// <summary>
		/// 获取诊断信息的 JSON 表示。
		/// </summary>
		/// <remarks>
		/// 此方法将诊断快照序列化为 JSON 字符串，便于日志记录或远程分析。
		/// </remarks>
		/// <param name="indented">是否使用缩进格式化输出（默认 true）。</param>
		/// <returns>JSON 格式的诊断信息字符串。</returns>
		public static string GetDiagnosticsJson(bool indented = true)
		{
			MixinDiagnosticsSnapshot snapshot = GetDiagnosticsSnapshot();
			return Diagnostics.ToJson(snapshot, indented);
		}

		private static void ValidateRegistrationSource(MixinRegistrationSource source)
		{
			if (source != MixinRegistrationSource.MainClassExplicit)
			{
				throw new ArgumentException("Only main-class explicit registration is allowed.", nameof(source));
			}
		}

		private static string InferModIdFromAssembly(Assembly assembly)
		{
			string? assemblyName = assembly.GetName().Name;
			if (!string.IsNullOrWhiteSpace(assemblyName))
			{
				return assemblyName;
			}

			return "reforge.mod.unknown";
		}

		private static MixinRegistrationResult BuildRegistrationResultFromInstall(
			MixinRegistrationOptions options,
			MixinLifecycleInstallResult installResult)
		{
			MixinRegistrationSummary summary = new(
				installResult.Counters.Installed,
				installResult.Counters.Failed,
				installResult.Counters.Skipped
			);

			return new MixinRegistrationResult(
				options.ModId,
				options.Source,
				MapLifecycleStateToRegistrationState(installResult.State),
				options.StrictMode,
				summary,
				installResult.Message,
				installResult.TimestampUtc
			);
		}

		private static MixinRegistrationResult BuildRegistrationResultFromLifecycleStatus(
			string modId,
			MixinRegistrationSource source,
			MixinModLifecycleStatus lifecycleStatus)
		{
			MixinRegistrationSummary summary = new(
				lifecycleStatus.Counters.Installed,
				lifecycleStatus.Counters.Failed,
				lifecycleStatus.Counters.Skipped
			);

			return new MixinRegistrationResult(
				modId,
				source,
				MapLifecycleStateToRegistrationState(lifecycleStatus.State),
				lifecycleStatus.StrictMode,
				summary,
				lifecycleStatus.Message,
				lifecycleStatus.UpdatedAtUtc
			);
		}

		private static MixinRegistrationState MapLifecycleStateToRegistrationState(MixinLifecycleState state)
		{
			return state switch
			{
				MixinLifecycleState.Active => MixinRegistrationState.Registered,
				MixinLifecycleState.Installing => MixinRegistrationState.Registered,
				MixinLifecycleState.Unloading => MixinRegistrationState.Unregistered,
				MixinLifecycleState.Unloaded => MixinRegistrationState.Unregistered,
				_ => MixinRegistrationState.NotRegistered,
			};
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
