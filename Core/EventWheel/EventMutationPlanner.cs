#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using MegaCrit.Sts2.Core.Models;
using ReForgeFramework.Api.Events;

namespace ReForgeFramework.EventWheel;

/// <summary>
/// 事件变更规划器。
/// 负责按规则优先级与顺序构建稳定、可执行的选项计划。
/// </summary>
internal sealed class EventMutationPlanner
{
	private readonly EventWheelDiagnostics? _diagnostics;

	/// <summary>
	/// 创建规划器。
	/// </summary>
	public EventMutationPlanner(EventWheelDiagnostics? diagnostics = null)
	{
		_diagnostics = diagnostics;
	}

	/// <summary>
	/// 生成事件变更计划。
	/// </summary>
	/// <param name="eventModel">事件模型上下文。</param>
	/// <param name="definition">事件定义。</param>
	/// <param name="rules">候选变更规则集合。</param>
	/// <param name="sourcePriorityByModId">可选来源优先级映射。</param>
	/// <returns>包含步骤、最终选项与告警的规划结果。</returns>
	public EventMutationPlan BuildPlan(
		EventModel? eventModel,
		IEventDefinition? definition,
		IEnumerable<IEventMutationRule>? rules,
		IReadOnlyDictionary<string, int>? sourcePriorityByModId = null)
	{
		string eventId = definition?.EventId?.Trim() ?? string.Empty;
		EventKind kind = definition?.Kind ?? EventKind.Normal;

		List<EventMutationWarning> warnings = new();
		List<EventMutationStep> steps = new();
		List<EventMutationPlannedOption> plannedOptions = BuildInitialOptionSet(definition, warnings);
		List<OrderedRule> orderedRules = OrderRules(eventModel, definition, rules, sourcePriorityByModId, warnings);

		for (int i = 0; i < orderedRules.Count; i++)
		{
			OrderedRule orderedRule = orderedRules[i];
			ApplyRule(
				eventId,
				sequence: i + 1,
				orderedRule,
				plannedOptions,
				steps,
				warnings
			);
		}

		List<EventMutationPlannedOption> pooledResolved = ResolvePools(eventModel, definition, plannedOptions, warnings);
		pooledResolved.Sort(static (left, right) => CompareOption(left, right));

		HashSet<string> dedupe = new(StringComparer.Ordinal);
		List<EventMutationPlannedOption> stableOptions = new(pooledResolved.Count);
		for (int i = 0; i < pooledResolved.Count; i++)
		{
			EventMutationPlannedOption option = pooledResolved[i];
			if (!dedupe.Add(option.OptionKey))
			{
				warnings.Add(new EventMutationWarning(
					Severity: EventWheelSeverity.Warning,
					Code: "plan.final_option_duplicate",
					Message: $"Duplicate optionKey detected after pool resolution and ignored. optionKey='{option.OptionKey}'.",
					EventId: eventId,
					SourceModId: definition?.SourceModId ?? string.Empty
				));
				continue;
			}

			stableOptions.Add(option);
		}

		EventMutationPlan plan = new EventMutationPlan(
			EventId: eventId,
			Kind: kind,
			Steps: steps.ToArray(),
			PlannedOptions: stableOptions.ToArray(),
			Warnings: warnings.ToArray()
		);

		PublishPlanDiagnostics(plan, definition);
		return plan;
	}

	private void PublishPlanDiagnostics(EventMutationPlan plan, IEventDefinition? definition)
	{
		if (_diagnostics == null)
		{
			return;
		}

		_diagnostics.TrackWarnings(EventWheelStage.Plan, plan.Warnings);

		int failedSteps = 0;
		for (int i = 0; i < plan.Steps.Count; i++)
		{
			if (!plan.Steps[i].Applied)
			{
				failedSteps++;
			}
		}

		string sourceModId = definition?.SourceModId?.Trim() ?? string.Empty;
		if (sourceModId.Length == 0)
		{
			sourceModId = "reforge.eventwheel";
		}

		EventWheelSeverity severity = failedSteps > 0 || plan.Warnings.Count > 0
			? EventWheelSeverity.Warning
			: EventWheelSeverity.Info;

		_diagnostics.Track(
			stage: EventWheelStage.Plan,
			severity: severity,
			eventId: plan.EventId,
			sourceModId: sourceModId,
			message: $"Mutation plan built. steps={plan.Steps.Count}, plannedOptions={plan.PlannedOptions.Count}, warnings={plan.Warnings.Count}, failedSteps={failedSteps}.",
			exceptionSummary: null,
			context: new Dictionary<string, string>(StringComparer.Ordinal)
			{
				["steps"] = plan.Steps.Count.ToString(),
				["plannedOptions"] = plan.PlannedOptions.Count.ToString(),
				["warnings"] = plan.Warnings.Count.ToString(),
				["failedSteps"] = failedSteps.ToString()
			});
	}

