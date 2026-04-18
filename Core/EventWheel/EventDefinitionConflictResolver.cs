#nullable enable

using System;
using ReForgeFramework.Api.Events;

namespace ReForgeFramework.EventWheel;

/// <summary>
/// 事件定义冲突策略。
/// </summary>
internal enum EventDefinitionConflictPolicy
{
	ReplaceExisting = 0,
	KeepExisting = 1,
	PreferHigherPriority = 2,
	RejectIncoming = 3
}

/// <summary>
/// 事件变更规则冲突策略。
/// </summary>
internal enum EventMutationConflictPolicy
{
	ReplaceExisting = 0,
	KeepExisting = 1,
	PreferHigherOrder = 2,
	RejectIncoming = 3
}

/// <summary>
/// 冲突解析结果。
/// </summary>
internal sealed record EventDefinitionConflictResolution(
	bool AcceptIncoming,
	bool IsConflict,
	EventWheelSeverity Severity,
	string Code,
	string Message
);

/// <summary>
/// 事件定义与规则冲突解析器。
/// 用于在同一 eventId 或 ruleId 重复注册时给出确定性处理结果。
/// </summary>
internal sealed class EventDefinitionConflictResolver
{
	/// <summary>
	/// 解析事件定义冲突。
	/// </summary>
	/// <param name="existing">已存在定义。</param>
	/// <param name="incoming">新注册定义。</param>
	/// <param name="policy">冲突策略。</param>
	/// <returns>冲突决议结果。</returns>
	public EventDefinitionConflictResolution ResolveDefinition(
		IEventDefinition existing,
		IEventDefinition incoming,
		EventDefinitionConflictPolicy policy)
	{
		if (policy == EventDefinitionConflictPolicy.ReplaceExisting)
		{
			return new EventDefinitionConflictResolution(
				AcceptIncoming: true,
				IsConflict: true,
				Severity: EventWheelSeverity.Warning,
				Code: "definition.replaced",
				Message: $"Definition replaced by policy. eventId='{incoming.EventId}', incoming='{incoming.SourceModId}', existing='{existing.SourceModId}'."
			);
		}

		if (policy == EventDefinitionConflictPolicy.KeepExisting)
		{
			return new EventDefinitionConflictResolution(
				AcceptIncoming: false,
				IsConflict: true,
				Severity: EventWheelSeverity.Warning,
				Code: "definition.ignored",
				Message: $"Definition kept existing by policy. eventId='{incoming.EventId}', incoming='{incoming.SourceModId}', existing='{existing.SourceModId}'."
			);
		}

		if (policy == EventDefinitionConflictPolicy.RejectIncoming)
		{
			return new EventDefinitionConflictResolution(
				AcceptIncoming: false,
				IsConflict: true,
				Severity: EventWheelSeverity.Error,
				Code: "definition.rejected",
				Message: $"Definition conflict rejected by policy. eventId='{incoming.EventId}', incoming='{incoming.SourceModId}', existing='{existing.SourceModId}'."
			);
		}

		int compare = CompareDefinition(incoming, existing);
		bool sameSource = StringComparer.Ordinal.Equals(incoming.SourceModId, existing.SourceModId);
		bool acceptIncoming = compare > 0 || (compare == 0 && sameSource);
		return new EventDefinitionConflictResolution(
			AcceptIncoming: acceptIncoming,
			IsConflict: true,
			Severity: EventWheelSeverity.Warning,
			Code: acceptIncoming ? "definition.replaced" : "definition.ignored",
			Message: acceptIncoming
				? $"Definition conflict resolved to incoming. eventId='{incoming.EventId}', incomingPriority={incoming.Priority}, existingPriority={existing.Priority}."
				: $"Definition conflict resolved to existing. eventId='{incoming.EventId}', incomingPriority={incoming.Priority}, existingPriority={existing.Priority}."
		);
	}

	/// <summary>
	/// 解析事件变更规则冲突。
	/// </summary>
	/// <param name="existing">已存在规则。</param>
	/// <param name="incoming">新注册规则。</param>
	/// <param name="policy">冲突策略。</param>
	/// <returns>冲突决议结果。</returns>
	public EventDefinitionConflictResolution ResolveMutationRule(
		IEventMutationRule existing,
		IEventMutationRule incoming,
		EventMutationConflictPolicy policy)
	{
		if (policy == EventMutationConflictPolicy.ReplaceExisting)
		{
			return new EventDefinitionConflictResolution(
				AcceptIncoming: true,
				IsConflict: true,
				Severity: EventWheelSeverity.Warning,
				Code: "rule.replaced",
				Message: $"Mutation rule replaced by policy. eventId='{incoming.EventId}', ruleId='{incoming.RuleId}'."
			);
		}

		if (policy == EventMutationConflictPolicy.KeepExisting)
		{
			return new EventDefinitionConflictResolution(
				AcceptIncoming: false,
				IsConflict: true,
				Severity: EventWheelSeverity.Warning,
				Code: "rule.ignored",
				Message: $"Mutation rule kept existing by policy. eventId='{incoming.EventId}', ruleId='{incoming.RuleId}'."
			);
		}

		if (policy == EventMutationConflictPolicy.RejectIncoming)
		{
			return new EventDefinitionConflictResolution(
				AcceptIncoming: false,
				IsConflict: true,
				Severity: EventWheelSeverity.Error,
				Code: "rule.rejected",
				Message: $"Mutation rule conflict rejected by policy. eventId='{incoming.EventId}', ruleId='{incoming.RuleId}'."
			);
		}

		int compare = CompareRule(incoming, existing);
		bool sameSource = StringComparer.Ordinal.Equals(incoming.SourceModId, existing.SourceModId);
		bool acceptIncoming = compare > 0 || (compare == 0 && sameSource);
		return new EventDefinitionConflictResolution(
			AcceptIncoming: acceptIncoming,
			IsConflict: true,
			Severity: EventWheelSeverity.Warning,
			Code: acceptIncoming ? "rule.replaced" : "rule.ignored",
			Message: acceptIncoming
				? $"Mutation rule conflict resolved to incoming. eventId='{incoming.EventId}', ruleId='{incoming.RuleId}', incomingOrder={incoming.Order}, existingOrder={existing.Order}."
				: $"Mutation rule conflict resolved to existing. eventId='{incoming.EventId}', ruleId='{incoming.RuleId}', incomingOrder={incoming.Order}, existingOrder={existing.Order}."
		);
	}

	private static int CompareDefinition(IEventDefinition incoming, IEventDefinition existing)
	{
		int priorityCompare = incoming.Priority.CompareTo(existing.Priority);
		if (priorityCompare != 0)
		{
			return priorityCompare;
		}

		int sourceCompare = StringComparer.Ordinal.Compare(incoming.SourceModId, existing.SourceModId);
		if (sourceCompare != 0)
		{
			return sourceCompare;
		}

		int kindCompare = ((int)incoming.Kind).CompareTo((int)existing.Kind);
		if (kindCompare != 0)
		{
			return kindCompare;
		}

		return StringComparer.Ordinal.Compare(incoming.EventId, existing.EventId);
	}

	private static int CompareRule(IEventMutationRule incoming, IEventMutationRule existing)
	{
		int orderCompare = incoming.Order.CompareTo(existing.Order);
		if (orderCompare != 0)
		{
			return orderCompare;
		}

		int sourceCompare = StringComparer.Ordinal.Compare(incoming.SourceModId, existing.SourceModId);
		if (sourceCompare != 0)
		{
			return sourceCompare;
		}

		return StringComparer.Ordinal.Compare(incoming.RuleId, existing.RuleId);
	}
}
