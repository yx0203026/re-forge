#nullable enable

using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Models;
using ReForgeFramework.Api.Events;

namespace ReForgeFramework.EventWheel;

/// <summary>
/// EventWheel 事件定义与变更规则注册中心。
/// 管理定义冲突、规则冲突、池化定义选择、以及注册与诊断监听分发。
/// </summary>
internal sealed class EventDefinitionRegistry
{
	private readonly object _syncRoot = new();
	private readonly EventDefinitionConflictResolver _conflictResolver;
	private readonly EventDefinitionConflictPolicy _definitionConflictPolicy;
	private readonly EventMutationConflictPolicy _mutationConflictPolicy;
	private readonly EventWheelDiagnostics _diagnostics;

	private readonly Dictionary<string, IEventDefinition> _definitionsByEventId = new(StringComparer.Ordinal);
	private readonly Dictionary<string, Dictionary<string, IEventDefinition>> _definitionPoolsByEventId = new(StringComparer.Ordinal);
	private readonly Dictionary<string, IEventDefinition[]> _orderedCandidatesByEventId = new(StringComparer.Ordinal);
	private readonly Dictionary<string, Dictionary<string, IEventMutationRule>> _rulesByEventId = new(StringComparer.Ordinal);
	private readonly Dictionary<string, Action<EventRegistrationResult>> _registrationListeners = new(StringComparer.Ordinal);

	/// <summary>
	/// 创建事件注册中心。
	/// </summary>
	/// <param name="conflictResolver">可选冲突解析器；为空时使用默认实现。</param>
	/// <param name="definitionConflictPolicy">定义冲突策略。</param>
	/// <param name="mutationConflictPolicy">规则冲突策略。</param>
	/// <param name="maxDiagnostics">内部诊断容量上限。</param>
	/// <param name="diagnostics">可选外部诊断实例。</param>
	public EventDefinitionRegistry(
		EventDefinitionConflictResolver? conflictResolver = null,
		EventDefinitionConflictPolicy definitionConflictPolicy = EventDefinitionConflictPolicy.ReplaceExisting,
		EventMutationConflictPolicy mutationConflictPolicy = EventMutationConflictPolicy.ReplaceExisting,
		int maxDiagnostics = 256,
		EventWheelDiagnostics? diagnostics = null)
	{
		_conflictResolver = conflictResolver ?? new EventDefinitionConflictResolver();
		_definitionConflictPolicy = definitionConflictPolicy;
		_mutationConflictPolicy = mutationConflictPolicy;
		_diagnostics = diagnostics ?? new EventWheelDiagnostics(maxDiagnostics: maxDiagnostics);
	}

	/// <summary>
	/// 注册诊断监听器。
	/// </summary>
	public bool RegisterDiagnosticListener(string busId, Action<EventWheelDiagnosticEvent> listener)
	{
		return _diagnostics.RegisterListener(busId, listener);
	}

	/// <summary>
	/// 注册定义注册结果监听器。
	/// </summary>
	public bool RegisterRegistrationListener(string busId, Action<EventRegistrationResult> listener)
	{
		if (!TryNormalizeRequiredKey(busId, out string normalizedBusId))
		{
			return false;
		}

		ArgumentNullException.ThrowIfNull(listener);
		lock (_syncRoot)
		{
			bool replaced = _registrationListeners.ContainsKey(normalizedBusId);
			_registrationListeners[normalizedBusId] = listener;
			return replaced;
		}
	}

	/// <summary>
	/// 注销诊断监听器。
	/// </summary>
	public int UnregisterDiagnosticListener(string busId)
	{
		return _diagnostics.UnregisterListener(busId);
	}

	/// <summary>
	/// 注销定义注册结果监听器。
	/// </summary>
	public int UnregisterRegistrationListener(string busId)
	{
		if (!TryNormalizeRequiredKey(busId, out string normalizedBusId))
		{
			return 0;
		}

		lock (_syncRoot)
		{
			return _registrationListeners.Remove(normalizedBusId) ? 1 : 0;
		}
	}

