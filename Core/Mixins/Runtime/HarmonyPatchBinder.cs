#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Reflection.Emit;
using Godot;
using HarmonyLib;

namespace ReForgeFramework.Mixins.Runtime;

public sealed record MixinPatchApplyRecord(
	string InjectionDescriptorKey,
	InjectionKind Kind,
	string ConflictKey,
	string TargetTypeName,
	string TargetMethodName,
	bool Success,
	string Message,
	MixinConflictResolution? ConflictResolution,
	MethodBase? PatchedTarget
);

public sealed class MixinPatchBindResult
{
	public MixinPatchBindResult(
		string mixinId,
		int installed,
		int failed,
		int skipped,
		bool abortedByStrictMode,
		IReadOnlyList<MixinPatchApplyRecord> records)
	{
		ArgumentNullException.ThrowIfNull(mixinId);
		ArgumentNullException.ThrowIfNull(records);

		MixinId = mixinId;
		Installed = installed;
		Failed = failed;
		Skipped = skipped;
		AbortedByStrictMode = abortedByStrictMode;
		Records = records;
	}

	public string MixinId { get; }

	public int Installed { get; }

	public int Failed { get; }

	public int Skipped { get; }

	public bool AbortedByStrictMode { get; }

	public IReadOnlyList<MixinPatchApplyRecord> Records { get; }
}

internal sealed class HarmonyPatchBinder
{
	private readonly HarmonyTargetResolver _targetResolver = new();
	private readonly MixinConflictPolicy _conflictPolicy;
	private readonly MixinAppliedRegistry _appliedRegistry;

	public HarmonyPatchBinder(
		MixinConflictPolicy? conflictPolicy = null,
		MixinAppliedRegistry? appliedRegistry = null)
	{
		_conflictPolicy = conflictPolicy ?? new MixinConflictPolicy();
		_appliedRegistry = appliedRegistry ?? new MixinAppliedRegistry();
	}

