#nullable enable

using System;
using System.Collections.Generic;
using Godot;
using ReForgeFramework.Api.Events;

namespace ReForgeFramework.EventWheel;

/// <summary>
/// 联机选项守卫结果。
/// </summary>
internal sealed record NetworkOptionGuardResult(
	IReadOnlyList<EventMutationPlannedOption> Options,
	NetworkOptionGuardReport Report
);

/// <summary>
/// 联机选项守卫报告。
/// </summary>
internal sealed record NetworkOptionGuardReport(
	bool IsMultiplayer,
	int MaxAllowedCount,
	int InputCount,
	int OutputCount,
	int RemovedDuplicateCount,
	int TrimmedCount,
	bool AppliedStableSort,
	IReadOnlyList<EventMutationWarning> Warnings
);

/// <summary>
/// 联机场景事件选项归一化守卫。
/// 负责稳定排序、去重、裁剪与协议风险告警，避免跨端选项索引漂移。
/// </summary>
internal sealed class NetworkOptionGuard
{
	private const int DefaultMultiplayerOptionCount = 16;
	private const int ProtocolSafeOptionCount = 16;
	private const int AbsoluteMaxMultiplayerOptionCount = 256;
	private const string ProjectSettingMaxMultiplayerOptionCount = "reforge/eventwheel/network/max_multiplayer_option_count";
	private const string EnvironmentMaxMultiplayerOptionCount = "REFORGE_EVENTWHEEL_NETWORK_MAX_MULTIPLAYER_OPTION_COUNT";

	private static readonly object ConfigLogSyncRoot = new();
	private static bool _configLogWritten;
	private static bool _protocolRiskLogWritten;

	private readonly int _maxMultiplayerOptionCount;
	private readonly string _maxOptionCountSource;

	/// <summary>
	/// 使用配置源初始化守卫。
	/// 优先级：环境变量 > ProjectSettings > 默认值。
	/// </summary>
	public NetworkOptionGuard()
	{
		int configuredValue = ResolveConfiguredMaxMultiplayerOptionCount(out string source);
		_maxMultiplayerOptionCount = NormalizeMaxMultiplayerOptionCount(configuredValue, out int normalizedFrom);
		_maxOptionCountSource = source;
		LogNormalizationIfNeeded(configuredValue, normalizedFrom, _maxMultiplayerOptionCount, _maxOptionCountSource);
		LogConfigurationOnce(_maxMultiplayerOptionCount, _maxOptionCountSource);
	}

	/// <summary>
	/// 使用指定上限初始化守卫。
	/// </summary>
	public NetworkOptionGuard(int maxMultiplayerOptionCount = DefaultMultiplayerOptionCount)
	{
		_maxMultiplayerOptionCount = NormalizeMaxMultiplayerOptionCount(maxMultiplayerOptionCount, out int normalizedFrom);
		_maxOptionCountSource = "constructor";
		LogNormalizationIfNeeded(maxMultiplayerOptionCount, normalizedFrom, _maxMultiplayerOptionCount, _maxOptionCountSource);
		LogConfigurationOnce(_maxMultiplayerOptionCount, _maxOptionCountSource);
	}

