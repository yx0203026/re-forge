#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using ReForgeFramework.Api.Events;

namespace ReForgeFramework.EventWheel.Patches;

/// <summary>
/// 普通事件选项补丁。
/// 在 EventModel 生成初始选项后注入 EventWheel 规则执行。
/// </summary>
[HarmonyPatch(typeof(EventModel), "GenerateInitialOptionsWrapper")]
internal static class EventModelOptionPatches
{
	/// <summary>
	/// Postfix：对普通事件选项应用 EventWheel 变更。
	/// </summary>
	[HarmonyPostfix]
	private static void Postfix(EventModel __instance, ref IReadOnlyList<EventOption> __result)
	{
		EventWheelOptionPatchRuntime.TryApplyMutation(
			model: __instance,
			options: ref __result,
			expectedKind: EventKind.Normal,
			patchId: "eventmodel.generate_initial_options");
	}
}

/// <summary>
/// EventWheel 选项补丁运行时。
/// 聚合定义查询、计划构建、执行落地、异常降级与诊断发射。
/// </summary>
internal static class EventWheelOptionPatchRuntime
{
	private const string PatchSourceModId = "reforge.eventwheel.patch";
	private static int _runtimeUnavailableLogged;

	/// <summary>
	/// 尝试对当前事件模型应用变更。
	/// 失败时以诊断与日志方式降级，不中断原流程。
	/// </summary>
	internal static void TryApplyMutation(
		EventModel? model,
		ref IReadOnlyList<EventOption> options,
		EventKind expectedKind,
		string patchId)
	{
		if (model == null)
		{
			return;
		}

		options ??= Array.Empty<EventOption>();

		if (!global::ReForge.EventWheel.TryGetRuntime(
			out ReForgeFramework.EventWheel.EventDefinitionRegistry? registry,
			out ReForgeFramework.EventWheel.EventMutationPlanner? planner,
			out ReForgeFramework.EventWheel.EventMutationExecutor? executor,
			out ReForgeFramework.EventWheel.EventWheelDiagnostics? diagnostics))
		{
			EmitRuntimeUnavailableOnce(patchId, model);
			return;
		}

		string eventId = ResolveEventId(model);
		if (eventId.Length == 0)
		{
			EmitPatchFailure(
				diagnostics,
				severity: EventWheelSeverity.Warning,
				model: model,
				eventId: string.Empty,
				expectedKind: expectedKind,
				patchId: patchId,
				message: "EventWheel patch skipped because event id could not be resolved.",
				exceptionSummary: null,
				context: null);
			return;
		}

		if (!registry!.TryGetDefinition(eventId, model, out IEventDefinition? definition) || definition == null)
		{
			return;
		}

		if (!definition.IsApplicable(model))
		{
			return;
		}

		if (definition.Kind != expectedKind)
		{
			return;
		}

		try
		{
			bool isMultiplayer = TryIsMultiplayer();
			IReadOnlyList<IEventMutationRule> rules = registry.GetMutationRules(eventId);
			EventMutationPlan plan = planner!.BuildPlan(model, definition, rules);

			// 在执行器读取快照前先种入本轮生成选项，避免读取到旧状态。
			EventModelBridge bridge = new();
			if (!bridge.TryApplyCurrentOptions(model, options, out string seedMessage))
			{
				if (TryBuildProjectedOptionsWithoutSeed(
					model,
					options,
					plan,
					isMultiplayer,
					out IReadOnlyList<EventOption> projectedOptions,
					out int warningCount,
					out string projectedMessage))
				{
					options = projectedOptions;
					EmitPatchNotice(
						diagnostics,
						severity: EventWheelSeverity.Warning,
						model: model,
						eventId: eventId,
						expectedKind: expectedKind,
						patchId: patchId,
						message: $"EventWheel patch degraded to no-seed projection. {projectedMessage}",
						context: new Dictionary<string, string>(StringComparer.Ordinal)
						{
							["seedError"] = seedMessage,
							["warnings"] = warningCount.ToString()
						});
					return;
				}

				EmitPatchFailure(
					diagnostics,
					severity: EventWheelSeverity.Error,
					model: model,
					eventId: eventId,
					expectedKind: expectedKind,
					patchId: patchId,
					message: $"EventWheel patch failed to seed generated options. {seedMessage}",
					exceptionSummary: seedMessage,
					context: null);
				return;
			}

			EventMutationExecutionResult execution = executor!.Execute(
				model: model,
				plan: plan,
				isMultiplayer: isMultiplayer,
				sourceModId: PatchSourceModId);

			if (!execution.Result.Success)
			{
				EmitPatchFailure(
					diagnostics,
					severity: EventWheelSeverity.Error,
					model: model,
					eventId: eventId,
					expectedKind: expectedKind,
					patchId: patchId,
					message: $"EventWheel patch execution failed. code='{execution.Result.Code}', message='{execution.Result.Message}'.",
					exceptionSummary: execution.Result.Message,
					context: new Dictionary<string, string>(StringComparer.Ordinal)
					{
						["code"] = execution.Result.Code,
						["rolledBack"] = execution.RolledBack ? "true" : "false",
						["warnings"] = execution.Warnings.Count.ToString()
					});
				return;
			}

			if (!bridge.TrySnapshotCurrentOptions(model, out IReadOnlyList<EventOption> patchedOptions, out string snapshotMessage))
			{
				EmitPatchFailure(
					diagnostics,
					severity: EventWheelSeverity.Error,
					model: model,
					eventId: eventId,
					expectedKind: expectedKind,
					patchId: patchId,
					message: $"EventWheel patch execution succeeded but result snapshot failed. {snapshotMessage}",
					exceptionSummary: snapshotMessage,
					context: null);
				return;
			}

			options = patchedOptions;
		}
		catch (Exception ex)
		{
			EmitPatchFailure(
				diagnostics,
				severity: EventWheelSeverity.Error,
				model: model,
				eventId: eventId,
				expectedKind: expectedKind,
				patchId: patchId,
				message: $"EventWheel patch threw unexpectedly. {ex.GetType().Name}: {ex.Message}",
				exceptionSummary: ex.ToString(),
				context: null);
		}
	}