	public MixinPatchBindResult BindAndApply(MixinDescriptor descriptor, Harmony harmony)
	{
		ArgumentNullException.ThrowIfNull(descriptor);
		ArgumentNullException.ThrowIfNull(harmony);

		int installed = 0;
		int failed = 0;
		int skipped = 0;
		bool abortedByStrictMode = false;
		List<MixinPatchApplyRecord> records = new(descriptor.Injections.Count);
		List<InjectionDescriptor> orderedInjections = new(descriptor.Injections);
		orderedInjections.Sort(static (a, b) => string.CompareOrdinal(a.DescriptorKey, b.DescriptorKey));

		for (int i = 0; i < orderedInjections.Count; i++)
		{
			InjectionDescriptor injection = orderedInjections[i];
			if (!TryMapPatchKind(injection.Kind, out PatchSlot patchSlot))
			{
				skipped++;
				string skipMessage =
					$"Skipped unsupported injection kind. mixin='{descriptor.MixinType.FullName}', kind='{injection.Kind}', targetType='{descriptor.TargetType.FullName}', method='{injection.TargetMethodName}'.";
				GD.Print($"[ReForge.Mixins] {skipMessage}");
				records.Add(new MixinPatchApplyRecord(
					injection.DescriptorKey,
					injection.Kind,
					ConflictKey: string.Empty,
					descriptor.TargetType.FullName ?? descriptor.TargetType.Name,
					injection.TargetMethodName,
					Success: false,
					skipMessage,
					ConflictResolution: MixinConflictResolution.Skip,
					PatchedTarget: null
				));
				continue;
			}

			if (!_targetResolver.TryResolveTargetMethod(descriptor, injection, out MethodBase? targetMethod, out string resolveError))
			{
				// ════════════════════════════════════════════════════════════════════════════════
				// Optional 支持：如果注入标记为可选且目标未找到，跳过而不是失败
				// ════════════════════════════════════════════════════════════════════════════════
				if (injection.Optional)
				{
					skipped++;
					string optionalSkipMessage =
						$"Optional injection skipped (target not found). mixin='{descriptor.MixinType.FullName}', kind='{injection.Kind}', targetType='{descriptor.TargetType.FullName}', method='{injection.TargetMethodName}'.";
					GD.Print($"[ReForge.Mixins] {optionalSkipMessage}");
					records.Add(new MixinPatchApplyRecord(
						injection.DescriptorKey,
						injection.Kind,
						ConflictKey: string.Empty,
						descriptor.TargetType.FullName ?? descriptor.TargetType.Name,
						injection.TargetMethodName,
						Success: false,
						optionalSkipMessage,
						ConflictResolution: MixinConflictResolution.Skip,
						PatchedTarget: null
					));
					continue;
				}

				failed++;
				GD.PrintErr($"[ReForge.Mixins] {resolveError}");
				records.Add(new MixinPatchApplyRecord(
					injection.DescriptorKey,
					injection.Kind,
					ConflictKey: string.Empty,
					descriptor.TargetType.FullName ?? descriptor.TargetType.Name,
					injection.TargetMethodName,
					Success: false,
					resolveError,
					ConflictResolution: MixinConflictResolution.Fail,
					PatchedTarget: null
				));

				if (descriptor.StrictMode)
				{
					abortedByStrictMode = true;
					break;
				}

				continue;
			}

			if (targetMethod == null)
			{
				failed++;
				string nullTargetError =
					$"Target resolution returned null unexpectedly. mixin='{descriptor.MixinType.FullName}', targetType='{descriptor.TargetType.FullName}', method='{injection.TargetMethodName}', descriptorKey='{injection.DescriptorKey}'.";
				GD.PrintErr($"[ReForge.Mixins] {nullTargetError}");
				records.Add(new MixinPatchApplyRecord(
					injection.DescriptorKey,
					injection.Kind,
					ConflictKey: string.Empty,
					descriptor.TargetType.FullName ?? descriptor.TargetType.Name,
					injection.TargetMethodName,
					Success: false,
					nullTargetError,
					ConflictResolution: MixinConflictResolution.Fail,
					PatchedTarget: null
				));

				if (descriptor.StrictMode)
				{
					abortedByStrictMode = true;
					break;
				}

				continue;
			}

			string conflictKey = MixinAppliedRegistry.BuildConflictKey(targetMethod, patchSlot.SlotName);

			if (_appliedRegistry.TryGetByInjectionKey(injection.DescriptorKey, out MixinAppliedEntry? duplicateEntry))
			{
				MixinConflictDecision decision = _conflictPolicy.Evaluate(new MixinConflictContext(
					MixinConflictType.DuplicateInjection,
					descriptor,
					injection,
					duplicateEntry,
					conflictKey
				));

				if (decision.Resolution == MixinConflictResolution.Skip)
				{
					skipped++;
					string message = $"{decision.Reason} Action='skip'.";
					GD.Print($"[ReForge.Mixins] {message}");
					records.Add(new MixinPatchApplyRecord(
						injection.DescriptorKey,
						injection.Kind,
						conflictKey,
						descriptor.TargetType.FullName ?? descriptor.TargetType.Name,
						targetMethod.Name,
						Success: false,
						message,
						ConflictResolution: MixinConflictResolution.Skip,
						PatchedTarget: targetMethod
					));
					continue;
				}

				failed++;
				string failMessage = $"{decision.Reason} Action='fail'.";
				GD.PrintErr($"[ReForge.Mixins] {failMessage}");
				records.Add(new MixinPatchApplyRecord(
					injection.DescriptorKey,
					injection.Kind,
					conflictKey,
					descriptor.TargetType.FullName ?? descriptor.TargetType.Name,
					targetMethod.Name,
					Success: false,
					failMessage,
					ConflictResolution: MixinConflictResolution.Fail,
					PatchedTarget: targetMethod
				));

				if (descriptor.StrictMode)
				{
					abortedByStrictMode = true;
					break;
				}

				continue;
			}

			if (_appliedRegistry.TryGetByConflictKey(conflictKey, out MixinAppliedEntry? conflictEntry)
				&& conflictEntry != null
				&& !string.Equals(conflictEntry.InjectionDescriptorKey, injection.DescriptorKey, StringComparison.Ordinal))
			{
				MixinConflictDecision decision = _conflictPolicy.Evaluate(new MixinConflictContext(
					MixinConflictType.TargetSlotConflict,
					descriptor,
					injection,
					conflictEntry,
					conflictKey
				));

				if (decision.Resolution == MixinConflictResolution.Skip)
				{
					skipped++;
					string message = $"{decision.Reason} Action='skip'.";
					GD.Print($"[ReForge.Mixins] {message}");
					records.Add(new MixinPatchApplyRecord(
						injection.DescriptorKey,
						injection.Kind,
						conflictKey,
						descriptor.TargetType.FullName ?? descriptor.TargetType.Name,
						targetMethod.Name,
						Success: false,
						message,
						ConflictResolution: MixinConflictResolution.Skip,
						PatchedTarget: targetMethod
					));
					continue;
				}

				if (decision.Resolution == MixinConflictResolution.Overwrite)
				{
					try
					{
						harmony.Unpatch(conflictEntry.TargetMethod, conflictEntry.HandlerMethod);
						_appliedRegistry.UnregisterByInjectionKey(conflictEntry.InjectionDescriptorKey);
						GD.Print($"[ReForge.Mixins] {decision.Reason} Action='overwrite'. existingInjectionKey='{conflictEntry.InjectionDescriptorKey}'.");
					}
					catch (Exception exception)
					{
						failed++;
						string overwriteError =
							$"Conflict overwrite failed during unpatch. injectionKey='{injection.DescriptorKey}', existingInjectionKey='{conflictEntry.InjectionDescriptorKey}', targetType='{targetMethod.DeclaringType?.FullName}', method='{targetMethod.Name}'. {exception}";
						GD.PrintErr($"[ReForge.Mixins] {overwriteError}");
						records.Add(new MixinPatchApplyRecord(
							injection.DescriptorKey,
							injection.Kind,
							conflictKey,
							descriptor.TargetType.FullName ?? descriptor.TargetType.Name,
							targetMethod.Name,
							Success: false,
							overwriteError,
							ConflictResolution: MixinConflictResolution.Overwrite,
							PatchedTarget: targetMethod
						));

						if (descriptor.StrictMode)
						{
							abortedByStrictMode = true;
							break;
						}

						continue;
					}
				}
				else
				{
					failed++;
					string message = $"{decision.Reason} Action='fail'.";
					GD.PrintErr($"[ReForge.Mixins] {message}");
					records.Add(new MixinPatchApplyRecord(
						injection.DescriptorKey,
						injection.Kind,
						conflictKey,
						descriptor.TargetType.FullName ?? descriptor.TargetType.Name,
						targetMethod.Name,
						Success: false,
						message,
						ConflictResolution: MixinConflictResolution.Fail,
						PatchedTarget: targetMethod
					));

					if (descriptor.StrictMode)
					{
						abortedByStrictMode = true;
						break;
					}

					continue;
				}
			}

			// ════════════════════════════════════════════════════════════════════════════════
			// 构建 Harmony 补丁方法：根据注入类型使用不同策略
			// ════════════════════════════════════════════════════════════════════════════════
			HarmonyMethod? prefix = null;
			HarmonyMethod? postfix = null;
			HarmonyMethod? transpiler = null;
			HarmonyMethod? finalizer = null;

			if (!TryBuildPatchMethods(injection, targetMethod, out prefix, out postfix, out transpiler, out finalizer, out string buildError))
			{
				failed++;
				GD.PrintErr($"[ReForge.Mixins] {buildError}");
				records.Add(new MixinPatchApplyRecord(
					injection.DescriptorKey,
					injection.Kind,
					conflictKey,
					descriptor.TargetType.FullName ?? descriptor.TargetType.Name,
					targetMethod.Name,
					Success: false,
					buildError,
					ConflictResolution: MixinConflictResolution.Fail,
					PatchedTarget: targetMethod
				));

				if (descriptor.StrictMode)
				{
					abortedByStrictMode = true;
					break;
				}

				continue;
			}

			try
			{
				harmony.Patch(targetMethod, prefix: prefix, postfix: postfix, transpiler: transpiler, finalizer: finalizer);
				installed++;
				string successMessage =
					$"Patch installed. mixin='{descriptor.MixinType.FullName}', kind='{injection.Kind}', targetType='{targetMethod.DeclaringType?.FullName}', method='{targetMethod.Name}'.";
				GD.Print($"[ReForge.Mixins] {successMessage}");
				_appliedRegistry.Register(new MixinAppliedEntry(
					injection.DescriptorKey,
					conflictKey,
					descriptor.MixinId,
					injection.Kind,
					harmony.Id,
					targetMethod,
					injection.HandlerMethod,
					DateTimeOffset.UtcNow
				));

				records.Add(new MixinPatchApplyRecord(
					injection.DescriptorKey,
					injection.Kind,
					conflictKey,
					descriptor.TargetType.FullName ?? descriptor.TargetType.Name,
					targetMethod.Name,
					Success: true,
					successMessage,
					ConflictResolution: null,
					PatchedTarget: targetMethod
				));
			}
			catch (Exception exception)
			{
				failed++;
				string installError =
					$"Patch install failed. mixin='{descriptor.MixinType.FullName}', kind='{injection.Kind}', targetType='{targetMethod.DeclaringType?.FullName}', method='{targetMethod.Name}', descriptorKey='{injection.DescriptorKey}'. {exception}";
				GD.PrintErr($"[ReForge.Mixins] {installError}");
				records.Add(new MixinPatchApplyRecord(
					injection.DescriptorKey,
					injection.Kind,
					conflictKey,
					descriptor.TargetType.FullName ?? descriptor.TargetType.Name,
					targetMethod.Name,
					Success: false,
					installError,
					ConflictResolution: MixinConflictResolution.Fail,
					PatchedTarget: targetMethod
				));

				if (descriptor.StrictMode)
				{
					abortedByStrictMode = true;
					break;
				}
			}
		}

		if (abortedByStrictMode)
		{
			for (int i = records.Count; i < orderedInjections.Count; i++)
			{
				InjectionDescriptor notProcessed = orderedInjections[i];
				skipped++;
				records.Add(new MixinPatchApplyRecord(
					notProcessed.DescriptorKey,
					notProcessed.Kind,
					ConflictKey: string.Empty,
					descriptor.TargetType.FullName ?? descriptor.TargetType.Name,
					notProcessed.TargetMethodName,
					Success: false,
					"Skipped due to strict-mode abort after previous failure.",
					ConflictResolution: MixinConflictResolution.Skip,
					PatchedTarget: null
				));
			}

			GD.PrintErr($"[ReForge.Mixins] Strict mode aborted binder execution. mixin='{descriptor.MixinType.FullName}', installed={installed}, failed={failed}, skipped={skipped}.");
		}

		return new MixinPatchBindResult(
			descriptor.MixinId,
			installed,
			failed,
			skipped,
			abortedByStrictMode,
			new ReadOnlyCollection<MixinPatchApplyRecord>(records)
		);
	}