	private static List<EventMutationPlannedOption> BuildInitialOptionSet(
		IEventDefinition? definition,
		List<EventMutationWarning> warnings)
	{
		if (definition == null)
		{
			warnings.Add(new EventMutationWarning(
				Severity: EventWheelSeverity.Error,
				Code: "plan.definition_missing",
				Message: "Cannot build mutation plan: event definition is null.",
				EventId: string.Empty,
				SourceModId: string.Empty
			));
			return new List<EventMutationPlannedOption>(capacity: 0);
		}

		if (definition.InitialOptions == null || definition.InitialOptions.Count == 0)
		{
			warnings.Add(new EventMutationWarning(
				Severity: EventWheelSeverity.Warning,
				Code: "plan.initial_options_empty",
				Message: "Event definition initial options are empty; plan starts from empty option set.",
				EventId: definition.EventId,
				SourceModId: definition.SourceModId
			));
			return new List<EventMutationPlannedOption>(capacity: 0);
		}

		List<EventMutationPlannedOption> normalized = new(definition.InitialOptions.Count);
		for (int i = 0; i < definition.InitialOptions.Count; i++)
		{
			if (!EventMutationPlannedOption.TryCreateFromDefinition(definition.InitialOptions[i], out EventMutationPlannedOption? option, out string message)
				|| option == null)
			{
				warnings.Add(new EventMutationWarning(
					Severity: EventWheelSeverity.Warning,
					Code: "plan.initial_option_invalid",
					Message: $"Ignored invalid initial option at index {i}. {message}",
					EventId: definition.EventId,
					SourceModId: definition.SourceModId
				));
				continue;
			}

			normalized.Add(option);
		}

		normalized.Sort(static (left, right) => CompareOption(left, right));

		// 只做初始规范化和去重，池化随机选择放到全部规则执行完成之后。
		HashSet<string> dedupe = new(StringComparer.Ordinal);
		List<EventMutationPlannedOption> stable = new(normalized.Count);
		for (int i = 0; i < normalized.Count; i++)
		{
			EventMutationPlannedOption option = normalized[i];
			if (!dedupe.Add(option.OptionKey))
			{
				warnings.Add(new EventMutationWarning(
					Severity: EventWheelSeverity.Warning,
					Code: "plan.initial_option_duplicate",
					Message: $"Duplicate initial optionKey detected and ignored. optionKey='{option.OptionKey}'.",
					EventId: definition.EventId,
					SourceModId: definition.SourceModId
				));
				continue;
			}

			stable.Add(option);
		}

		return stable;
	}

