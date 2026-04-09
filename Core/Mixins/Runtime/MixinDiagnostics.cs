#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json;

namespace ReForgeFramework.Mixins.Runtime;

public sealed record MixinAppliedEntryView(
	string InjectionDescriptorKey,
	string MixinId,
	string HarmonyId,
	string Kind,
	string TargetType,
	string TargetMethod,
	string HandlerType,
	string HandlerMethod,
	DateTimeOffset AppliedAtUtc
);

public sealed record MixinRegistrationStatusView(
	string ModId,
	string State,
	bool StrictMode,
	int Installed,
	int Failed,
	int Skipped,
	string Message,
	DateTimeOffset UpdatedAtUtc
);

public sealed record MixinLifecycleStatusView(
	string ModId,
	string State,
	bool StrictMode,
	string HarmonyId,
	string AssemblyName,
	int Installed,
	int Failed,
	int Skipped,
	int ScannerErrors,
	int ScannerWarnings,
	int UnpatchFailures,
	string Message,
	DateTimeOffset UpdatedAtUtc
);

public sealed record MixinDiagnosticsSnapshot(
	DateTimeOffset TimestampUtc,
	int RegistrationCount,
	int LifecycleCount,
	int AppliedPatchCount,
	IReadOnlyList<MixinRegistrationStatusView> Registrations,
	IReadOnlyList<MixinLifecycleStatusView> Lifecycles,
	IReadOnlyList<MixinAppliedEntryView> AppliedPatches
);

internal sealed class MixinDiagnostics
{
	public MixinDiagnosticsSnapshot BuildSnapshot(
		MixinStatusSnapshot registrationStatus,
		MixinLifecycleSnapshot lifecycleSnapshot,
		IReadOnlyList<MixinAppliedEntry> appliedEntries)
	{
		ArgumentNullException.ThrowIfNull(registrationStatus);
		ArgumentNullException.ThrowIfNull(lifecycleSnapshot);
		ArgumentNullException.ThrowIfNull(appliedEntries);

		List<MixinRegistrationStatusView> registrationViews = new();
		foreach (KeyValuePair<string, MixinRegistrationResult> pair in registrationStatus.Registrations)
		{
			registrationViews.Add(new MixinRegistrationStatusView(
				pair.Key,
				pair.Value.State.ToString(),
				pair.Value.StrictMode,
				pair.Value.Summary.Installed,
				pair.Value.Summary.Failed,
				pair.Value.Summary.Skipped,
				pair.Value.Message,
				pair.Value.TimestampUtc
			));
		}

		registrationViews.Sort(static (a, b) => string.CompareOrdinal(a.ModId, b.ModId));

		List<MixinLifecycleStatusView> lifecycleViews = new();
		foreach (KeyValuePair<string, MixinModLifecycleStatus> pair in lifecycleSnapshot.Mods)
		{
			MixinModLifecycleStatus status = pair.Value;
			lifecycleViews.Add(new MixinLifecycleStatusView(
				pair.Key,
				status.State.ToString(),
				status.StrictMode,
				status.HarmonyId,
				status.AssemblyName,
				status.Counters.Installed,
				status.Counters.Failed,
				status.Counters.Skipped,
				status.Counters.ScannerErrors,
				status.Counters.ScannerWarnings,
				status.Counters.UnpatchFailures,
				status.Message,
				status.UpdatedAtUtc
			));
		}

		lifecycleViews.Sort(static (a, b) => string.CompareOrdinal(a.ModId, b.ModId));

		List<MixinAppliedEntryView> patchViews = new(appliedEntries.Count);
		for (int i = 0; i < appliedEntries.Count; i++)
		{
			MixinAppliedEntry entry = appliedEntries[i];
			patchViews.Add(new MixinAppliedEntryView(
				entry.InjectionDescriptorKey,
				entry.MixinId,
				entry.HarmonyId,
				entry.Kind.ToString(),
				entry.TargetMethod.DeclaringType?.FullName ?? "UnknownType",
				entry.TargetMethod.Name,
				entry.DeclaredHandlerMethod.DeclaringType?.FullName ?? "UnknownType",
				entry.DeclaredHandlerMethod.Name,
				entry.AppliedAtUtc
			));
		}

		patchViews.Sort(static (a, b) => string.CompareOrdinal(a.InjectionDescriptorKey, b.InjectionDescriptorKey));

		return new MixinDiagnosticsSnapshot(
			DateTimeOffset.UtcNow,
			registrationViews.Count,
			lifecycleViews.Count,
			patchViews.Count,
			new ReadOnlyCollection<MixinRegistrationStatusView>(registrationViews),
			new ReadOnlyCollection<MixinLifecycleStatusView>(lifecycleViews),
			new ReadOnlyCollection<MixinAppliedEntryView>(patchViews)
		);
	}

	public string ToJson(MixinDiagnosticsSnapshot snapshot, bool indented = true)
	{
		ArgumentNullException.ThrowIfNull(snapshot);

		JsonSerializerOptions options = new()
		{
			WriteIndented = indented,
			PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		};

		return JsonSerializer.Serialize(snapshot, options);
	}
}