	/// <summary>
	/// 注册事件定义。
	/// 支持固定定义与池化定义，并在冲突时按策略处理。
	/// </summary>
	public EventRegistrationResult RegisterDefinition(
		IEventDefinition? definition,
		EventDefinitionConflictPolicy? conflictPolicy = null)
	{
		if (!TryValidateDefinition(definition, out string eventId, out string sourceModId, out string validationMessage))
		{
			EventRegistrationResult failedResult = new(
				Success: false,
				EventId: eventId,
				SourceModId: sourceModId,
				Replaced: false,
				Message: validationMessage
			);
			PublishRegistrationResult(failedResult);
			PublishDiagnostic(CreateDiagnostic(
				severity: EventWheelSeverity.Error,
				eventId: eventId,
				sourceModId: sourceModId,
				message: validationMessage,
				context: null
			));
			return failedResult;
		}

		EventRegistrationResult result;
		EventWheelDiagnosticEvent diagnostic;
		lock (_syncRoot)
		{
			if (definition is IEventPoolDefinition poolDefinition)
			{
				result = RegisterPoolDefinitionUnsafe(eventId, sourceModId, poolDefinition);
				_orderedCandidatesByEventId.Remove(eventId);
				diagnostic = CreateDiagnosticUnsafe(
					severity: EventWheelSeverity.Info,
					eventId: eventId,
					sourceModId: sourceModId,
					message: result.Message,
					context: new Dictionary<string, string>(StringComparer.Ordinal)
					{
						["poolId"] = poolDefinition.PoolId,
						["weight"] = poolDefinition.Weight.ToString()
					}
				);
			}
			else if (!_definitionsByEventId.TryGetValue(eventId, out IEventDefinition? existingDefinition))
			{
				_definitionsByEventId[eventId] = definition!;
				_orderedCandidatesByEventId.Remove(eventId);
				result = new EventRegistrationResult(
					Success: true,
					EventId: eventId,
					SourceModId: sourceModId,
					Replaced: false,
					Message: "Event definition registered."
				);
				diagnostic = CreateDiagnosticUnsafe(
					severity: EventWheelSeverity.Info,
					eventId: eventId,
					sourceModId: sourceModId,
					message: "Event definition registered.",
					context: null
				);
			}
			else
			{
				EventDefinitionConflictResolution resolution = _conflictResolver.ResolveDefinition(
					existingDefinition,
					definition!,
					conflictPolicy ?? _definitionConflictPolicy
				);

				if (resolution.AcceptIncoming)
				{
					_definitionsByEventId[eventId] = definition!;
					_orderedCandidatesByEventId.Remove(eventId);
				}

				result = new EventRegistrationResult(
					Success: resolution.AcceptIncoming,
					EventId: eventId,
					SourceModId: sourceModId,
					Replaced: resolution.AcceptIncoming,
					Message: resolution.Message
				);
				diagnostic = CreateDiagnosticUnsafe(
					severity: resolution.Severity,
					eventId: eventId,
					sourceModId: sourceModId,
					message: resolution.Message,
					context: new Dictionary<string, string>(StringComparer.Ordinal)
					{
						["code"] = resolution.Code,
						["incomingAccepted"] = resolution.AcceptIncoming ? "true" : "false"
					}
				);
			}
		}

		PublishRegistrationResult(result);
		PublishDiagnostic(diagnostic);
		return result;
	}