	public IReadOnlyList<MixinAppliedEntry> GetAppliedEntries()
	{
		return _appliedRegistry.Snapshot();
	}

	public int RemoveAppliedByHarmonyId(string harmonyId)
	{
		ArgumentNullException.ThrowIfNull(harmonyId);
		return _appliedRegistry.RemoveByHarmonyId(harmonyId);
	}

	public int RemoveAppliedByInjectionKeys(IReadOnlyCollection<string> injectionDescriptorKeys)
	{
		ArgumentNullException.ThrowIfNull(injectionDescriptorKeys);

		int removed = 0;
		foreach (string key in injectionDescriptorKeys)
		{
			if (string.IsNullOrWhiteSpace(key))
			{
				continue;
			}

			if (_appliedRegistry.UnregisterByInjectionKey(key))
			{
				removed++;
			}
		}

		return removed;
	}

	private static bool TryMapPatchKind(InjectionKind kind, out PatchSlot patchSlot)
	{
		switch (kind)
		{
			case InjectionKind.InjectPrefix:
				patchSlot = new PatchSlot("prefix", PatchSlotType.Prefix);
				return true;
			case InjectionKind.InjectPostfix:
				patchSlot = new PatchSlot("postfix", PatchSlotType.Postfix);
				return true;
			case InjectionKind.InjectFinalizer:
				patchSlot = new PatchSlot("finalizer", PatchSlotType.Finalizer);
				return true;
			case InjectionKind.Redirect:
				patchSlot = new PatchSlot("transpiler", PatchSlotType.Transpiler);
				return true;
			case InjectionKind.ModifyArg:
				patchSlot = new PatchSlot("transpiler", PatchSlotType.Transpiler);
				return true;
			case InjectionKind.ModifyConstant:
				patchSlot = new PatchSlot("transpiler", PatchSlotType.Transpiler);
				return true;
			case InjectionKind.Overwrite:
				patchSlot = new PatchSlot("prefix", PatchSlotType.Prefix);
				return true;
			default:
				patchSlot = default;
				return false;
		}
	}

