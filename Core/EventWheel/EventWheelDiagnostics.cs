#nullable enable

using System;
using System.Collections.Generic;
using Godot;
using ReForgeFramework.Api.Events;
using ReForgeFramework.EventBus;

namespace ReForgeFramework.EventWheel;

/// <summary>
/// EventWheel 诊断事件总线标识。
/// </summary>
internal static class EventWheelDiagnosticsEventIds
{
	public const string Recorded = "reforge.eventwheel.diagnostic.recorded";
}

/// <summary>
/// 诊断事件发布载荷。
/// </summary>
internal readonly record struct EventWheelDiagnosticRecordedEvent(
	EventWheelDiagnosticEvent Diagnostic
) : IEventArg;

/// <summary>
/// EventWheel 诊断中心。
/// 支持缓冲、查询、监听分发、Godot 日志输出与事件总线广播。
/// </summary>
internal sealed class EventWheelDiagnostics : IEventWheelDiagnosticsApi
{
	private static readonly EventWheelStage[] StageOrder =
	{
		EventWheelStage.Register,
		EventWheelStage.Plan,
		EventWheelStage.Execute,
		EventWheelStage.Layout
	};

	private readonly object _syncRoot = new();
	private readonly Queue<EventWheelDiagnosticEvent> _events = new();
	private readonly Dictionary<string, Action<EventWheelDiagnosticEvent>> _listeners = new(StringComparer.Ordinal);
	private readonly int _maxDiagnostics;
	private readonly bool _emitGodotLog;
	private readonly bool _publishToEventBus;

	/// <summary>
	/// 创建诊断中心。
	/// </summary>
	public EventWheelDiagnostics(
		int maxDiagnostics = 512,
		bool emitGodotLog = true,
		bool publishToEventBus = true)
	{
		_maxDiagnostics = maxDiagnostics <= 0 ? 1 : maxDiagnostics;
		_emitGodotLog = emitGodotLog;
		_publishToEventBus = publishToEventBus;
	}

	/// <summary>
	/// 记录一条诊断事件。
	/// </summary>
	public void Track(EventWheelDiagnosticEvent diagnosticEvent)
	{
		EventWheelDiagnosticEvent normalized = NormalizeDiagnostic(diagnosticEvent);

		Action<EventWheelDiagnosticEvent>[] listeners;
		lock (_syncRoot)
		{
			_events.Enqueue(normalized);
			while (_events.Count > _maxDiagnostics)
			{
				_events.Dequeue();
			}

			listeners = SnapshotListenersUnsafe();
		}

		for (int i = 0; i < listeners.Length; i++)
		{
			try
			{
				listeners[i](normalized);
			}
			catch
			{
				// 监听器异常不能影响主流程。
			}
		}

		if (_emitGodotLog)
		{
			PrintDiagnostic(normalized);
		}

		if (_publishToEventBus)
		{
			try
			{
				ReForge.EventBus.Publish(
					EventWheelDiagnosticsEventIds.Recorded,
					new EventWheelDiagnosticRecordedEvent(normalized));
			}
			catch (Exception ex)
			{
				if (_emitGodotLog)
				{
					GD.PrintErr($"[ReForge.EventWheel] Failed to publish diagnostics event bus message. {ex.GetType().Name}: {ex.Message}");
				}
			}
		}
	}

	/// <summary>
	/// 按参数构建并记录诊断事件。
	/// </summary>
	public void Track(
		EventWheelStage stage,
		EventWheelSeverity severity,
		string eventId,
		string sourceModId,
		string message,
		string? exceptionSummary = null,
		IReadOnlyDictionary<string, string>? context = null)
	{
		Track(new EventWheelDiagnosticEvent(
			TimestampUtc: DateTimeOffset.UtcNow,
			Stage: stage,
			Severity: severity,
			EventId: eventId,
			SourceModId: sourceModId,
			Message: message,
			ExceptionSummary: exceptionSummary,
			Context: context));
	}

	/// <summary>
	/// 查询诊断明细。
	/// </summary>
	public IReadOnlyList<EventWheelDiagnosticEvent> Query(EventWheelDiagnosticQuery? query = null)
	{
		return BuildSnapshot(query).Events;
	}

	/// <summary>
	/// 获取诊断汇总。
	/// </summary>
	public EventWheelDiagnosticsSummary GetSummary(EventWheelDiagnosticQuery? query = null)
	{
		return BuildSnapshot(query).Summary;
	}