	/// <summary>
	/// 对规划选项执行联机归一化。
	/// </summary>
	/// <param name="eventId">事件标识。</param>
	/// <param name="options">输入选项集合。</param>
	/// <param name="isMultiplayer">是否联机。</param>
	/// <param name="sourceModId">来源模组标识。</param>
	/// <returns>归一化后的选项与报告。</returns>
	public NetworkOptionGuardResult NormalizeForNetwork(
		string eventId,
		IReadOnlyList<EventMutationPlannedOption>? options,
		bool isMultiplayer,
		string sourceModId = "reforge.eventwheel")
	{
		string normalizedEventId = eventId?.Trim() ?? string.Empty;
		string normalizedSourceModId = sourceModId?.Trim() ?? string.Empty;

		if (normalizedSourceModId.Length == 0)
		{
			normalizedSourceModId = "reforge.eventwheel";
		}

		if (options == null || options.Count == 0)
		{
			return new NetworkOptionGuardResult(
				Options: Array.Empty<EventMutationPlannedOption>(),
				Report: new NetworkOptionGuardReport(
					IsMultiplayer: isMultiplayer,
					MaxAllowedCount: _maxMultiplayerOptionCount,
					InputCount: 0,
					OutputCount: 0,
					RemovedDuplicateCount: 0,
					TrimmedCount: 0,
					AppliedStableSort: false,
					Warnings: Array.Empty<EventMutationWarning>()
				)
			);
		}

		if (!isMultiplayer)
		{
			EventMutationPlannedOption[] singlePlayerSnapshot = new EventMutationPlannedOption[options.Count];
			for (int i = 0; i < options.Count; i++)
			{
				singlePlayerSnapshot[i] = options[i];
			}

			return new NetworkOptionGuardResult(
				Options: singlePlayerSnapshot,
				Report: new NetworkOptionGuardReport(
					IsMultiplayer: false,
					MaxAllowedCount: _maxMultiplayerOptionCount,
					InputCount: options.Count,
					OutputCount: singlePlayerSnapshot.Length,
					RemovedDuplicateCount: 0,
					TrimmedCount: 0,
					AppliedStableSort: false,
					Warnings: Array.Empty<EventMutationWarning>()
				)
			);
		}

		List<EventMutationWarning> warnings = new();
		AppendProtocolRiskWarningIfNeeded(warnings, normalizedEventId, normalizedSourceModId);
		List<EventMutationPlannedOption> normalized = new(options.Count);
		for (int i = 0; i < options.Count; i++)
		{
			EventMutationPlannedOption option = options[i];
			if (option == null)
			{
				warnings.Add(new EventMutationWarning(
					Severity: EventWheelSeverity.Warning,
					Code: "network.option_null",
					Message: $"Ignored null option entry at index {i} during network normalization.",
					EventId: normalizedEventId,
					SourceModId: normalizedSourceModId
				));
				continue;
			}

			string optionKey = option.OptionKey?.Trim() ?? string.Empty;
			if (optionKey.Length == 0)
			{
				optionKey = BuildFallbackOptionKey(option);
				warnings.Add(new EventMutationWarning(
					Severity: EventWheelSeverity.Warning,
					Code: "network.option_key_empty",
					Message: $"Option key was empty during network normalization; synthesized deterministic key '{optionKey}'.",
					EventId: normalizedEventId,
					SourceModId: normalizedSourceModId
				));
			}

			string actionKey = option.ActionKey?.Trim() ?? string.Empty;
			string titleKey = option.TitleKey?.Trim() ?? string.Empty;
			string descriptionKey = option.DescriptionKey?.Trim() ?? string.Empty;
			IReadOnlyList<string> normalizedTagKeys = NormalizeTagKeys(option.TagKeys);

			if (!StringComparer.Ordinal.Equals(option.OptionKey, optionKey)
				|| !StringComparer.Ordinal.Equals(option.ActionKey ?? string.Empty, actionKey)
				|| !StringComparer.Ordinal.Equals(option.TitleKey, titleKey)
				|| !StringComparer.Ordinal.Equals(option.DescriptionKey, descriptionKey)
				|| !ReferenceEquals(option.TagKeys, normalizedTagKeys))
			{
				option = new EventMutationPlannedOption(
					optionKey: optionKey,
					actionKey: actionKey,
					titleKey: titleKey,
					descriptionKey: descriptionKey,
					order: option.Order,
					isLocked: option.IsLocked,
					isProceed: option.IsProceed,
					tagKeys: normalizedTagKeys);
			}

			normalized.Add(option);
		}

		normalized.Sort(static (left, right) => CompareForNetwork(left, right));

		HashSet<string> dedupe = new(StringComparer.Ordinal);
		List<EventMutationPlannedOption> deduped = new(normalized.Count);
		int removedDuplicateCount = 0;
		for (int i = 0; i < normalized.Count; i++)
		{
			EventMutationPlannedOption option = normalized[i];
			if (!dedupe.Add(option.OptionKey))
			{
				removedDuplicateCount++;
				warnings.Add(new EventMutationWarning(
					Severity: EventWheelSeverity.Warning,
					Code: "network.option_duplicate_removed",
					Message: $"Removed duplicate optionKey for multiplayer synchronization. optionKey='{option.OptionKey}'.",
					EventId: normalizedEventId,
					SourceModId: normalizedSourceModId,
					TargetOptionKey: option.OptionKey
				));
				continue;
			}

			deduped.Add(option);
		}

		int trimmedCount = 0;
		if (deduped.Count > _maxMultiplayerOptionCount)
		{
			trimmedCount = deduped.Count - _maxMultiplayerOptionCount;
			deduped.RemoveRange(_maxMultiplayerOptionCount, trimmedCount);
			warnings.Add(new EventMutationWarning(
				Severity: EventWheelSeverity.Warning,
				Code: "network.option_trimmed",
				Message: $"Trimmed multiplayer options to {_maxMultiplayerOptionCount} to honor OptionIndex 4-bit constraint.",
				EventId: normalizedEventId,
				SourceModId: normalizedSourceModId
			));
		}

		if (removedDuplicateCount == 0 && trimmedCount == 0)
		{
			warnings.Add(new EventMutationWarning(
				Severity: EventWheelSeverity.Info,
				Code: "network.option_normalized",
				Message: "Multiplayer options normalized with stable sort; no dedupe/trim was required.",
				EventId: normalizedEventId,
				SourceModId: normalizedSourceModId
			));
		}

		return new NetworkOptionGuardResult(
			Options: deduped.ToArray(),
			Report: new NetworkOptionGuardReport(
				IsMultiplayer: true,
				MaxAllowedCount: _maxMultiplayerOptionCount,
				InputCount: options.Count,
				OutputCount: deduped.Count,
				RemovedDuplicateCount: removedDuplicateCount,
				TrimmedCount: trimmedCount,
				AppliedStableSort: true,
				Warnings: warnings.ToArray()
			)
		);
	}

