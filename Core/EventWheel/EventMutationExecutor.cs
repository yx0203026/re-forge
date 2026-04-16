#nullable enable

using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using ReForgeFramework.Api.Events;

namespace ReForgeFramework.EventWheel;

internal sealed record EventMutationExecutionResult(
	EventWheelResult Result,
	IReadOnlyList<EventMutationWarning> Warnings,
	bool RolledBack,
	int AppliedOptionCount
);

internal sealed class EventMutationExecutor
{
	private readonly EventModelBridge _bridge;
	private readonly NetworkOptionGuard _networkGuard;
	private readonly EventWheelDiagnostics? _diagnostics;

	public EventMutationExecutor(
		EventModelBridge? bridge = null,
		NetworkOptionGuard? networkGuard = null,
		EventWheelDiagnostics? diagnostics = null)
	{
		_bridge = bridge ?? new EventModelBridge();
		_networkGuard = networkGuard ?? new NetworkOptionGuard();
		_diagnostics = diagnostics;
	}

	public EventMutationExecutionResult Execute(
		EventModel? model,
		EventMutationPlan? plan,
		bool isMultiplayer,
		string sourceModId = "reforge.eventwheel")
	{
		string normalizedSourceModId = sourceModId?.Trim() ?? string.Empty;
		if (normalizedSourceModId.Length == 0)
		{
			normalizedSourceModId = "reforge.eventwheel";
		}

		if (model == null)
		{
			EventMutationExecutionResult failed = Failed(
				eventId: plan?.EventId ?? string.Empty,
				sourceModId: normalizedSourceModId,
				code: "execute.model_null",
				message: "Event mutation execution failed: EventModel is null.",
				warnings: plan?.Warnings ?? Array.Empty<EventMutationWarning>()
			);
			EmitDiagnosticsFromResult(failed, isMultiplayer);
			return failed;
		}

		if (plan == null)
		{
			EventMutationExecutionResult failed = Failed(
				eventId: string.Empty,
				sourceModId: normalizedSourceModId,
				code: "execute.plan_null",
				message: "Event mutation execution failed: mutation plan is null.",
				warnings: Array.Empty<EventMutationWarning>()
			);
			EmitDiagnosticsFromResult(failed, isMultiplayer);
			return failed;
		}

		List<EventMutationWarning> warnings = new(plan.Warnings.Count + 8);
		for (int i = 0; i < plan.Warnings.Count; i++)
		{
			warnings.Add(plan.Warnings[i]);
		}

		if (!_bridge.TrySnapshotCurrentOptions(model, out IReadOnlyList<EventOption> originalOptions, out string snapshotError))
		{
			warnings.Add(new EventMutationWarning(
				Severity: EventWheelSeverity.Error,
				Code: "execute.snapshot_failed",
				Message: snapshotError,
				EventId: plan.EventId,
				SourceModId: normalizedSourceModId
			));

			EventMutationExecutionResult failed = Failed(
				eventId: plan.EventId,
				sourceModId: normalizedSourceModId,
				code: "execute.snapshot_failed",
				message: $"Event mutation execution failed before apply: {snapshotError}",
				warnings: warnings
			);
			EmitDiagnosticsFromResult(failed, isMultiplayer);
			return failed;
		}

		NetworkOptionGuardResult networkResult = _networkGuard.NormalizeForNetwork(
			eventId: plan.EventId,
			options: plan.PlannedOptions,
			isMultiplayer: isMultiplayer,
			sourceModId: normalizedSourceModId);

		for (int i = 0; i < networkResult.Report.Warnings.Count; i++)
		{
			warnings.Add(networkResult.Report.Warnings[i]);
		}

		IReadOnlyList<EventOption> runtimeOptions = _bridge.BuildRuntimeOptions(
			model: model,
			plannedOptions: networkResult.Options,
			existingOptions: originalOptions,
			eventId: plan.EventId,
			sourceModId: normalizedSourceModId,
			warnings: warnings);

		if (runtimeOptions.Count == 0)
		{
			if (originalOptions.Count == 0)
			{
				warnings.Add(new EventMutationWarning(
					Severity: EventWheelSeverity.Error,
					Code: "execute.empty_result",
					Message: "Planned/runtime options are empty and no rollback snapshot is available.",
					EventId: plan.EventId,
					SourceModId: normalizedSourceModId
				));

				EventMutationExecutionResult failed = Failed(
					eventId: plan.EventId,
					sourceModId: normalizedSourceModId,
					code: "execute.empty_result",
					message: "Event mutation execution aborted to avoid empty option crash.",
					warnings: warnings
				);
				EmitDiagnosticsFromResult(failed, isMultiplayer);
				return failed;
			}

			warnings.Add(new EventMutationWarning(
				Severity: EventWheelSeverity.Warning,
				Code: "execute.empty_result_rollback_source",
				Message: "Runtime options became empty; fallback to original options snapshot.",
				EventId: plan.EventId,
				SourceModId: normalizedSourceModId
			));

			runtimeOptions = originalOptions;
		}

		bool rolledBack = false;
		try
		{
			if (!_bridge.TryApplyCurrentOptions(model, runtimeOptions, out string applyError))
			{
				bool rollbackOk = TryRollback(model, originalOptions, warnings, plan.EventId, normalizedSourceModId, applyError);
				rolledBack = rollbackOk;

				EventMutationExecutionResult failed = Failed(
					eventId: plan.EventId,
					sourceModId: normalizedSourceModId,
					code: "execute.apply_failed",
					message: $"Event mutation apply failed. {applyError}",
					warnings: warnings,
					rolledBack: rolledBack,
					appliedOptionCount: 0
				);
				EmitDiagnosticsFromResult(failed, isMultiplayer);
				return failed;
			}
		}
		catch (Exception ex)
		{
			bool rollbackOk = TryRollback(model, originalOptions, warnings, plan.EventId, normalizedSourceModId, ex.Message);
			rolledBack = rollbackOk;

			EventMutationExecutionResult failed = Failed(
				eventId: plan.EventId,
				sourceModId: normalizedSourceModId,
				code: "execute.exception",
				message: $"Event mutation execution threw {ex.GetType().Name}: {ex.Message}",
				warnings: warnings,
				rolledBack: rolledBack,
				appliedOptionCount: 0
			);
			EmitDiagnosticsFromResult(failed, isMultiplayer);
			return failed;
		}

		EventWheelResult successResult = new(
			Success: true,
			Code: "execute.applied",
			Message: $"Event mutation plan applied successfully. options={runtimeOptions.Count}, multiplayer={isMultiplayer}.",
			EventId: plan.EventId,
			SourceModId: normalizedSourceModId,
			Details: BuildDetails(networkResult.Report, warnings));

		EventMutationExecutionResult succeeded = new EventMutationExecutionResult(
			Result: successResult,
			Warnings: warnings.ToArray(),
			RolledBack: false,
			AppliedOptionCount: runtimeOptions.Count);

		EmitDiagnosticsFromResult(succeeded, isMultiplayer);
		return succeeded;
	}

