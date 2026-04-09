#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Reflection.Emit;
using Godot;
using HarmonyLib;

namespace ReForgeFramework.Mixins.Runtime;

/// <summary>
/// 补丁应用record：记录单次补丁应用的详细信息。
/// </summary>
public sealed record MixinPatchApplyRecord(
	/// <summary>注入描述符键。</summary>
	string InjectionDescriptorKey,

	/// <summary>注入类型。</summary>
	InjectionKind Kind,

	/// <summary>冲突检测键。</summary>
	string ConflictKey,

	/// <summary>目标类型名称。</summary>
	string TargetTypeName,

	/// <summary>目标方法名称。</summary>
	string TargetMethodName,

	/// <summary>补丁是否成功应用。</summary>
	bool Success,

	/// <summary>应用结果说明。</summary>
	string Message,

	/// <summary>冲突发生时的处理策略（若有）。</summary>
	MixinConflictResolution? ConflictResolution,

	/// <summary>修补后的目标方法。</summary>
	MethodBase? PatchedTarget,

	/// <summary>实际应用的补丁方法。</summary>
	MethodInfo? AppliedPatchMethod
);

/// <summary>
/// Shadow 字段绑定 record：记录单条字段绑定的结果。
/// </summary>
public sealed record ShadowBindRecord(
	/// <summary>Shadow 描述符键。</summary>
	string ShadowDescriptorKey,

	/// <summary>Mixin 中的字段成员名。</summary>
	string MixinMemberName,

	/// <summary>目标类型中找到的字段名。</summary>
	string TargetMemberName,

	/// <summary>绑定是否成功。</summary>
	bool Success,

	/// <summary>是否为可选字段（不存在时允许跳过）。</summary>
	bool Optional,

	/// <summary>绑定结果说明。</summary>
	string Message
);

/// <summary>
/// 单个 Mixin 的补丁绑定与应用结果。
/// </summary>
public sealed class MixinPatchBindResult
{
	/// <summary>
	/// 初始化 <see cref="MixinPatchBindResult"/> 的新实例。
	/// </summary>
	/// <param name="mixinId">Mixin 标识符。</param>
	/// <param name="installed">成功安装的补丁数。</param>
	/// <param name="failed">安装失败的补丁数。</param>
	/// <param name="skipped">被跳过的补丁数。</param>
	/// <param name="abortedByStrictMode">是否因严格模式中止。</param>
	/// <param name="records">补丁应用记录列表。</param>
	/// <param name="shadowInstalled">成功绑定的 Shadow 字段数（可选）。</param>
	/// <param name="shadowFailed">绑定失败的 Shadow 字段数（可选）。</param>
	/// <param name="shadowSkipped">被跳过的 Shadow 字段数（可选）。</param>
	/// <param name="shadowRecords">Shadow 绑定记录列表（可选）。</param>
	public MixinPatchBindResult(
		string mixinId,
		int installed,
		int failed,
		int skipped,
		bool abortedByStrictMode,
		IReadOnlyList<MixinPatchApplyRecord> records,
		int shadowInstalled = 0,
		int shadowFailed = 0,
		int shadowSkipped = 0,
		IReadOnlyList<ShadowBindRecord>? shadowRecords = null)
	{
		ArgumentNullException.ThrowIfNull(mixinId);
		ArgumentNullException.ThrowIfNull(records);

		MixinId = mixinId;
		Installed = installed;
		Failed = failed;
		Skipped = skipped;
		AbortedByStrictMode = abortedByStrictMode;
		Records = records;
		ShadowInstalled = shadowInstalled;
		ShadowFailed = shadowFailed;
		ShadowSkipped = shadowSkipped;
		ShadowRecords = shadowRecords ?? Array.Empty<ShadowBindRecord>();
	}

	/// <summary>
	/// 获取 Mixin 标识符。
	/// </summary>
	public string MixinId { get; }

	/// <summary>
	/// 获取成功安装的补丁数。
	/// </summary>
	public int Installed { get; }

	/// <summary>
	/// 获取安装失败的补丁数。
	/// </summary>
	public int Failed { get; }

	/// <summary>
	/// 获取被跳过的补丁数。
	/// </summary>
	public int Skipped { get; }

	/// <summary>
	/// 获取是否因严格模式违规而中止。
	/// </summary>
	public bool AbortedByStrictMode { get; }

	/// <summary>
	/// 获取补丁应用详细记录列表。
	/// </summary>
	public IReadOnlyList<MixinPatchApplyRecord> Records { get; }