	private static List<EventMutationPlannedOption> ResolvePools(
		EventModel? eventModel,
		IEventDefinition? definition,
		IReadOnlyList<EventMutationPlannedOption> options,
		List<EventMutationWarning> warnings)
	{
		if (options.Count == 0)
		{
			return new List<EventMutationPlannedOption>(capacity: 0);
		}

		Dictionary<string, List<EventMutationPlannedOption>> groupedByPool = new(StringComparer.Ordinal);
		List<EventMutationPlannedOption> fixedOptions = new(options.Count);
		for (int i = 0; i < options.Count; i++)
		{
			EventMutationPlannedOption option = options[i];
			if (string.IsNullOrWhiteSpace(option.PoolId))
			{
				fixedOptions.Add(option);
				continue;
			}

			if (!groupedByPool.TryGetValue(option.PoolId, out List<EventMutationPlannedOption>? poolOptions))
			{
				poolOptions = new List<EventMutationPlannedOption>();
				groupedByPool[option.PoolId] = poolOptions;
			}

			poolOptions.Add(option);
		}

		if (groupedByPool.Count == 0)
		{
			return fixedOptions;
		}

		List<EventMutationPlannedOption> resolved = new(fixedOptions.Count + groupedByPool.Count);
		resolved.AddRange(fixedOptions);

		string eventSeed = BuildEventSeed(eventModel, definition);
		foreach ((string poolId, List<EventMutationPlannedOption> poolOptions) in groupedByPool)
		{
			if (poolOptions.Count == 0)
			{
				continue;
			}

			if (poolOptions.Count == 1)
			{
				resolved.Add(poolOptions[0]);
				continue;
			}

			int chosenIndex = EventWheelPoolSelection.SelectWeightedIndex(
				poolOptions,
				EventWheelPoolSelection.BuildSeed(eventSeed, poolId),
				static option => option.Weight);

			if (chosenIndex < 0 || chosenIndex >= poolOptions.Count)
			{
				warnings.Add(new EventMutationWarning(
					Severity: EventWheelSeverity.Warning,
					Code: "plan.pool_selection_failed",
					Message: $"Pool selection failed and fell back to the first candidate. poolId='{poolId}'.",
					EventId: definition?.EventId ?? string.Empty,
					SourceModId: definition?.SourceModId ?? string.Empty
				));
				resolved.Add(poolOptions[0]);
				continue;
			}

			resolved.Add(poolOptions[chosenIndex]);
		}

		return resolved;
	}

	private static string BuildEventSeed(EventModel? eventModel, IEventDefinition? definition)
	{
		string eventId = definition?.EventId?.Trim() ?? string.Empty;
		string sourceModId = definition?.SourceModId?.Trim() ?? string.Empty;
		string modelId = string.Empty;

		if (eventModel != null)
		{
			try
			{
				modelId = eventModel.Id.Entry?.Trim() ?? string.Empty;
			}
			catch
			{
				try
				{
					modelId = eventModel.Id.ToString()?.Trim() ?? string.Empty;
				}
				catch
				{
					modelId = eventModel.GetType().FullName ?? eventModel.GetType().Name;
				}
			}
		}

		return EventWheelPoolSelection.BuildSeed(eventId, sourceModId, modelId);
	}

	private static List<OrderedRule> OrderRules(
		EventModel? eventModel,
		IEventDefinition? definition,
		IEnumerable<IEventMutationRule>? rules,
		IReadOnlyDictionary<string, int>? sourcePriorityByModId,
		List<EventMutationWarning> warnings)
	{
		List<OrderedRule> ordered = new();
		if (rules == null)
		{
			return ordered;
		}

		string expectedEventId = definition?.EventId?.Trim() ?? string.Empty;
		foreach (IEventMutationRule rule in rules)
		{
			if (rule == null)
			{
				warnings.Add(new EventMutationWarning(
					Severity: EventWheelSeverity.Warning,
					Code: "plan.rule_null",
					Message: "Ignored null mutation rule.",
					EventId: expectedEventId,
					SourceModId: string.Empty
				));
				continue;
			}

			if (!rule.IsApplicable(eventModel))
			{
				continue;
			}

			string ruleId = rule.RuleId?.Trim() ?? string.Empty;
			string eventId = rule.EventId?.Trim() ?? string.Empty;
			string sourceModId = rule.SourceModId?.Trim() ?? string.Empty;

			if (ruleId.Length == 0 || eventId.Length == 0 || sourceModId.Length == 0)
			{
				warnings.Add(new EventMutationWarning(
					Severity: EventWheelSeverity.Warning,
					Code: "plan.rule_invalid_identity",
					Message: "Ignored mutation rule with missing ruleId/eventId/sourceModId.",
					EventId: eventId,
					SourceModId: sourceModId,
					RuleId: ruleId
				));
				continue;
			}

			if (expectedEventId.Length > 0 && !StringComparer.Ordinal.Equals(eventId, expectedEventId))
			{
				warnings.Add(new EventMutationWarning(
					Severity: EventWheelSeverity.Warning,
					Code: "plan.rule_event_mismatch",
					Message: $"Ignored mutation rule because eventId does not match target definition. expected='{expectedEventId}', actual='{eventId}'.",
					EventId: expectedEventId,
					SourceModId: sourceModId,
					RuleId: ruleId
				));
				continue;
			}

			int sourcePriority = 0;
			if (sourcePriorityByModId != null
				&& sourcePriorityByModId.TryGetValue(sourceModId, out int mappedPriority))
			{
				sourcePriority = mappedPriority;
			}

			ordered.Add(new OrderedRule(rule, sourcePriority));
		}

		ordered.Sort(static (left, right) => CompareOrderedRule(left, right));
		return ordered;
	}

