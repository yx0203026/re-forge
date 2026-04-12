#nullable enable

using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Models;

namespace ReForgeFramework.Api.Events;

/// <summary>
/// 事件类型：普通事件或远古事件。
/// </summary>
public enum EventKind
{
	Normal = 0,
	Ancient = 1
}

/// <summary>
/// 事件选项变更操作。
/// </summary>
public enum EventMutationOperation
{
	Add = 0,
	Replace = 1,
	InsertBefore = 2,
	InsertAfter = 3,
	Lock = 4,
	Remove = 5
}

/// <summary>
/// 事件轮子内部阶段。
/// </summary>
public enum EventWheelStage
{
	Register = 0,
	Plan = 1,
	Execute = 2,
	Layout = 3
}

/// <summary>
/// 诊断严重级别。
/// </summary>
public enum EventWheelSeverity
{
	Info = 0,
	Warning = 1,
	Error = 2
}

/// <summary>
/// 事件定义契约。
/// </summary>
public interface IEventDefinition
{
	string EventId { get; }

	EventKind Kind { get; }

	bool IsApplicable(EventModel? eventModel);

	string SourceModId { get; }

	int Priority { get; }

	IReadOnlyList<IEventOptionDefinition> InitialOptions { get; }

	IReadOnlyList<IEventMutationRule> MutationRules { get; }

	IReadOnlyDictionary<string, string> Metadata { get; }
}

/// <summary>
/// 事件选项定义契约。
/// </summary>
public interface IEventOptionDefinition
{
	string OptionKey { get; }

	string? ActionKey { get; }

	string TitleKey { get; }

	string DescriptionKey { get; }

	int Order { get; }

	bool IsLocked { get; }

	bool IsProceed { get; }

	IReadOnlyList<string> TagKeys { get; }
}

/// <summary>
/// 事件池条目契约，用于把定义或选项放入同一个随机池中。
/// </summary>
public interface IEventPoolEntry
{
	string PoolId { get; }

	int Weight { get; }
}

/// <summary>
/// 事件池定义契约。
/// </summary>
public interface IEventPoolDefinition : IEventDefinition, IEventPoolEntry
{
}

/// <summary>
/// 事件选项池条目契约。
/// </summary>
public interface IEventOptionPoolEntry : IEventOptionDefinition, IEventPoolEntry
{
}

/// <summary>
/// 事件选项变更规则契约。
/// </summary>
public interface IEventMutationRule
{
	string RuleId { get; }

	string EventId { get; }

	string SourceModId { get; }

	EventMutationOperation Operation { get; }

	bool IsApplicable(EventModel? eventModel);

	string? TargetOptionKey { get; }

	IEventOptionDefinition? Option { get; }

	int Order { get; }

	bool StopOnFailure { get; }
}

/// <summary>
/// 通用执行结果。
/// </summary>
public sealed record EventWheelResult(
	bool Success,
	string Code,
	string Message,
	string EventId,
	string SourceModId,
	IReadOnlyList<string>? Details = null
);

/// <summary>
/// 事件注册结果。
/// </summary>
public sealed record EventRegistrationResult(
	bool Success,
	string EventId,
	string SourceModId,
	bool Replaced,
	string Message
);

/// <summary>
/// 事件轮子诊断事件。
/// </summary>
public sealed record EventWheelDiagnosticEvent(
	DateTimeOffset TimestampUtc,
	EventWheelStage Stage,
	EventWheelSeverity Severity,
	string EventId,
	string SourceModId,
	string Message,
	string? ExceptionSummary = null,
	IReadOnlyDictionary<string, string>? Context = null
);

/// <summary>
/// 事件轮子诊断查询条件。
/// </summary>
public sealed record EventWheelDiagnosticQuery(
	EventWheelStage? Stage = null,
	EventWheelSeverity? MinSeverity = null,
	string? EventId = null,
	string? SourceModId = null,
	int Limit = 100
);

/// <summary>
/// 事件轮子单阶段诊断统计。
/// </summary>
public sealed record EventWheelStageDiagnosticStat(
	EventWheelStage Stage,
	int TotalCount,
	int InfoCount,
	int WarningCount,
	int ErrorCount,
	DateTimeOffset? LastTimestampUtc,
	string? LastMessage
);

/// <summary>
/// 事件轮子诊断统计汇总。
/// </summary>
public sealed record EventWheelDiagnosticsSummary(
	int TotalCount,
	int InfoCount,
	int WarningCount,
	int ErrorCount,
	IReadOnlyList<EventWheelStageDiagnosticStat> StageStats
);

/// <summary>
/// 事件轮子诊断快照。
/// </summary>
public sealed record EventWheelDiagnosticsSnapshot(
	IReadOnlyList<EventWheelDiagnosticEvent> Events,
	EventWheelDiagnosticsSummary Summary
);