	/// <summary>
	/// 获取成功绑定的 Shadow 字段数。
	/// </summary>
	public int ShadowInstalled { get; }

	/// <summary>
	/// 获取绑定失败的 Shadow 字段数。
	/// </summary>
	public int ShadowFailed { get; }

	/// <summary>
	/// 获取被跳过的 Shadow 字段数。
	/// </summary>
	public int ShadowSkipped { get; }

	/// <summary>
	/// 获取 Shadow 字段绑定详细记录列表。
	/// </summary>
	public IReadOnlyList<ShadowBindRecord> ShadowRecords { get; }
}

/// <summary>
/// Harmony 补丁绑定器：负责将 Mixin 描述符中的注入转换为 Harmony 补丁并应用。
/// </summary>
/// <remarks>
/// 此类是 Mixin 系统与 Harmony 库之间的适配层，执行以下职责：
/// • 验证目标方法是否存在且可访问
/// • 将注入描述符转换为 Harmony 补丁
/// • 应用补丁并处理冲突
/// • 跟踪已应用补丁以供后续卸载
/// • 绑定 Shadow 字段映射
/// </remarks>
internal sealed class HarmonyPatchBinder
{
	private readonly HarmonyTargetResolver _targetResolver = new();
	private readonly MixinConflictPolicy _conflictPolicy;
	private readonly MixinAppliedRegistry _appliedRegistry;

	/// <summary>
	/// 初始化 <see cref="HarmonyPatchBinder"/> 的新实例。
	/// </summary>
	/// <param name="conflictPolicy">冲突处理策略（可选，使用默认策略）。</param>
	/// <param name="appliedRegistry">已应用补丁追踪注册表（可选，创建新实例）。</param>
	public HarmonyPatchBinder(
		MixinConflictPolicy? conflictPolicy = null,
		MixinAppliedRegistry? appliedRegistry = null)
	{
		_conflictPolicy = conflictPolicy ?? new MixinConflictPolicy();
		_appliedRegistry = appliedRegistry ?? new MixinAppliedRegistry();
	}