	/// <summary>
	/// 注册事件变更规则。
	/// </summary>
	public EventWheelResult RegisterMutationRule(
		IEventMutationRule? rule,
		EventMutationConflictPolicy? conflictPolicy = null)
	{
		if (!TryValidateMutationRule(rule, out string eventId, out string sourceModId, out string validationMessage))
		{
			EventWheelResult failedResult = new(
				Success: false,
				Code: "rule.invalid",
				Message: validationMessage,
				EventId: eventId,
				SourceModId: sourceModId,
				Details: null
			);
			PublishDiagnostic(CreateDiagnostic(
				severity: EventWheelSeverity.Error,
				eventId: eventId,
				sourceModId: sourceModId,
				message: validationMessage,
				context: new Dictionary<string, string>(StringComparer.Ordinal)
				{
					["code"] = "rule.invalid"
				}
			));
			return failedResult;
		}

		EventWheelResult result;
		EventWheelDiagnosticEvent diagnostic;
		lock (_syncRoot)
		{
			if (!_rulesByEventId.TryGetValue(eventId, out Dictionary<string, IEventMutationRule>? rulesById))
			{
				rulesById = new Dictionary<string, IEventMutationRule>(StringComparer.Ordinal);
				_rulesByEventId[eventId] = rulesById;
			}

			if (!rulesById.TryGetValue(rule!.RuleId, out IEventMutationRule? existingRule))
			{
				rulesById[rule.RuleId] = rule;
				result = new EventWheelResult(
					Success: true,
					Code: "rule.registered",
					Message: "Mutation rule registered.",
					EventId: eventId,
					SourceModId: sourceModId,
					Details: null
				);
				diagnostic = CreateDiagnosticUnsafe(
					severity: EventWheelSeverity.Info,
					eventId: eventId,
					sourceModId: sourceModId,
					message: $"Mutation rule registered. ruleId='{rule.RuleId}'.",
					context: null
				);
			}
			else
			{
				EventDefinitionConflictResolution resolution = _conflictResolver.ResolveMutationRule(
					existingRule,
					rule,
					conflictPolicy ?? _mutationConflictPolicy
				);

				if (resolution.AcceptIncoming)
				{
					rulesById[rule.RuleId] = rule;
				}

				result = new EventWheelResult(
					Success: resolution.AcceptIncoming,
					Code: resolution.Code,
					Message: resolution.Message,
					EventId: eventId,
					SourceModId: sourceModId,
					Details: null
				);
				diagnostic = CreateDiagnosticUnsafe(
					severity: resolution.Severity,
					eventId: eventId,
					sourceModId: sourceModId,
					message: resolution.Message,
					context: new Dictionary<string, string>(StringComparer.Ordinal)
					{
						["code"] = resolution.Code,
						["ruleId"] = rule.RuleId,
						["incomingAccepted"] = resolution.AcceptIncoming ? "true" : "false"
					}
				);
			}
		}

		PublishDiagnostic(diagnostic);
		return result;
	}

	/// <summary>
	/// 按 eventId 查询定义（不带模型上下文）。
	/// </summary>
	public bool TryGetDefinition(string eventId, out IEventDefinition? definition)
	{
		return TryGetDefinition(eventId, eventModel: null, out definition);
	}

	/// <summary>
	/// 按 eventId 与模型上下文查询定义。
	/// 当存在池化定义时会执行稳定加权选择。
	/// </summary>
	public bool TryGetDefinition(string eventId, EventModel? eventModel, out IEventDefinition? definition)
	{
		definition = null;
		if (!TryNormalizeRequiredKey(eventId, out string normalizedEventId))
		{
			return false;
		}

		lock (_syncRoot)
		{
			bool hasFixedDefinition = _definitionsByEventId.TryGetValue(normalizedEventId, out IEventDefinition? fixedDefinition);
			if (!_definitionPoolsByEventId.TryGetValue(normalizedEventId, out Dictionary<string, IEventDefinition>? pooledDefinitions)
				|| pooledDefinitions.Count == 0)
			{
				definition = hasFixedDefinition ? fixedDefinition : null;
				return hasFixedDefinition;
			}

			IEventDefinition[] candidates = GetOrBuildOrderedCandidatesUnsafe(normalizedEventId, fixedDefinition, pooledDefinitions);
			definition = SelectDefinitionFromPool(normalizedEventId, eventModel, candidates);
			return definition != null;
		}
	}

	/// <summary>
	/// 获取当前所有事件定义快照（包含固定定义与池化定义）。
	/// </summary>
	public IReadOnlyList<IEventDefinition> GetDefinitionSnapshot()
	{
		lock (_syncRoot)
		{
			int totalCount = _definitionsByEventId.Count;
			foreach ((_, Dictionary<string, IEventDefinition> pooledDefinitions) in _definitionPoolsByEventId)
			{
				totalCount += pooledDefinitions.Count;
			}

			if (totalCount == 0)
			{
				return Array.Empty<IEventDefinition>();
			}

			IEventDefinition[] snapshot = new IEventDefinition[totalCount];
			int index = 0;
			foreach ((_, IEventDefinition definition) in _definitionsByEventId)
			{
				snapshot[index++] = definition;
			}

			foreach ((_, Dictionary<string, IEventDefinition> pooledDefinitions) in _definitionPoolsByEventId)
			{
				foreach ((_, IEventDefinition definition) in pooledDefinitions)
				{
					snapshot[index++] = definition;
				}
			}

			Array.Sort(snapshot, static (left, right) =>
			{
				int eventCompare = StringComparer.Ordinal.Compare(left.EventId, right.EventId);
				if (eventCompare != 0)
				{
					return eventCompare;
				}

				int sourceCompare = StringComparer.Ordinal.Compare(left.SourceModId, right.SourceModId);
				if (sourceCompare != 0)
				{
					return sourceCompare;
				}

				return StringComparer.Ordinal.Compare(left.GetType().FullName ?? left.GetType().Name, right.GetType().FullName ?? right.GetType().Name);
			});
			return snapshot;
		}
	}