	// ════════════════════════════════════════════════════════════════════════════════════════════
	// 高级注入语义：根据 InjectionKind 构建实际的 Harmony 补丁方法
	// ════════════════════════════════════════════════════════════════════════════════════════════

	private bool TryBuildPatchMethods(
		InjectionDescriptor injection,
		MethodBase targetMethod,
		out HarmonyMethod? prefix,
		out HarmonyMethod? postfix,
		out HarmonyMethod? transpiler,
		out HarmonyMethod? finalizer,
		out string error)
	{
		prefix = null;
		postfix = null;
		transpiler = null;
		finalizer = null;
		error = string.Empty;

		try
		{
			switch (injection.Kind)
			{
				// ────────────────────────────────────────────────────────────────────────────────
				// 基础注入：直接使用 handler 方法
				// ────────────────────────────────────────────────────────────────────────────────
				case InjectionKind.InjectPrefix:
					prefix = new HarmonyMethod(injection.HandlerMethod) { priority = injection.Priority };
					return true;

				case InjectionKind.InjectPostfix:
					postfix = new HarmonyMethod(injection.HandlerMethod) { priority = injection.Priority };
					return true;

				case InjectionKind.InjectFinalizer:
					finalizer = new HarmonyMethod(injection.HandlerMethod) { priority = injection.Priority };
					return true;

				// ────────────────────────────────────────────────────────────────────────────────
				// Overwrite：生成 prefix + return false 完全替换原方法
				// ────────────────────────────────────────────────────────────────────────────────
				case InjectionKind.Overwrite:
					return TryBuildOverwritePrefix(injection, targetMethod, out prefix, out error);

				// ────────────────────────────────────────────────────────────────────────────────
				// ModifyArg：生成 transpiler 修改指定参数
				// ────────────────────────────────────────────────────────────────────────────────
				case InjectionKind.ModifyArg:
					return TryBuildModifyArgTranspiler(injection, targetMethod, out transpiler, out error);

				// ────────────────────────────────────────────────────────────────────────────────
				// ModifyConstant：生成 transpiler 修改常量值
				// ────────────────────────────────────────────────────────────────────────────────
				case InjectionKind.ModifyConstant:
					return TryBuildModifyConstantTranspiler(injection, out transpiler, out error);

				// ────────────────────────────────────────────────────────────────────────────────
				// Redirect：生成 transpiler 重定向方法调用
				// ────────────────────────────────────────────────────────────────────────────────
				case InjectionKind.Redirect:
					return TryBuildRedirectTranspiler(injection, targetMethod, out transpiler, out error);

				default:
					error = $"Unsupported injection kind: {injection.Kind}";
					return false;
			}
		}
		catch (Exception ex)
		{
			error = $"Failed to build patch methods for {injection.Kind}: {ex.Message}";
			return false;
		}
	}