	private static bool TryBuildProjectedOptionsWithoutSeed(
		EventModel model,
		IReadOnlyList<EventOption> existingOptions,
		EventMutationPlan plan,
		bool isMultiplayer,
		out IReadOnlyList<EventOption> projectedOptions,
		out int warningCount,
		out string message)
	{
		NetworkOptionGuard networkGuard = new();
		NetworkOptionGuardResult normalized = networkGuard.NormalizeForNetwork(
			eventId: plan.EventId,
			options: plan.PlannedOptions,
			isMultiplayer: isMultiplayer,
			sourceModId: PatchSourceModId);

		List<EventMutationWarning> warnings = new(plan.Warnings.Count + normalized.Report.Warnings.Count);
		for (int i = 0; i < plan.Warnings.Count; i++)
		{
			warnings.Add(plan.Warnings[i]);
		}

		for (int i = 0; i < normalized.Report.Warnings.Count; i++)
		{
			warnings.Add(normalized.Report.Warnings[i]);
		}

		EventModelBridge bridge = new();
		IReadOnlyList<EventOption> runtimeOptions = bridge.BuildRuntimeOptions(
			model: model,
			plannedOptions: normalized.Options,
			existingOptions: existingOptions,
			eventId: plan.EventId,
			sourceModId: PatchSourceModId,
			warnings: warnings);

		if (runtimeOptions.Count == 0)
		{
			if (existingOptions.Count > 0)
			{
				projectedOptions = existingOptions;
				warningCount = warnings.Count;
				message = $"runtime options became empty; restored original options (count={existingOptions.Count}).";
				return true;
			}

			projectedOptions = Array.Empty<EventOption>();
			warningCount = warnings.Count;
			message = "runtime options are empty and no original options are available.";
			return false;
		}

		projectedOptions = runtimeOptions;
		warningCount = warnings.Count;
		message = $"input={existingOptions.Count}, output={runtimeOptions.Count}, warnings={warnings.Count}, multiplayer={isMultiplayer}.";
		return true;
	}