	private void EmitDiagnosticsFromResult(EventMutationExecutionResult result, bool isMultiplayer)
	{
		if (_diagnostics == null)
		{
			return;
		}

		_diagnostics.TrackWarnings(EventWheelStage.Execute, result.Warnings);

		string eventId = result.Result.EventId?.Trim() ?? string.Empty;
		if (eventId.Length == 0)
		{
			eventId = "unknown.event";
		}

		string sourceModId = result.Result.SourceModId?.Trim() ?? string.Empty;
		if (sourceModId.Length == 0)
		{
			sourceModId = "reforge.eventwheel";
		}

		EventWheelSeverity severity;
		if (!result.Result.Success)
		{
			severity = EventWheelSeverity.Error;
		}
		else if (result.Warnings.Count > 0)
		{
			severity = EventWheelSeverity.Warning;
		}
		else
		{
			severity = EventWheelSeverity.Info;
		}

		_diagnostics.Track(
			stage: EventWheelStage.Execute,
			severity: severity,
			eventId: eventId,
			sourceModId: sourceModId,
			message: result.Result.Message,
			exceptionSummary: result.Result.Success ? null : result.Result.Message,
			context: new Dictionary<string, string>(StringComparer.Ordinal)
			{
				["code"] = result.Result.Code,
				["success"] = result.Result.Success ? "true" : "false",
				["warnings"] = result.Warnings.Count.ToString(),
				["rolledBack"] = result.RolledBack ? "true" : "false",
				["appliedOptionCount"] = result.AppliedOptionCount.ToString(),
				["isMultiplayer"] = isMultiplayer ? "true" : "false"
			});
	}

	private static bool TryRollback(
		EventModel model,
		IReadOnlyList<EventOption> originalOptions,
		List<EventMutationWarning> warnings,
		string eventId,
		string sourceModId,
		string reason)
	{
		EventModelBridge rollbackBridge = new();
		if (originalOptions.Count == 0)
		{
			warnings.Add(new EventMutationWarning(
				Severity: EventWheelSeverity.Error,
				Code: "execute.rollback_skipped",
				Message: $"Rollback skipped because original snapshot is empty. reason='{reason}'.",
				EventId: eventId,
				SourceModId: sourceModId
			));
			return false;
		}

		if (rollbackBridge.TryApplyCurrentOptions(model, originalOptions, out string rollbackError))
		{
			warnings.Add(new EventMutationWarning(
				Severity: EventWheelSeverity.Warning,
				Code: "execute.rolled_back",
				Message: "Apply failed and options were rolled back to original snapshot.",
				EventId: eventId,
				SourceModId: sourceModId
			));
			return true;
		}

		warnings.Add(new EventMutationWarning(
			Severity: EventWheelSeverity.Error,
			Code: "execute.rollback_failed",
			Message: $"Rollback failed after apply error. rollbackError='{rollbackError}'. originalReason='{reason}'.",
			EventId: eventId,
			SourceModId: sourceModId
		));
		return false;
	}

	private static IReadOnlyList<string> BuildDetails(NetworkOptionGuardReport report, List<EventMutationWarning> warnings)
	{
		List<string> details = new(6)
		{
			$"multiplayer={report.IsMultiplayer}",
			$"maxAllowed={report.MaxAllowedCount}",
			$"input={report.InputCount}",
			$"output={report.OutputCount}",
			$"dedupeRemoved={report.RemovedDuplicateCount}",
			$"trimmed={report.TrimmedCount}"
		};

		if (warnings.Count > 0)
		{
			details.Add($"warnings={warnings.Count}");
		}

		return details;
	}

	private static EventMutationExecutionResult Failed(
		string eventId,
		string sourceModId,
		string code,
		string message,
		IReadOnlyList<EventMutationWarning> warnings,
		bool rolledBack = false,
		int appliedOptionCount = 0)
	{
		EventWheelResult result = new(
			Success: false,
			Code: code,
			Message: message,
			EventId: eventId,
			SourceModId: sourceModId,
			Details: Array.Empty<string>());

		return new EventMutationExecutionResult(
			Result: result,
			Warnings: warnings,
			RolledBack: rolledBack,
			AppliedOptionCount: appliedOptionCount);
	}
}