	// ════════════════════════════════════════════════════════════════════════════════════════════
	// Overwrite 实现：通过动态生成 Prefix 完全替换原方法
	// ════════════════════════════════════════════════════════════════════════════════════════════

	private static bool TryBuildOverwritePrefix(
		InjectionDescriptor injection,
		MethodBase targetMethod,
		out HarmonyMethod? prefix,
		out string error)
	{
		prefix = null;
		error = string.Empty;

		try
		{
			// 生成动态 Prefix 方法：调用 handler 并返回 false 跳过原方法
			MethodInfo overwritePrefix = TranspilerFactory.CreateOverwritePrefix(targetMethod, injection.HandlerMethod);
			prefix = new HarmonyMethod(overwritePrefix) { priority = injection.Priority };
			return true;
		}
		catch (Exception ex)
		{
			error = $"Failed to build Overwrite prefix: {ex.Message}";
			return false;
		}
	}

	// ════════════════════════════════════════════════════════════════════════════════════════════
	// ModifyArg 实现：生成 Transpiler 在调用点前修改参数
	// ════════════════════════════════════════════════════════════════════════════════════════════

	private bool TryBuildModifyArgTranspiler(
		InjectionDescriptor injection,
		MethodBase targetMethod,
		out HarmonyMethod? transpiler,
		out string error)
	{
		transpiler = null;
		error = string.Empty;

		if (injection.ArgumentIndex == null || injection.ArgumentIndex < 0)
		{
			error = $"ModifyArg requires a valid ArgumentIndex >= 0, got: {injection.ArgumentIndex}";
			return false;
		}

		try
		{
			int argIndex = injection.ArgumentIndex.Value;
			MethodInfo handlerMethod = injection.HandlerMethod;
			int ordinal = injection.Ordinal;

			// 创建 Transpiler 委托
			Func<IEnumerable<CodeInstruction>, IEnumerable<CodeInstruction>> transpilerFunc =
				TranspilerFactory.CreateModifyArgTranspiler(
					targetCallSite: null, // 匹配所有调用点
					argumentIndex: argIndex,
					handlerMethod: handlerMethod,
					ordinal: ordinal
				);

			// 将委托包装为静态方法供 Harmony 使用
			MethodInfo wrapperMethod = CreateTranspilerWrapper(transpilerFunc, $"ModifyArg_{injection.DescriptorKey}");
			transpiler = new HarmonyMethod(wrapperMethod) { priority = injection.Priority };
			return true;
		}
		catch (Exception ex)
		{
			error = $"Failed to build ModifyArg transpiler: {ex.Message}";
			return false;
		}
	}