	/// <summary>
	/// 构建诊断快照（明细与汇总）。
	/// </summary>
	public EventWheelDiagnosticsSnapshot BuildSnapshot(EventWheelDiagnosticQuery? query = null)
	{
		query ??= new EventWheelDiagnosticQuery();
		int limit = query.Limit <= 0 ? 100 : query.Limit;

		EventWheelDiagnosticEvent[] snapshot;
		lock (_syncRoot)
		{
			snapshot = _events.ToArray();
		}

		if (snapshot.Length == 0)
		{
			return new EventWheelDiagnosticsSnapshot(
				Events: Array.Empty<EventWheelDiagnosticEvent>(),
				Summary: BuildSummary(
					totalCount: 0,
					infoCount: 0,
					warningCount: 0,
					errorCount: 0,
					stageAccumulators: CreateEmptyAccumulators()));
		}

		List<EventWheelDiagnosticEvent> filtered = new(capacity: Math.Min(limit, snapshot.Length));
		StageAccumulator[] stageAccumulators = CreateEmptyAccumulators();
		int totalCount = 0;
		int infoCount = 0;
		int warningCount = 0;
		int errorCount = 0;

		for (int i = snapshot.Length - 1; i >= 0; i--)
		{
			if (filtered.Count >= limit)
			{
				break;
			}

			EventWheelDiagnosticEvent diagnostic = snapshot[i];
			if (!MatchesQuery(diagnostic, query))
			{
				continue;
			}

			filtered.Add(diagnostic);
			totalCount++;

			switch (diagnostic.Severity)
			{
				case EventWheelSeverity.Info:
					infoCount++;
					break;
				case EventWheelSeverity.Warning:
					warningCount++;
					break;
				case EventWheelSeverity.Error:
					errorCount++;
					break;
			}

			int stageIndex = ToStageIndex(diagnostic.Stage);
			if (stageIndex >= 0)
			{
				StageAccumulator accumulator = stageAccumulators[stageIndex];
				accumulator.TotalCount++;
				switch (diagnostic.Severity)
				{
					case EventWheelSeverity.Info:
						accumulator.InfoCount++;
						break;
					case EventWheelSeverity.Warning:
						accumulator.WarningCount++;
						break;
					case EventWheelSeverity.Error:
						accumulator.ErrorCount++;
						break;
				}

				if (!accumulator.HasLast || diagnostic.TimestampUtc >= accumulator.LastTimestampUtc)
				{
					accumulator.LastTimestampUtc = diagnostic.TimestampUtc;
					accumulator.LastMessage = diagnostic.Message;
					accumulator.HasLast = true;
				}

				stageAccumulators[stageIndex] = accumulator;
			}
		}

		filtered.Reverse();
		return new EventWheelDiagnosticsSnapshot(
			Events: filtered,
			Summary: BuildSummary(totalCount, infoCount, warningCount, errorCount, stageAccumulators));
	}

	/// <summary>
	/// 注册诊断监听器。
	/// </summary>
	public bool RegisterListener(string busId, Action<EventWheelDiagnosticEvent> listener)
	{
		ArgumentNullException.ThrowIfNull(listener);
		if (!TryNormalizeRequiredKey(busId, out string normalizedBusId))
		{
			return false;
		}

		lock (_syncRoot)
		{
			bool replaced = _listeners.ContainsKey(normalizedBusId);
			_listeners[normalizedBusId] = listener;
			return replaced;
		}
	}

	/// <summary>
	/// 注销诊断监听器。
	/// </summary>
	public int UnregisterListener(string busId)
	{
		if (!TryNormalizeRequiredKey(busId, out string normalizedBusId))
		{
			return 0;
		}

		lock (_syncRoot)
		{
			return _listeners.Remove(normalizedBusId) ? 1 : 0;
		}
	}

	internal void TrackWarnings(EventWheelStage stage, IReadOnlyList<EventMutationWarning> warnings)
	{
		if (warnings == null || warnings.Count == 0)
		{
			return;
		}

		for (int i = 0; i < warnings.Count; i++)
		{
			EventMutationWarning warning = warnings[i];
			Track(
				stage: stage,
				severity: warning.Severity,
				eventId: warning.EventId,
				sourceModId: warning.SourceModId,
				message: warning.Message,
				exceptionSummary: null,
				context: BuildWarningContext(warning));
		}
	}

	private static EventWheelDiagnosticsSummary BuildSummary(
		int totalCount,
		int infoCount,
		int warningCount,
		int errorCount,
		StageAccumulator[] stageAccumulators)
	{
		EventWheelStageDiagnosticStat[] stageStats = new EventWheelStageDiagnosticStat[StageOrder.Length];
		for (int i = 0; i < StageOrder.Length; i++)
		{
			StageAccumulator accumulator = stageAccumulators[i];
			stageStats[i] = new EventWheelStageDiagnosticStat(
				Stage: StageOrder[i],
				TotalCount: accumulator.TotalCount,
				InfoCount: accumulator.InfoCount,
				WarningCount: accumulator.WarningCount,
				ErrorCount: accumulator.ErrorCount,
				LastTimestampUtc: accumulator.HasLast ? accumulator.LastTimestampUtc : null,
				LastMessage: accumulator.HasLast ? accumulator.LastMessage : null);
		}

		return new EventWheelDiagnosticsSummary(
			TotalCount: totalCount,
			InfoCount: infoCount,
			WarningCount: warningCount,
			ErrorCount: errorCount,
			StageStats: stageStats);
	}

