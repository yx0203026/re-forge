#nullable enable

using System;
using System.Collections.Generic;
using ReForgeFramework.Api.Events;

namespace ReForgeFramework.EventWheel;

internal sealed record EventMutationPlan(
	string EventId,
	EventKind Kind,
	IReadOnlyList<EventMutationStep> Steps,
	IReadOnlyList<EventMutationPlannedOption> PlannedOptions,
	IReadOnlyList<EventMutationWarning> Warnings
);

internal sealed record EventMutationStep(
	int Sequence,
	string RuleId,
	string SourceModId,
	int SourcePriority,
	int RuleOrder,
	EventMutationOperation Operation,
	string? TargetOptionKey,
	string? OptionKey,
	bool Applied,
	string Code,
	string Message
);

internal sealed record EventMutationWarning(
	EventWheelSeverity Severity,
	string Code,
	string Message,
	string EventId,
	string SourceModId,
	string? RuleId = null,
	string? TargetOptionKey = null
);

internal sealed class EventMutationPlannedOption : IEventOptionDefinition
{
	public EventMutationPlannedOption(
		string optionKey,
		string? actionKey,
		string titleKey,
		string descriptionKey,
		int order,
		bool isLocked,
		bool isProceed,
		string? poolId = null,
		int weight = 100,
		IReadOnlyList<string>? tagKeys = null)
	{
		OptionKey = optionKey;
		ActionKey = actionKey;
		TitleKey = titleKey;
		DescriptionKey = descriptionKey;
		Order = order;
		IsLocked = isLocked;
		IsProceed = isProceed;
		PoolId = NormalizePoolId(poolId);
		Weight = weight <= 0 ? 1 : weight;
		TagKeys = NormalizeTags(tagKeys);
	}

	public string OptionKey { get; }

	public string? ActionKey { get; }

	public string TitleKey { get; }

	public string DescriptionKey { get; }

	public int Order { get; }

	public bool IsLocked { get; }

	public bool IsProceed { get; }

	public string PoolId { get; }

	public int Weight { get; }

	public IReadOnlyList<string> TagKeys { get; }

	public EventMutationPlannedOption WithLocked(bool isLocked)
	{
		if (isLocked == IsLocked)
		{
			return this;
		}

		return new EventMutationPlannedOption(
			optionKey: OptionKey,
			actionKey: ActionKey,
			titleKey: TitleKey,
			descriptionKey: DescriptionKey,
			order: Order,
			isLocked: isLocked,
			isProceed: IsProceed,
			poolId: PoolId,
			weight: Weight,
			tagKeys: TagKeys
		);
	}

	public static bool TryCreateFromDefinition(
		IEventOptionDefinition? option,
		out EventMutationPlannedOption? plannedOption,
		out string message)
	{
		plannedOption = null;
		message = string.Empty;

		if (option == null)
		{
			message = "Option payload is null.";
			return false;
		}

		string optionKey = option.OptionKey?.Trim() ?? string.Empty;
		string titleKey = option.TitleKey?.Trim() ?? string.Empty;
		string descriptionKey = option.DescriptionKey?.Trim() ?? string.Empty;

		if (optionKey.Length == 0)
		{
			message = "Option payload optionKey is required.";
			return false;
		}

		if (titleKey.Length == 0)
		{
			message = $"Option payload titleKey is required. optionKey='{optionKey}'.";
			return false;
		}

		if (descriptionKey.Length == 0)
		{
			message = $"Option payload descriptionKey is required. optionKey='{optionKey}'.";
			return false;
		}

		plannedOption = new EventMutationPlannedOption(
			optionKey: optionKey,
			actionKey: option.ActionKey,
			titleKey: titleKey,
			descriptionKey: descriptionKey,
			order: option.Order,
			isLocked: option.IsLocked,
			isProceed: option.IsProceed,
			poolId: option is IEventOptionPoolEntry pooled ? pooled.PoolId : null,
			weight: option is IEventOptionPoolEntry pooledOption ? pooledOption.Weight : 100,
			tagKeys: option.TagKeys
		);
		return true;
	}

	private static string NormalizePoolId(string? poolId)
	{
		return poolId?.Trim() ?? string.Empty;
	}

	private static IReadOnlyList<string> NormalizeTags(IReadOnlyList<string>? tagKeys)
	{
		if (tagKeys == null || tagKeys.Count == 0)
		{
			return Array.Empty<string>();
		}

		List<string> normalized = new(tagKeys.Count);
		HashSet<string> dedupe = new(StringComparer.Ordinal);
		for (int i = 0; i < tagKeys.Count; i++)
		{
			string trimmed = tagKeys[i]?.Trim() ?? string.Empty;
			if (trimmed.Length == 0)
			{
				continue;
			}

			if (!dedupe.Add(trimmed))
			{
				continue;
			}

			normalized.Add(trimmed);
		}

		normalized.Sort(StringComparer.Ordinal);
		return normalized.ToArray();
	}
}