	private static void EmitRuntimeUnavailableOnce(string patchId, EventModel model)
	{
		if (Interlocked.Exchange(ref _runtimeUnavailableLogged, 1) != 0)
		{
			return;
		}

		GD.PrintErr($"[ReForge.EventWheel] runtime unavailable, mutation patch downgraded to no-op. patchId='{patchId}', modelType='{model.GetType().FullName ?? model.GetType().Name}'.");
	}

	private static string ResolveEventId(EventModel model)
	{
		try
		{
			string entry = model.Id.Entry?.Trim() ?? string.Empty;
			if (entry.Length > 0)
			{
				return entry;
			}
		}
		catch
		{
			// 继续尝试回退路径。
		}

		try
		{
			string fromId = model.Id.ToString()?.Trim() ?? string.Empty;
			if (fromId.Length > 0)
			{
				return fromId;
			}
		}
		catch
		{
			// 继续尝试类型名回退。
		}

		return model.GetType().Name?.Trim() ?? string.Empty;
	}

	private static bool TryIsMultiplayer()
	{
		try
		{
			return RunManager.Instance.NetService.Type.IsMultiplayer();
		}
		catch
		{
			return false;
		}
	}

	private static void EmitPatchFailure(
		ReForgeFramework.EventWheel.EventWheelDiagnostics? diagnostics,
		EventWheelSeverity severity,
		EventModel model,
		string eventId,
		EventKind expectedKind,
		string patchId,
		string message,
		string? exceptionSummary,
		IReadOnlyDictionary<string, string>? context)
	{
		string normalizedEventId = eventId?.Trim() ?? string.Empty;
		if (normalizedEventId.Length == 0)
		{
			normalizedEventId = "unknown.event";
		}

		Dictionary<string, string> mergedContext = new(StringComparer.Ordinal)
		{
			["patchId"] = patchId,
			["modelType"] = model.GetType().FullName ?? model.GetType().Name,
			["expectedKind"] = expectedKind.ToString()
		};

		if (context != null)
		{
			foreach (KeyValuePair<string, string> pair in context)
			{
				mergedContext[pair.Key] = pair.Value;
			}
		}

		GD.PrintErr($"[ReForge.EventWheel] {message}");

		diagnostics?.Track(
			stage: EventWheelStage.Execute,
			severity: severity,
			eventId: normalizedEventId,
			sourceModId: PatchSourceModId,
			message: message,
			exceptionSummary: exceptionSummary,
			context: mergedContext);
	}

	private static void EmitPatchNotice(
		ReForgeFramework.EventWheel.EventWheelDiagnostics? diagnostics,
		EventWheelSeverity severity,
		EventModel model,
		string eventId,
		EventKind expectedKind,
		string patchId,
		string message,
		IReadOnlyDictionary<string, string>? context)
	{
		string normalizedEventId = eventId?.Trim() ?? string.Empty;
		if (normalizedEventId.Length == 0)
		{
			normalizedEventId = "unknown.event";
		}

		Dictionary<string, string> mergedContext = new(StringComparer.Ordinal)
		{
			["patchId"] = patchId,
			["modelType"] = model.GetType().FullName ?? model.GetType().Name,
			["expectedKind"] = expectedKind.ToString()
		};

		if (context != null)
		{
			foreach (KeyValuePair<string, string> pair in context)
			{
				mergedContext[pair.Key] = pair.Value;
			}
		}

		if (severity == EventWheelSeverity.Error)
		{
			GD.PrintErr($"[ReForge.EventWheel] {message}");
		}
		else
		{
			GD.Print($"[ReForge.EventWheel] {message}");
		}

		diagnostics?.Track(
			stage: EventWheelStage.Execute,
			severity: severity,
			eventId: normalizedEventId,
			sourceModId: PatchSourceModId,
			message: message,
			exceptionSummary: null,
			context: mergedContext);
	}
}