	/// <summary>
	/// 获取指定事件的变更规则快照。
	/// 返回结果按稳定比较器排序，确保跨端一致。
	/// </summary>
	public IReadOnlyList<IEventMutationRule> GetMutationRules(string eventId)
	{
		if (!TryNormalizeRequiredKey(eventId, out string normalizedEventId))
		{
			return Array.Empty<IEventMutationRule>();
		}

		lock (_syncRoot)
		{
			if (!_rulesByEventId.TryGetValue(normalizedEventId, out Dictionary<string, IEventMutationRule>? rulesById)
				|| rulesById.Count == 0)
			{
				return Array.Empty<IEventMutationRule>();
			}

			IEventMutationRule[] snapshot = new IEventMutationRule[rulesById.Count];
			int index = 0;
			foreach ((_, IEventMutationRule rule) in rulesById)
			{
				snapshot[index++] = rule;
			}

			// 使用稳定比较器输出确定顺序，避免集合遍历导致跨端差异。
			Array.Sort(snapshot, static (left, right) =>
			{
				int orderCompare = left.Order.CompareTo(right.Order);
				if (orderCompare != 0)
				{
					return orderCompare;
				}

				int sourceCompare = StringComparer.Ordinal.Compare(left.SourceModId, right.SourceModId);
				if (sourceCompare != 0)
				{
					return sourceCompare;
				}

				return StringComparer.Ordinal.Compare(left.RuleId, right.RuleId);
			});

			return snapshot;
		}
	}

	/// <summary>
	/// 获取指定事件的定义与规则集合。
	/// </summary>
	public bool TryGetEventData(string eventId, out IEventDefinition? definition, out IReadOnlyList<IEventMutationRule> rules)
	{
		rules = Array.Empty<IEventMutationRule>();
		if (!TryGetDefinition(eventId, out definition) || definition == null)
		{
			return false;
		}

		rules = GetMutationRules(eventId);
		return true;
	}

	/// <summary>
	/// 查询诊断事件。
	/// </summary>
	public IReadOnlyList<EventWheelDiagnosticEvent> QueryDiagnostics(EventWheelDiagnosticQuery? query = null)
	{
		return _diagnostics.Query(query);
	}

	/// <summary>
	/// 获取诊断汇总信息。
	/// </summary>
	public EventWheelDiagnosticsSummary GetDiagnosticsSummary(EventWheelDiagnosticQuery? query = null)
	{
		return _diagnostics.GetSummary(query);
	}

	/// <summary>
	/// 构建诊断快照（事件明细 + 汇总）。
	/// </summary>
	public EventWheelDiagnosticsSnapshot GetDiagnosticsSnapshot(EventWheelDiagnosticQuery? query = null)
	{
		return _diagnostics.BuildSnapshot(query);
	}

	private void PublishRegistrationResult(EventRegistrationResult result)
	{
		Action<EventRegistrationResult>[] listeners;
		lock (_syncRoot)
		{
			listeners = SnapshotListenersUnsafe(_registrationListeners);
		}

		for (int i = 0; i < listeners.Length; i++)
		{
			try
			{
				listeners[i](result);
			}
			catch
			{
				// 诊断监听异常不得影响主流程。
			}
		}
	}

	private void PublishDiagnostic(EventWheelDiagnosticEvent diagnostic)
	{
		_diagnostics.Track(diagnostic);
	}

	private static Action<T>[] SnapshotListenersUnsafe<T>(Dictionary<string, Action<T>> listeners)
	{
		if (listeners.Count == 0)
		{
			return Array.Empty<Action<T>>();
		}

		Action<T>[] snapshot = new Action<T>[listeners.Count];
		int index = 0;
		foreach ((_, Action<T> listener) in listeners)
		{
			snapshot[index++] = listener;
		}

		return snapshot;
	}

