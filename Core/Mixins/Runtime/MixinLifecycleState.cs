#nullable enable

using System;
using System.Collections.Generic;

namespace ReForgeFramework.Mixins.Runtime;

public enum MixinLifecycleState
{
	NotInstalled = 0,
	Installing = 1,
	Active = 2,
	Failed = 3,
	Unloading = 4,
	Unloaded = 5,
}

public readonly record struct MixinLifecycleCounters(
	int Installed,
	int Failed,
	int Skipped,
	int ScannerErrors,
	int ScannerWarnings,
	int UnpatchFailures
);

public sealed record MixinModLifecycleStatus(
	string ModId,
	MixinLifecycleState State,
	bool StrictMode,
	string HarmonyId,
	string AssemblyName,
	MixinLifecycleCounters Counters,
	string Message,
	DateTimeOffset UpdatedAtUtc
);

public sealed class MixinLifecycleSnapshot
{
	public MixinLifecycleSnapshot(IReadOnlyDictionary<string, MixinModLifecycleStatus> mods)
	{
		ArgumentNullException.ThrowIfNull(mods);
		Mods = mods;
	}

	public IReadOnlyDictionary<string, MixinModLifecycleStatus> Mods { get; }
}

public sealed record MixinLifecycleInstallResult(
	string ModId,
	MixinLifecycleState State,
	bool NoOp,
	bool AbortedByStrictMode,
	MixinLifecycleCounters Counters,
	string Message,
	DateTimeOffset TimestampUtc
);

public sealed record MixinLifecycleUnloadResult(
	string ModId,
	MixinLifecycleState State,
	bool NoOp,
	int RemovedCount,
	int UnpatchFailures,
	string Message,
	DateTimeOffset TimestampUtc
);