	private static bool MatchesQuery(EventWheelDiagnosticEvent diagnostic, EventWheelDiagnosticQuery query)
	{
		if (query.Stage.HasValue && diagnostic.Stage != query.Stage.Value)
		{
			return false;
		}

		if (query.MinSeverity.HasValue && (int)diagnostic.Severity < (int)query.MinSeverity.Value)
		{
			return false;
		}

		if (!string.IsNullOrWhiteSpace(query.EventId)
			&& !StringComparer.Ordinal.Equals(diagnostic.EventId, query.EventId))
		{
			return false;
		}

		if (!string.IsNullOrWhiteSpace(query.SourceModId)
			&& !StringComparer.Ordinal.Equals(diagnostic.SourceModId, query.SourceModId))
		{
			return false;
		}

		return true;
	}

	private Action<EventWheelDiagnosticEvent>[] SnapshotListenersUnsafe()
	{
		if (_listeners.Count == 0)
		{
			return Array.Empty<Action<EventWheelDiagnosticEvent>>();
		}

		Action<EventWheelDiagnosticEvent>[] snapshot = new Action<EventWheelDiagnosticEvent>[_listeners.Count];
		int index = 0;
		foreach ((_, Action<EventWheelDiagnosticEvent> listener) in _listeners)
		{
			snapshot[index++] = listener;
		}

		return snapshot;
	}

	private static EventWheelDiagnosticEvent NormalizeDiagnostic(EventWheelDiagnosticEvent diagnostic)
	{
		string eventId = diagnostic.EventId?.Trim() ?? string.Empty;
		if (eventId.Length == 0)
		{
			eventId = "unknown.event";
		}

		string sourceModId = diagnostic.SourceModId?.Trim() ?? string.Empty;
		if (sourceModId.Length == 0)
		{
			sourceModId = "unknown.mod";
		}

		string message = diagnostic.Message?.Trim() ?? string.Empty;
		if (message.Length == 0)
		{
			message = "No diagnostic message.";
		}

		DateTimeOffset timestamp = diagnostic.TimestampUtc == default
			? DateTimeOffset.UtcNow
			: diagnostic.TimestampUtc;

		return new EventWheelDiagnosticEvent(
			TimestampUtc: timestamp,
			Stage: diagnostic.Stage,
			Severity: diagnostic.Severity,
			EventId: eventId,
			SourceModId: sourceModId,
			Message: message,
			ExceptionSummary: diagnostic.ExceptionSummary,
			Context: diagnostic.Context);
	}

	private void PrintDiagnostic(EventWheelDiagnosticEvent diagnostic)
	{
		string summary = diagnostic.ExceptionSummary?.Trim() ?? string.Empty;
		string line = $"[ReForge.EventWheel] [{diagnostic.Stage}] [{diagnostic.Severity}] {diagnostic.SourceModId}/{diagnostic.EventId}: {diagnostic.Message}";
		if (summary.Length > 0)
		{
			line = line + $" | {summary}";
		}

		if (diagnostic.Severity == EventWheelSeverity.Error)
		{
			GD.PrintErr(line);
			return;
		}

		GD.Print(line);
	}

	private static int ToStageIndex(EventWheelStage stage)
	{
		return stage switch
		{
			EventWheelStage.Register => 0,
			EventWheelStage.Plan => 1,
			EventWheelStage.Execute => 2,
			EventWheelStage.Layout => 3,
			_ => -1
		};
	}

	private static StageAccumulator[] CreateEmptyAccumulators()
	{
		return new StageAccumulator[StageOrder.Length];
	}

	private static IReadOnlyDictionary<string, string> BuildWarningContext(EventMutationWarning warning)
	{
		Dictionary<string, string> context = new(StringComparer.Ordinal)
		{
			["code"] = warning.Code,
			["severity"] = warning.Severity.ToString()
		};

		string ruleId = warning.RuleId?.Trim() ?? string.Empty;
		if (ruleId.Length > 0)
		{
			context["ruleId"] = ruleId;
		}

		string targetOptionKey = warning.TargetOptionKey?.Trim() ?? string.Empty;
		if (targetOptionKey.Length > 0)
		{
			context["targetOptionKey"] = targetOptionKey;
		}

		return context;
	}

	private static bool TryNormalizeRequiredKey(string value, out string normalized)
	{
		normalized = value?.Trim() ?? string.Empty;
		return normalized.Length > 0;
	}

	private struct StageAccumulator
	{
		public int TotalCount;
		public int InfoCount;
		public int WarningCount;
		public int ErrorCount;
		public DateTimeOffset LastTimestampUtc;
		public string? LastMessage;
		public bool HasLast;
	}
}