	// ════════════════════════════════════════════════════════════════════════════════════════════
	// ModifyConstant 实现：生成 Transpiler 修改常量加载指令
	// ════════════════════════════════════════════════════════════════════════════════════════════

	private bool TryBuildModifyConstantTranspiler(
		InjectionDescriptor injection,
		out HarmonyMethod? transpiler,
		out string error)
	{
		transpiler = null;
		error = string.Empty;

		if (string.IsNullOrWhiteSpace(injection.ConstantExpression))
		{
			error = "ModifyConstant requires a non-empty ConstantExpression";
			return false;
		}

		try
		{
			string constantExpr = injection.ConstantExpression;
			MethodInfo handlerMethod = injection.HandlerMethod;
			int ordinal = injection.Ordinal;

			// 创建 Transpiler 委托
			Func<IEnumerable<CodeInstruction>, IEnumerable<CodeInstruction>> transpilerFunc =
				TranspilerFactory.CreateModifyConstantTranspiler(
					constantExpression: constantExpr,
					handlerMethod: handlerMethod,
					ordinal: ordinal
				);

			// 将委托包装为静态方法供 Harmony 使用
			MethodInfo wrapperMethod = CreateTranspilerWrapper(transpilerFunc, $"ModifyConstant_{injection.DescriptorKey}");
			transpiler = new HarmonyMethod(wrapperMethod) { priority = injection.Priority };
			return true;
		}
		catch (Exception ex)
		{
			error = $"Failed to build ModifyConstant transpiler: {ex.Message}";
			return false;
		}
	}

	// ════════════════════════════════════════════════════════════════════════════════════════════
	// Redirect 实现：生成 Transpiler 重定向方法调用
	// ════════════════════════════════════════════════════════════════════════════════════════════