	private EventWheelDiagnosticEvent CreateDiagnostic(
		EventWheelSeverity severity,
		string eventId,
		string sourceModId,
		string message,
		IReadOnlyDictionary<string, string>? context)
	{
		return CreateDiagnosticUnsafe(severity, eventId, sourceModId, message, context);
	}

	private static EventWheelDiagnosticEvent CreateDiagnosticUnsafe(
		EventWheelSeverity severity,
		string eventId,
		string sourceModId,
		string message,
		IReadOnlyDictionary<string, string>? context)
	{
		return new EventWheelDiagnosticEvent(
			TimestampUtc: DateTimeOffset.UtcNow,
			Stage: EventWheelStage.Register,
			Severity: severity,
			EventId: eventId,
			SourceModId: sourceModId,
			Message: message,
			ExceptionSummary: null,
			Context: context
		);
	}

	private EventRegistrationResult RegisterPoolDefinitionUnsafe(
		string eventId,
		string sourceModId,
		IEventPoolDefinition poolDefinition)
	{
		if (!_definitionPoolsByEventId.TryGetValue(eventId, out Dictionary<string, IEventDefinition>? pooledDefinitions))
		{
			pooledDefinitions = new Dictionary<string, IEventDefinition>(StringComparer.Ordinal);
			_definitionPoolsByEventId[eventId] = pooledDefinitions;
		}

		string poolKey = BuildPoolEntryKey(poolDefinition.PoolId, poolDefinition, sourceModId);
		bool replaced = pooledDefinitions.ContainsKey(poolKey);
		pooledDefinitions[poolKey] = poolDefinition;

		return new EventRegistrationResult(
			Success: true,
			EventId: eventId,
			SourceModId: sourceModId,
			Replaced: replaced,
			Message: replaced ? "Event definition pool entry replaced." : "Event definition pooled.");
	}

	private IEventDefinition[] GetOrBuildOrderedCandidatesUnsafe(
		string eventId,
		IEventDefinition? fixedDefinition,
		Dictionary<string, IEventDefinition> pooledDefinitions)
	{
		if (_orderedCandidatesByEventId.TryGetValue(eventId, out IEventDefinition[]? cached))
		{
			return cached;
		}

		List<IEventDefinition> ordered = new(pooledDefinitions.Count + (fixedDefinition == null ? 0 : 1));
		if (fixedDefinition != null)
		{
			ordered.Add(fixedDefinition);
		}

		foreach ((_, IEventDefinition pooled) in pooledDefinitions)
		{
			ordered.Add(pooled);
		}

		ordered.Sort(static (left, right) =>
		{
			int sourceCompare = StringComparer.Ordinal.Compare(left.SourceModId, right.SourceModId);
			if (sourceCompare != 0)
			{
				return sourceCompare;
			}

			return StringComparer.Ordinal.Compare(left.GetType().FullName ?? left.GetType().Name, right.GetType().FullName ?? right.GetType().Name);
		});

		IEventDefinition[] snapshot = ordered.ToArray();
		_orderedCandidatesByEventId[eventId] = snapshot;
		return snapshot;
	}

	private static IEventDefinition? SelectDefinitionFromPool(
		string eventId,
		EventModel? eventModel,
		IReadOnlyList<IEventDefinition> candidates)
	{
		if (candidates.Count == 0)
		{
			return null;
		}

		string seed = EventWheelPoolSelection.BuildSeed(
			eventId,
			eventModel?.Id.Entry,
			eventModel?.Id.ToString(),
			eventModel?.GetType().FullName);

		int selectedIndex = EventWheelPoolSelection.SelectWeightedIndex(
			candidates,
			seed,
			static definition => definition is IEventPoolEntry entry ? entry.Weight : 1);
		if (selectedIndex < 0 || selectedIndex >= candidates.Count)
		{
			return candidates[0];
		}

		return candidates[selectedIndex];
	}

	private static string BuildPoolEntryKey(string poolId, IEventDefinition definition, string sourceModId)
	{
		string normalizedPoolId = poolId?.Trim() ?? string.Empty;
		string normalizedSourceModId = sourceModId?.Trim() ?? string.Empty;
		string typeName = definition.GetType().FullName ?? definition.GetType().Name;
		return string.Concat(normalizedPoolId, "\u001f", normalizedSourceModId, "\u001f", typeName);
	}