	private static void ApplyRule(
		string eventId,
		int sequence,
		OrderedRule orderedRule,
		List<EventMutationPlannedOption> plannedOptions,
		List<EventMutationStep> steps,
		List<EventMutationWarning> warnings)
	{
		IEventMutationRule rule = orderedRule.Rule;
		string sourceModId = rule.SourceModId?.Trim() ?? string.Empty;
		string ruleId = rule.RuleId?.Trim() ?? string.Empty;

		bool applied;
		string code;
		string message;
		string? optionKey = null;
		string? targetOptionKey = rule.TargetOptionKey?.Trim();

		switch (rule.Operation)
		{
			case EventMutationOperation.Add:
			{
				if (!TryCreatePayloadOption(eventId, rule, warnings, out EventMutationPlannedOption? payload))
				{
					applied = false;
					code = "plan.rule_invalid_payload";
					message = "Add operation skipped: option payload is invalid.";
					break;
				}

				optionKey = payload.OptionKey;
				if (FindOptionIndex(plannedOptions, payload.OptionKey) >= 0)
				{
					applied = false;
					code = "plan.rule_duplicate_option_key";
					message = $"Add operation skipped: optionKey already exists. optionKey='{payload.OptionKey}'.";
					warnings.Add(new EventMutationWarning(
						Severity: EventWheelSeverity.Warning,
						Code: code,
						Message: message,
						EventId: eventId,
						SourceModId: sourceModId,
						RuleId: ruleId,
						TargetOptionKey: payload.OptionKey
					));
					break;
				}

				plannedOptions.Add(payload);
				applied = true;
				code = "plan.rule_applied";
				message = $"Add operation applied. optionKey='{payload.OptionKey}'.";
				break;
			}

			case EventMutationOperation.Replace:
			{
				if (!TryCreatePayloadOption(eventId, rule, warnings, out EventMutationPlannedOption? payload))
				{
					applied = false;
					code = "plan.rule_invalid_payload";
					message = "Replace operation skipped: option payload is invalid.";
					break;
				}

				if (!TryFindTargetIndex(eventId, rule, plannedOptions, warnings, out int targetIndex, out targetOptionKey))
				{
					applied = false;
					code = "plan.rule_target_missing";
					message = "Replace operation skipped: target optionKey was not found.";
					break;
				}

				int duplicateIndex = FindOptionIndex(plannedOptions, payload.OptionKey);
				if (duplicateIndex >= 0 && duplicateIndex != targetIndex)
				{
					plannedOptions.RemoveAt(duplicateIndex);
					if (duplicateIndex < targetIndex)
					{
						targetIndex--;
					}

					warnings.Add(new EventMutationWarning(
						Severity: EventWheelSeverity.Warning,
						Code: "plan.rule_replace_removed_duplicate",
						Message: $"Replace operation removed duplicate optionKey before apply. optionKey='{payload.OptionKey}'.",
						EventId: eventId,
						SourceModId: sourceModId,
						RuleId: ruleId,
						TargetOptionKey: payload.OptionKey
					));
				}

				plannedOptions[targetIndex] = payload;
				optionKey = payload.OptionKey;
				applied = true;
				code = "plan.rule_applied";
				message = $"Replace operation applied. target='{targetOptionKey}', optionKey='{payload.OptionKey}'.";
				break;
			}

			case EventMutationOperation.InsertBefore:
			case EventMutationOperation.InsertAfter:
			{
				if (!TryCreatePayloadOption(eventId, rule, warnings, out EventMutationPlannedOption? payload))
				{
					applied = false;
					code = "plan.rule_invalid_payload";
					message = "Insert operation skipped: option payload is invalid.";
					break;
				}

				if (!TryFindTargetIndex(eventId, rule, plannedOptions, warnings, out int targetIndex, out targetOptionKey))
				{
					applied = false;
					code = "plan.rule_target_missing";
					message = "Insert operation skipped: target optionKey was not found.";
					break;
				}

				int duplicateIndex = FindOptionIndex(plannedOptions, payload.OptionKey);
				if (duplicateIndex >= 0)
				{
					plannedOptions.RemoveAt(duplicateIndex);
					if (duplicateIndex < targetIndex)
					{
						targetIndex--;
					}

					warnings.Add(new EventMutationWarning(
						Severity: EventWheelSeverity.Warning,
						Code: "plan.rule_insert_relocated_duplicate",
						Message: $"Insert operation relocated existing optionKey. optionKey='{payload.OptionKey}'.",
						EventId: eventId,
						SourceModId: sourceModId,
						RuleId: ruleId,
						TargetOptionKey: payload.OptionKey
					));
				}

				int insertIndex = rule.Operation == EventMutationOperation.InsertBefore
					? targetIndex
					: targetIndex + 1;

				if (insertIndex < 0)
				{
					insertIndex = 0;
				}
				if (insertIndex > plannedOptions.Count)
				{
					insertIndex = plannedOptions.Count;
				}

				plannedOptions.Insert(insertIndex, payload);
				optionKey = payload.OptionKey;
				applied = true;
				code = "plan.rule_applied";
				message = $"{rule.Operation} operation applied. target='{targetOptionKey}', optionKey='{payload.OptionKey}'.";
				break;
			}

			case EventMutationOperation.Lock:
			{
				if (!TryFindTargetIndex(eventId, rule, plannedOptions, warnings, out int targetIndex, out targetOptionKey))
				{
					applied = false;
					code = "plan.rule_target_missing";
					message = "Lock operation skipped: target optionKey was not found.";
					break;
				}

				plannedOptions[targetIndex] = plannedOptions[targetIndex].WithLocked(true);
				optionKey = plannedOptions[targetIndex].OptionKey;
				applied = true;
				code = "plan.rule_applied";
				message = $"Lock operation applied. target='{targetOptionKey}'.";
				break;
			}

			case EventMutationOperation.Remove:
			{
				if (!TryFindTargetIndex(eventId, rule, plannedOptions, warnings, out int targetIndex, out targetOptionKey))
				{
					applied = false;
					code = "plan.rule_target_missing";
					message = "Remove operation skipped: target optionKey was not found.";
					break;
				}

				optionKey = plannedOptions[targetIndex].OptionKey;
				plannedOptions.RemoveAt(targetIndex);
				applied = true;
				code = "plan.rule_applied";
				message = $"Remove operation applied. target='{targetOptionKey}'.";
				break;
			}

			default:
			{
				applied = false;
				code = "plan.rule_unknown_operation";
				message = $"Rule operation is not supported: {rule.Operation}.";
				warnings.Add(new EventMutationWarning(
					Severity: EventWheelSeverity.Warning,
					Code: code,
					Message: message,
					EventId: eventId,
					SourceModId: sourceModId,
					RuleId: ruleId,
					TargetOptionKey: targetOptionKey
				));
				break;
			}
		}

		if (!applied && rule.StopOnFailure)
		{
			warnings.Add(new EventMutationWarning(
				Severity: EventWheelSeverity.Error,
				Code: "plan.rule_stop_on_failure",
				Message: $"Rule failed and requested stop-on-failure. ruleId='{ruleId}'.",
				EventId: eventId,
				SourceModId: sourceModId,
				RuleId: ruleId,
				TargetOptionKey: targetOptionKey
			));
		}

		steps.Add(new EventMutationStep(
			Sequence: sequence,
			RuleId: ruleId,
			SourceModId: sourceModId,
			SourcePriority: orderedRule.SourcePriority,
			RuleOrder: rule.Order,
			Operation: rule.Operation,
			TargetOptionKey: targetOptionKey,
			OptionKey: optionKey,
			Applied: applied,
			Code: code,
			Message: message
		));
	}