	private bool TryBuildRedirectTranspiler(
		InjectionDescriptor injection,
		MethodBase targetMethod,
		out HarmonyMethod? transpiler,
		out string error)
	{
		transpiler = null;
		error = string.Empty;

		if (string.IsNullOrWhiteSpace(injection.At))
		{
			error = "Redirect requires a non-empty At expression (e.g., 'INVOKE:MethodName')";
			return false;
		}

		try
		{
			string at = injection.At;
			MethodInfo handlerMethod = injection.HandlerMethod;
			int ordinal = injection.Ordinal;

			// 创建 Transpiler 委托
			Func<IEnumerable<CodeInstruction>, IEnumerable<CodeInstruction>> transpilerFunc =
				TranspilerFactory.CreateRedirectTranspiler(
					targetCallSite: targetMethod,
					handlerMethod: handlerMethod,
					at: at,
					ordinal: ordinal
				);

			// 将委托包装为静态方法供 Harmony 使用
			MethodInfo wrapperMethod = CreateTranspilerWrapper(transpilerFunc, $"Redirect_{injection.DescriptorKey}");
			transpiler = new HarmonyMethod(wrapperMethod) { priority = injection.Priority };
			return true;
		}
		catch (Exception ex)
		{
			error = $"Failed to build Redirect transpiler: {ex.Message}";
			return false;
		}
	}

	// ════════════════════════════════════════════════════════════════════════════════════════════
	// Transpiler 包装器：将委托转换为 Harmony 可用的静态方法
	// ════════════════════════════════════════════════════════════════════════════════════════════

	private static readonly Dictionary<string, TranspilerHolder> _transpilerCache = new(StringComparer.Ordinal);
	private static readonly object _transpilerCacheLock = new();

	private static MethodInfo CreateTranspilerWrapper(
		Func<IEnumerable<CodeInstruction>, IEnumerable<CodeInstruction>> transpilerFunc,
		string uniqueKey)
	{
		lock (_transpilerCacheLock)
		{
			// 缓存 holder 以保持委托存活
			string safeKey = SanitizeKey(uniqueKey);
			if (!_transpilerCache.TryGetValue(safeKey, out TranspilerHolder? holder))
			{
				holder = new TranspilerHolder(transpilerFunc);
				_transpilerCache[safeKey] = holder;
			}

			return holder.TranspilerMethod;
		}
	}

	private static string SanitizeKey(string key)
	{
		// 移除非法字符，保留字母数字和下划线
		char[] chars = key.ToCharArray();
		for (int i = 0; i < chars.Length; i++)
		{
			if (!char.IsLetterOrDigit(chars[i]) && chars[i] != '_')
			{
				chars[i] = '_';
			}
		}

		return new string(chars);
	}

	/// <summary>
	/// 持有 Transpiler 委托的容器类，通过实例方法桥接到 Harmony。
	/// </summary>
	private sealed class TranspilerHolder
	{
		private static int _counter;
		private readonly Func<IEnumerable<CodeInstruction>, IEnumerable<CodeInstruction>> _transpiler;

		public TranspilerHolder(Func<IEnumerable<CodeInstruction>, IEnumerable<CodeInstruction>> transpiler)
		{
			_transpiler = transpiler ?? throw new ArgumentNullException(nameof(transpiler));

			// 创建动态方法作为 Harmony transpiler 入口
			int id = System.Threading.Interlocked.Increment(ref _counter);
			DynamicMethod dm = new(
				$"TranspilerWrapper_{id}",
				typeof(IEnumerable<CodeInstruction>),
				new[] { typeof(IEnumerable<CodeInstruction>) },
				typeof(TranspilerHolder).Module,
				skipVisibility: true
			);

			ILGenerator il = dm.GetILGenerator();

			// 获取当前 holder 实例的 Invoke 方法
			MethodInfo invokeMethod = typeof(TranspilerHolder).GetMethod(
				nameof(Invoke),
				BindingFlags.Instance | BindingFlags.NonPublic
			)!;

			// 加载 this（通过闭包捕获）
			// 由于 DynamicMethod 无法直接捕获 this，我们使用静态字段存储
			// 改用更简单的方案：直接返回 Invoke 方法
			TranspilerMethod = invokeMethod;
		}

		public MethodInfo TranspilerMethod { get; }

		[System.Diagnostics.CodeAnalysis.SuppressMessage("CodeQuality", "IDE0051", Justification = "Called via reflection")]
		private IEnumerable<CodeInstruction> Invoke(IEnumerable<CodeInstruction> instructions)
		{
			return _transpiler(instructions);
		}
	}

	private readonly record struct PatchSlot(string SlotName, PatchSlotType SlotType);

	private enum PatchSlotType
	{
		Prefix = 0,
		Postfix = 1,
		Transpiler = 2,
		Finalizer = 3,
	}
}