	private static int ResolveConfiguredMaxMultiplayerOptionCount(out string source)
	{
		if (TryReadEnvironmentOverride(out int fromEnvironment))
		{
			source = $"env:{EnvironmentMaxMultiplayerOptionCount}";
			return fromEnvironment;
		}

		if (TryReadProjectSettingOverride(out int fromProjectSetting))
		{
			source = $"project:{ProjectSettingMaxMultiplayerOptionCount}";
			return fromProjectSetting;
		}

		source = "default";
		return DefaultMultiplayerOptionCount;
	}

	private static bool TryReadEnvironmentOverride(out int value)
	{
		value = 0;
		string? raw = System.Environment.GetEnvironmentVariable(EnvironmentMaxMultiplayerOptionCount);
		if (string.IsNullOrWhiteSpace(raw))
		{
			return false;
		}

		if (!int.TryParse(raw, out int parsed))
		{
			GD.PrintErr($"[ReForge.EventWheel] invalid env override for max multiplayer option count: key='{EnvironmentMaxMultiplayerOptionCount}', value='{raw}'. fallback to project/default.");
			return false;
		}

		value = parsed;
		return true;
	}

	private static bool TryReadProjectSettingOverride(out int value)
	{
		value = 0;
		try
		{
			if (!ProjectSettings.HasSetting(ProjectSettingMaxMultiplayerOptionCount))
			{
				return false;
			}

			object raw = ProjectSettings.GetSetting(ProjectSettingMaxMultiplayerOptionCount);
			if (!TryParseInt(raw, out int parsed))
			{
				GD.PrintErr($"[ReForge.EventWheel] invalid project setting for max multiplayer option count: key='{ProjectSettingMaxMultiplayerOptionCount}'. fallback to default.");
				return false;
			}

			value = parsed;
			return true;
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[ReForge.EventWheel] failed reading project setting '{ProjectSettingMaxMultiplayerOptionCount}'. {ex.GetType().Name}: {ex.Message}");
			return false;
		}
	}