	private static bool TryCreatePayloadOption(
		string eventId,
		IEventMutationRule rule,
		List<EventMutationWarning> warnings,
		[NotNullWhen(true)] out EventMutationPlannedOption? payload)
	{
		if (EventMutationPlannedOption.TryCreateFromDefinition(rule.Option, out payload, out string message)
			&& payload != null)
		{
			return true;
		}

		warnings.Add(new EventMutationWarning(
			Severity: EventWheelSeverity.Warning,
			Code: "plan.rule_invalid_payload",
			Message: $"Ignored rule option payload. {message}",
			EventId: eventId,
			SourceModId: rule.SourceModId?.Trim() ?? string.Empty,
			RuleId: rule.RuleId?.Trim() ?? string.Empty,
			TargetOptionKey: rule.TargetOptionKey?.Trim()
		));
		payload = null;
		return false;
	}

	private static bool TryFindTargetIndex(
		string eventId,
		IEventMutationRule rule,
		List<EventMutationPlannedOption> plannedOptions,
		List<EventMutationWarning> warnings,
		out int targetIndex,
		out string targetOptionKey)
	{
		targetIndex = -1;
		targetOptionKey = rule.TargetOptionKey?.Trim() ?? string.Empty;
		if (targetOptionKey.Length == 0)
		{
			warnings.Add(new EventMutationWarning(
				Severity: EventWheelSeverity.Warning,
				Code: "plan.rule_target_missing",
				Message: $"Rule targetOptionKey is required for operation {rule.Operation}.",
				EventId: eventId,
				SourceModId: rule.SourceModId?.Trim() ?? string.Empty,
				RuleId: rule.RuleId?.Trim() ?? string.Empty
			));
			return false;
		}

		targetIndex = FindOptionIndex(plannedOptions, targetOptionKey);
		if (targetIndex >= 0)
		{
			return true;
		}

		warnings.Add(new EventMutationWarning(
			Severity: EventWheelSeverity.Warning,
			Code: "plan.rule_target_not_found",
			Message: $"Rule target optionKey was not found. target='{targetOptionKey}'.",
			EventId: eventId,
			SourceModId: rule.SourceModId?.Trim() ?? string.Empty,
			RuleId: rule.RuleId?.Trim() ?? string.Empty,
			TargetOptionKey: targetOptionKey
		));
		return false;
	}