	/// <summary>
	/// 绑定并应用单个 Mixin 的所有补丁。
	/// </summary>
	/// <param name="descriptor">Mixin 描述符。</param>
	/// <param name="harmony">Harmony 实例，用于应用补丁。</param>
	/// <returns>包含绑定结果与统计信息的 <see cref="MixinPatchBindResult"/> 对象。</returns>
	/// <exception cref="ArgumentNullException">当任何参数为 null 时。</exception>
	public MixinPatchBindResult BindAndApply(MixinDescriptor descriptor, Harmony harmony)
	{
		ArgumentNullException.ThrowIfNull(descriptor);
		ArgumentNullException.ThrowIfNull(harmony);

		int installed = 0;
		int failed = 0;
		int skipped = 0;
		int shadowInstalled = 0;
		int shadowFailed = 0;
		int shadowSkipped = 0;
		bool abortedByStrictMode = false;
		List<MixinPatchApplyRecord> records = new(descriptor.Injections.Count);
		List<ShadowBindRecord> shadowRecords = descriptor.ShadowFields.Count == 0
			? new List<ShadowBindRecord>()
			: new List<ShadowBindRecord>(descriptor.ShadowFields.Count);
		List<InjectionDescriptor> orderedInjections = new(descriptor.Injections);
		orderedInjections.Sort(static (a, b) => string.CompareOrdinal(a.DescriptorKey, b.DescriptorKey));

		if (descriptor.ShadowFields.Count > 0)
		{
			TryBindShadowFields(
				descriptor,
				out shadowInstalled,
				out shadowFailed,
				out shadowSkipped,
				out bool shadowStrictAbort,
				shadowRecords
			);

			if (shadowStrictAbort)
			{
				abortedByStrictMode = true;
			}

			GD.Print(
				$"[ReForge.Mixins] Shadow binding summary. mixin='{descriptor.MixinType.FullName}', targetType='{descriptor.TargetType.FullName}', installed={shadowInstalled}, failed={shadowFailed}, skipped={shadowSkipped}, strictAbort={shadowStrictAbort}."
			);
		}

		for (int i = 0; i < orderedInjections.Count; i++)
		{
			if (abortedByStrictMode)
			{
				break;
			}

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
					PatchedTarget: null,
					AppliedPatchMethod: null
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
						PatchedTarget: null,
						AppliedPatchMethod: null
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
					PatchedTarget: null,
					AppliedPatchMethod: null
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
					PatchedTarget: null,
					AppliedPatchMethod: null
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
						PatchedTarget: targetMethod,
						AppliedPatchMethod: null
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
					PatchedTarget: targetMethod,
					AppliedPatchMethod: null
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
						PatchedTarget: targetMethod,
						AppliedPatchMethod: null
					));
					continue;
				}

				if (decision.Resolution == MixinConflictResolution.Overwrite)
				{
					try
					{
						harmony.Unpatch(conflictEntry.TargetMethod, conflictEntry.AppliedPatchMethod);
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
							PatchedTarget: targetMethod,
							AppliedPatchMethod: null
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
						PatchedTarget: targetMethod,
						AppliedPatchMethod: null
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
					PatchedTarget: targetMethod,
					AppliedPatchMethod: null
				));

				if (descriptor.StrictMode)
				{
					abortedByStrictMode = true;
					break;
				}

				continue;
			}

			MethodInfo? appliedPatchMethod = ResolveAppliedPatchMethod(injection.Kind, prefix, postfix, transpiler, finalizer);
			if (appliedPatchMethod == null)
			{
				failed++;
				string noPatchMethodError =
					$"Patch method resolution failed. mixin='{descriptor.MixinType.FullName}', kind='{injection.Kind}', targetType='{targetMethod.DeclaringType?.FullName}', method='{targetMethod.Name}', descriptorKey='{injection.DescriptorKey}'.";
				GD.PrintErr($"[ReForge.Mixins] {noPatchMethodError}");
				records.Add(new MixinPatchApplyRecord(
					injection.DescriptorKey,
					injection.Kind,
					conflictKey,
					descriptor.TargetType.FullName ?? descriptor.TargetType.Name,
					targetMethod.Name,
					Success: false,
					noPatchMethodError,
					ConflictResolution: MixinConflictResolution.Fail,
					PatchedTarget: targetMethod,
					AppliedPatchMethod: null
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
					appliedPatchMethod,
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
					PatchedTarget: targetMethod,
					AppliedPatchMethod: appliedPatchMethod
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
					PatchedTarget: targetMethod,
					AppliedPatchMethod: appliedPatchMethod
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
					PatchedTarget: null,
					AppliedPatchMethod: null
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
			new ReadOnlyCollection<MixinPatchApplyRecord>(records),
			shadowInstalled,
			shadowFailed,
			shadowSkipped,
			new ReadOnlyCollection<ShadowBindRecord>(shadowRecords)
		);
	}

	public IReadOnlyList<MixinAppliedEntry> GetAppliedEntries()
	{
		return _appliedRegistry.Snapshot();
	}

	public bool TryGetAppliedEntry(string injectionDescriptorKey, out MixinAppliedEntry? entry)
	{
		ArgumentNullException.ThrowIfNull(injectionDescriptorKey);
		return _appliedRegistry.TryGetByInjectionKey(injectionDescriptorKey, out entry);
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

	private static void TryBindShadowFields(
		MixinDescriptor descriptor,
		out int installed,
		out int failed,
		out int skipped,
		out bool abortedByStrictMode,
		List<ShadowBindRecord> records)
	{
		ArgumentNullException.ThrowIfNull(descriptor);
		ArgumentNullException.ThrowIfNull(records);

		installed = 0;
		failed = 0;
		skipped = 0;
		abortedByStrictMode = false;

		List<ShadowFieldDescriptor> orderedShadows = new(descriptor.ShadowFields);
		orderedShadows.Sort(static (a, b) => string.CompareOrdinal(a.DescriptorKey, b.DescriptorKey));

		for (int i = 0; i < orderedShadows.Count; i++)
		{
			ShadowFieldDescriptor shadow = orderedShadows[i];
			if (!TryResolveShadowTargetField(descriptor, shadow, out FieldInfo? targetField, out string resolvedTargetName, out string error))
			{
				if (shadow.Optional)
				{
					skipped++;
					string skipMessage =
						$"Optional shadow skipped (target field not found). mixin='{descriptor.MixinType.FullName}', targetType='{descriptor.TargetType.FullName}', member='{shadow.MixinField.Name}', target='{shadow.TargetName}', aliases='{BuildAliasText(shadow.Aliases)}'.";
					GD.Print($"[ReForge.Mixins] {skipMessage}");
					records.Add(new ShadowBindRecord(
						shadow.DescriptorKey,
						shadow.MixinField.Name,
						resolvedTargetName,
						Success: false,
						Optional: true,
						skipMessage
					));
					continue;
				}

				failed++;
				string failMessage =
					$"Shadow binding failed. mixin='{descriptor.MixinType.FullName}', targetType='{descriptor.TargetType.FullName}', member='{shadow.MixinField.Name}', target='{shadow.TargetName}', aliases='{BuildAliasText(shadow.Aliases)}'. {error}";
				GD.PrintErr($"[ReForge.Mixins] {failMessage}");
				records.Add(new ShadowBindRecord(
					shadow.DescriptorKey,
					shadow.MixinField.Name,
					resolvedTargetName,
					Success: false,
					Optional: false,
					failMessage
				));

				if (descriptor.StrictMode)
				{
					abortedByStrictMode = true;
					break;
				}

				continue;
			}

			if (targetField == null)
			{
				failed++;
				string nullFieldMessage =
					$"Shadow resolver returned null unexpectedly. mixin='{descriptor.MixinType.FullName}', targetType='{descriptor.TargetType.FullName}', member='{shadow.MixinField.Name}', target='{shadow.TargetName}'.";
				GD.PrintErr($"[ReForge.Mixins] {nullFieldMessage}");
				records.Add(new ShadowBindRecord(
					shadow.DescriptorKey,
					shadow.MixinField.Name,
					resolvedTargetName,
					Success: false,
					Optional: shadow.Optional,
					nullFieldMessage
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
				shadow.MixinField.SetValue(obj: null, value: targetField);
				installed++;
				string successMessage =
					$"Shadow bound. mixin='{descriptor.MixinType.FullName}', targetType='{descriptor.TargetType.FullName}', member='{shadow.MixinField.Name}', target='{targetField.Name}', descriptorKey='{shadow.DescriptorKey}'.";
				GD.Print($"[ReForge.Mixins] {successMessage}");
				records.Add(new ShadowBindRecord(
					shadow.DescriptorKey,
					shadow.MixinField.Name,
					targetField.Name,
					Success: true,
					Optional: shadow.Optional,
					successMessage
				));
			}
			catch (Exception exception)
			{
				failed++;
				string bindError =
					$"Shadow assignment failed. mixin='{descriptor.MixinType.FullName}', targetType='{descriptor.TargetType.FullName}', member='{shadow.MixinField.Name}', target='{targetField.Name}', descriptorKey='{shadow.DescriptorKey}'. {exception}";
				GD.PrintErr($"[ReForge.Mixins] {bindError}");
				records.Add(new ShadowBindRecord(
					shadow.DescriptorKey,
					shadow.MixinField.Name,
					targetField.Name,
					Success: false,
					Optional: shadow.Optional,
					bindError
				));

				if (descriptor.StrictMode)
				{
					abortedByStrictMode = true;
					break;
				}
			}
		}
	}

	private static bool TryResolveShadowTargetField(
		MixinDescriptor descriptor,
		ShadowFieldDescriptor shadow,
		out FieldInfo? targetField,
		out string resolvedTargetName,
		out string error)
	{
		ArgumentNullException.ThrowIfNull(descriptor);
		ArgumentNullException.ThrowIfNull(shadow);

		targetField = null;
		resolvedTargetName = string.Empty;
		error = string.Empty;

		List<string> candidates = BuildShadowCandidates(shadow);
		if (candidates.Count == 0)
		{
			error = "No shadow target candidates after normalization.";
			return false;
		}

		const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
		FieldInfo[] targetFields = descriptor.TargetType.GetFields(Flags);

		for (int i = 0; i < candidates.Count; i++)
		{
			string candidate = candidates[i];
			for (int j = 0; j < targetFields.Length; j++)
			{
				FieldInfo field = targetFields[j];
				if (!string.Equals(field.Name, candidate, StringComparison.Ordinal))
				{
					continue;
				}

				resolvedTargetName = candidate;
				targetField = field;
				return true;
			}
		}

		resolvedTargetName = candidates[0];
		error =
			$"Target field not found. mixin='{descriptor.MixinType.FullName}', targetType='{descriptor.TargetType.FullName}', member='{shadow.MixinField.Name}', candidates='{string.Join("|", candidates)}'.";
		return false;
	}

	private static List<string> BuildShadowCandidates(ShadowFieldDescriptor shadow)
	{
		HashSet<string> dedup = new(StringComparer.Ordinal);
		List<string> candidates = new();

		AddCandidate(candidates, dedup, shadow.TargetName);
		for (int i = 0; i < shadow.Aliases.Count; i++)
		{
			AddCandidate(candidates, dedup, shadow.Aliases[i]);
		}

		return candidates;
	}

	private static void AddCandidate(List<string> candidates, HashSet<string> dedup, string? candidate)
	{
		if (string.IsNullOrWhiteSpace(candidate))
		{
			return;
		}

		string trimmed = candidate.Trim();
		if (!dedup.Add(trimmed))
		{
			return;
		}

		candidates.Add(trimmed);
	}

	private static string BuildAliasText(IReadOnlyList<string> aliases)
	{
		if (aliases.Count == 0)
		{
			return string.Empty;
		}

		return string.Join("|", aliases);
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

	private static MethodInfo? ResolveAppliedPatchMethod(
		InjectionKind kind,
		HarmonyMethod? prefix,
		HarmonyMethod? postfix,
		HarmonyMethod? transpiler,
		HarmonyMethod? finalizer)
	{
		return kind switch
		{
			InjectionKind.InjectPrefix => prefix?.method,
			InjectionKind.InjectPostfix => postfix?.method,
			InjectionKind.InjectFinalizer => finalizer?.method,
			InjectionKind.Overwrite => prefix?.method,
			InjectionKind.ModifyArg => transpiler?.method,
			InjectionKind.ModifyConstant => transpiler?.method,
			InjectionKind.Redirect => transpiler?.method,
			_ => null,
		};
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
			RuntimeTranspiler transpilerFunc =
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
			RuntimeTranspiler transpilerFunc =
				TranspilerFactory.CreateModifyConstantTranspiler(
					constantExpression: constantExpr,
					handlerMethod: handlerMethod,
					ordinal: ordinal,
					requireMatch: !injection.Optional
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
			RuntimeTranspiler transpilerFunc =
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

	private static readonly Dictionary<string, RuntimeTranspiler> _transpilerCache = new(StringComparer.Ordinal);
	private static readonly Dictionary<string, MethodInfo> _transpilerMethodCache = new(StringComparer.Ordinal);
	private static readonly object _transpilerCacheLock = new();
	private static int _transpilerWrapperCounter;

	private static MethodInfo CreateTranspilerWrapper(
		RuntimeTranspiler transpilerFunc,
		string uniqueKey)
	{
		lock (_transpilerCacheLock)
		{
			string safeKey = SanitizeKey(uniqueKey);
			_transpilerCache[safeKey] = transpilerFunc;

			if (!_transpilerMethodCache.TryGetValue(safeKey, out MethodInfo? method))
			{
				method = BuildTranspilerDispatchMethod(safeKey);
				_transpilerMethodCache[safeKey] = method;
			}

			return method;
		}
	}

	private static MethodInfo BuildTranspilerDispatchMethod(string safeKey)
	{
		MethodInfo dispatcher = typeof(HarmonyPatchBinder).GetMethod(
			nameof(InvokeCachedTranspiler),
			BindingFlags.Static | BindingFlags.NonPublic
		)!;

		int id = System.Threading.Interlocked.Increment(ref _transpilerWrapperCounter);
		DynamicMethod dynamicMethod = new(
			$"TranspilerWrapper_{id}",
			typeof(IEnumerable<CodeInstruction>),
			new[] { typeof(IEnumerable<CodeInstruction>), typeof(ILGenerator) },
			typeof(HarmonyPatchBinder).Module,
			skipVisibility: true
		);

		ILGenerator il = dynamicMethod.GetILGenerator();
		il.Emit(OpCodes.Ldarg_0);
		il.Emit(OpCodes.Ldarg_1);
		il.Emit(OpCodes.Ldstr, safeKey);
		il.Emit(OpCodes.Call, dispatcher);
		il.Emit(OpCodes.Ret);

		return dynamicMethod;
	}

	private static IEnumerable<CodeInstruction> InvokeCachedTranspiler(
		IEnumerable<CodeInstruction> instructions,
		ILGenerator ilGenerator,
		string cacheKey)
	{
		RuntimeTranspiler? transpiler;
		lock (_transpilerCacheLock)
		{
			if (!_transpilerCache.TryGetValue(cacheKey, out transpiler) || transpiler == null)
			{
				throw new InvalidOperationException($"Transpiler delegate not found. key='{cacheKey}'.");
			}
		}

		return transpiler(instructions, ilGenerator);
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

	private readonly record struct PatchSlot(string SlotName, PatchSlotType SlotType);

	private enum PatchSlotType
	{
		Prefix = 0,
		Postfix = 1,
		Transpiler = 2,
		Finalizer = 3,
	}
}