	private static bool TryParseInt(object? raw, out int value)
	{
		switch (raw)
		{
			case null:
				value = 0;
				return false;
			case int i:
				value = i;
				return true;
			case long l when l >= int.MinValue && l <= int.MaxValue:
				value = (int)l;
				return true;
			case float f:
				value = (int)f;
				return true;
			case double d:
				value = (int)d;
				return true;
			case string s when int.TryParse(s, out int parsed):
				value = parsed;
				return true;
			default:
				value = 0;
				return false;
		}
	}

	private static int NormalizeMaxMultiplayerOptionCount(int requested, out int normalizedFrom)
	{
		normalizedFrom = requested;
		if (requested <= 0)
		{
			normalizedFrom = DefaultMultiplayerOptionCount;
			return DefaultMultiplayerOptionCount;
		}

		int bounded = requested;
		if (requested > AbsoluteMaxMultiplayerOptionCount)
		{
			bounded = AbsoluteMaxMultiplayerOptionCount;
		}

		if (!IsOptionIndex8BitPatchEnabled() && bounded > ProtocolSafeOptionCount)
		{
			return ProtocolSafeOptionCount;
		}

		return bounded;
	}

	private static bool IsOptionIndex8BitPatchEnabled()
	{
		try
		{
			return global::ReForge.IsOptionIndex8BitPatchEnabled();
		}
		catch
		{
			return false;
		}
	}

	private static void LogConfigurationOnce(int maxCount, string source)
	{
		lock (ConfigLogSyncRoot)
		{
			if (_configLogWritten)
			{
				return;
			}

			_configLogWritten = true;
		}

		GD.Print($"[ReForge.EventWheel] network option guard configured. maxMultiplayerOptionCount={maxCount}, source='{source}', protocolSafeLimit={ProtocolSafeOptionCount}, absoluteMax={AbsoluteMaxMultiplayerOptionCount}.");
	}

	private static void LogNormalizationIfNeeded(int requested, int normalizedFrom, int normalizedTo, string source)
	{
		if (requested == normalizedTo)
		{
			return;
		}

		if (!IsOptionIndex8BitPatchEnabled() && normalizedTo == ProtocolSafeOptionCount && requested > ProtocolSafeOptionCount)
		{
			GD.PrintErr($"[ReForge.EventWheel] max multiplayer option count was clamped to protocol-safe limit because OptionIndex 8-bit patch is disabled. requested={requested}, normalizedFrom={normalizedFrom}, normalizedTo={normalizedTo}, source='{source}'.");
			return;
		}

		GD.PrintErr($"[ReForge.EventWheel] normalized invalid max multiplayer option count. requested={requested}, normalizedFrom={normalizedFrom}, normalizedTo={normalizedTo}, source='{source}'.");
	}

	private void AppendProtocolRiskWarningIfNeeded(List<EventMutationWarning> warnings, string eventId, string sourceModId)
	{
		if (_maxMultiplayerOptionCount <= ProtocolSafeOptionCount)
		{
			return;
		}

		warnings.Add(new EventMutationWarning(
			Severity: EventWheelSeverity.Warning,
			Code: "network.option_protocol_risk",
			Message: $"Configured multiplayer option cap is {_maxMultiplayerOptionCount} (> {ProtocolSafeOptionCount}). Ensure OptionIndex 8-bit patch is enabled and every peer uses the same setting, otherwise option indexes can mismatch.",
			EventId: eventId,
			SourceModId: sourceModId
		));

		lock (ConfigLogSyncRoot)
		{
			if (_protocolRiskLogWritten)
			{
				return;
			}

			_protocolRiskLogWritten = true;
		}

		GD.PrintErr($"[ReForge.EventWheel] protocol risk detected: maxMultiplayerOptionCount={_maxMultiplayerOptionCount} exceeds protocol-safe limit {ProtocolSafeOptionCount}. Ensure OptionIndex 8-bit patch is enabled and synchronized on all peers.");
	}