	private static int FindOptionIndex(List<EventMutationPlannedOption> plannedOptions, string optionKey)
	{
		for (int i = 0; i < plannedOptions.Count; i++)
		{
			if (StringComparer.Ordinal.Equals(plannedOptions[i].OptionKey, optionKey))
			{
				return i;
			}
		}

		return -1;
	}

	private static int CompareOption(EventMutationPlannedOption left, EventMutationPlannedOption right)
	{
		int orderCompare = left.Order.CompareTo(right.Order);
		if (orderCompare != 0)
		{
			return orderCompare;
		}

		int keyCompare = StringComparer.Ordinal.Compare(left.OptionKey, right.OptionKey);
		if (keyCompare != 0)
		{
			return keyCompare;
		}

		int titleCompare = StringComparer.Ordinal.Compare(left.TitleKey, right.TitleKey);
		if (titleCompare != 0)
		{
			return titleCompare;
		}

		return StringComparer.Ordinal.Compare(left.DescriptionKey, right.DescriptionKey);
	}

	private static int CompareOrderedRule(OrderedRule left, OrderedRule right)
	{
		int priorityCompare = right.SourcePriority.CompareTo(left.SourcePriority);
		if (priorityCompare != 0)
		{
			return priorityCompare;
		}

		int orderCompare = left.Rule.Order.CompareTo(right.Rule.Order);
		if (orderCompare != 0)
		{
			return orderCompare;
		}

		int operationCompare = ((int)left.Rule.Operation).CompareTo((int)right.Rule.Operation);
		if (operationCompare != 0)
		{
			return operationCompare;
		}

		int sourceCompare = StringComparer.Ordinal.Compare(left.Rule.SourceModId, right.Rule.SourceModId);
		if (sourceCompare != 0)
		{
			return sourceCompare;
		}

		return StringComparer.Ordinal.Compare(left.Rule.RuleId, right.Rule.RuleId);
	}

	private readonly record struct OrderedRule(IEventMutationRule Rule, int SourcePriority);
}