	private static bool TryValidateDefinition(
		IEventDefinition? definition,
		out string eventId,
		out string sourceModId,
		out string message)
	{
		eventId = string.Empty;
		sourceModId = string.Empty;

		if (definition == null)
		{
			message = "Event definition is null.";
			return false;
		}

		eventId = definition.EventId?.Trim() ?? string.Empty;
		sourceModId = definition.SourceModId?.Trim() ?? string.Empty;

		if (eventId.Length == 0)
		{
			message = "Event definition eventId is required.";
			return false;
		}

		if (sourceModId.Length == 0)
		{
			message = $"Event definition sourceModId is required. eventId='{eventId}'.";
			return false;
		}

		if (definition.InitialOptions == null || definition.InitialOptions.Count == 0)
		{
			message = $"Event definition initial options are required. eventId='{eventId}', source='{sourceModId}'.";
			return false;
		}

		for (int i = 0; i < definition.InitialOptions.Count; i++)
		{
			IEventOptionDefinition? option = definition.InitialOptions[i];
			if (option == null)
			{
				message = $"Event definition option is null. eventId='{eventId}', index={i}.";
				return false;
			}

			if (string.IsNullOrWhiteSpace(option.OptionKey))
			{
				message = $"Event definition optionKey is required. eventId='{eventId}', index={i}.";
				return false;
			}

			if (string.IsNullOrWhiteSpace(option.TitleKey))
			{
				message = $"Event definition titleKey is required. eventId='{eventId}', option='{option.OptionKey}'.";
				return false;
			}

			if (string.IsNullOrWhiteSpace(option.DescriptionKey))
			{
				message = $"Event definition descriptionKey is required. eventId='{eventId}', option='{option.OptionKey}'.";
				return false;
			}
		}

		message = string.Empty;
		return true;
	}

	private static bool TryValidateMutationRule(
		IEventMutationRule? rule,
		out string eventId,
		out string sourceModId,
		out string message)
	{
		eventId = string.Empty;
		sourceModId = string.Empty;

		if (rule == null)
		{
			message = "Mutation rule is null.";
			return false;
		}

		eventId = rule.EventId?.Trim() ?? string.Empty;
		sourceModId = rule.SourceModId?.Trim() ?? string.Empty;

		if (eventId.Length == 0)
		{
			message = "Mutation rule eventId is required.";
			return false;
		}

		if (sourceModId.Length == 0)
		{
			message = $"Mutation rule sourceModId is required. eventId='{eventId}'.";
			return false;
		}

		if (string.IsNullOrWhiteSpace(rule.RuleId))
		{
			message = $"Mutation rule ruleId is required. eventId='{eventId}', source='{sourceModId}'.";
			return false;
		}

		if (rule.Operation is EventMutationOperation.Replace
			or EventMutationOperation.InsertBefore
			or EventMutationOperation.InsertAfter
			or EventMutationOperation.Lock
			or EventMutationOperation.Remove)
		{
			if (string.IsNullOrWhiteSpace(rule.TargetOptionKey))
			{
				message = $"Mutation rule targetOptionKey is required for operation '{rule.Operation}'. eventId='{eventId}', ruleId='{rule.RuleId}'.";
				return false;
			}
		}

		if (rule.Operation is EventMutationOperation.Add
			or EventMutationOperation.Replace
			or EventMutationOperation.InsertBefore
			or EventMutationOperation.InsertAfter)
		{
			if (rule.Option == null)
			{
				message = $"Mutation rule option payload is required for operation '{rule.Operation}'. eventId='{eventId}', ruleId='{rule.RuleId}'.";
				return false;
			}

			if (string.IsNullOrWhiteSpace(rule.Option.OptionKey)
				|| string.IsNullOrWhiteSpace(rule.Option.TitleKey)
				|| string.IsNullOrWhiteSpace(rule.Option.DescriptionKey))
			{
				message = $"Mutation rule option payload is incomplete. eventId='{eventId}', ruleId='{rule.RuleId}'.";
				return false;
			}
		}

		message = string.Empty;
		return true;
	}

	private static bool TryNormalizeRequiredKey(string value, out string normalized)
	{
		normalized = value?.Trim() ?? string.Empty;
		return normalized.Length > 0;
	}
}