	private static int CompareForNetwork(EventMutationPlannedOption left, EventMutationPlannedOption right)
	{
		int keyCompare = StringComparer.Ordinal.Compare(left.OptionKey, right.OptionKey);
		if (keyCompare != 0)
		{
			return keyCompare;
		}

		int orderCompare = left.Order.CompareTo(right.Order);
		if (orderCompare != 0)
		{
			return orderCompare;
		}

		int titleCompare = StringComparer.Ordinal.Compare(left.TitleKey, right.TitleKey);
		if (titleCompare != 0)
		{
			return titleCompare;
		}

		int descriptionCompare = StringComparer.Ordinal.Compare(left.DescriptionKey, right.DescriptionKey);
		if (descriptionCompare != 0)
		{
			return descriptionCompare;
		}

		int lockedCompare = left.IsLocked.CompareTo(right.IsLocked);
		if (lockedCompare != 0)
		{
			return lockedCompare;
		}

		int proceedCompare = left.IsProceed.CompareTo(right.IsProceed);
		if (proceedCompare != 0)
		{
			return proceedCompare;
		}

		int tagCompare = CompareTagKeys(left.TagKeys, right.TagKeys);
		if (tagCompare != 0)
		{
			return tagCompare;
		}

		return 0;
	}

	private static string BuildFallbackOptionKey(EventMutationPlannedOption option)
	{
		string actionKey = option.ActionKey?.Trim() ?? string.Empty;
		string title = option.TitleKey?.Trim() ?? string.Empty;
		string description = option.DescriptionKey?.Trim() ?? string.Empty;
		return $"__fallback__:{option.Order}:{option.IsLocked}:{option.IsProceed}:{actionKey}:{title}:{description}";
	}

	private static IReadOnlyList<string> NormalizeTagKeys(IReadOnlyList<string> tagKeys)
	{
		if (tagKeys == null || tagKeys.Count == 0)
		{
			return Array.Empty<string>();
		}

		List<string> normalized = new(tagKeys.Count);
		HashSet<string> dedupe = new(StringComparer.Ordinal);
		for (int i = 0; i < tagKeys.Count; i++)
		{
			string tag = tagKeys[i]?.Trim() ?? string.Empty;
			if (tag.Length == 0)
			{
				continue;
			}

			if (!dedupe.Add(tag))
			{
				continue;
			}

			normalized.Add(tag);
		}

		normalized.Sort(StringComparer.Ordinal);
		return normalized.ToArray();
	}

	private static int CompareTagKeys(IReadOnlyList<string>? leftTags, IReadOnlyList<string>? rightTags)
	{
		IReadOnlyList<string> safeLeftTags = leftTags ?? Array.Empty<string>();
		IReadOnlyList<string> safeRightTags = rightTags ?? Array.Empty<string>();

		int leftCount = safeLeftTags.Count;
		int rightCount = safeRightTags.Count;
		int minCount = Math.Min(leftCount, rightCount);

		for (int i = 0; i < minCount; i++)
		{
			string leftTag = safeLeftTags[i] ?? string.Empty;
			string rightTag = safeRightTags[i] ?? string.Empty;
			int compare = StringComparer.Ordinal.Compare(leftTag, rightTag);
			if (compare != 0)
			{
				return compare;
			}
		}

		return leftCount.CompareTo(rightCount);
	}
}