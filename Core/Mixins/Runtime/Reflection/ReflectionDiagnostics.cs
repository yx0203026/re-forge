#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;

namespace ReForgeFramework.Mixins.Runtime.Reflection;

internal sealed record ReflectionWarmupPlan(
	string PlanId,
	string Owner,
	IReadOnlyList<ReflectionMemberKey> RequiredMembers,
	IReadOnlyList<ReflectionMemberKey> OptionalMembers
);

internal sealed record ReflectionWarmupPlanResult(
	string PlanId,
	string Owner,
	int ResolvedCount,
	int RequiredFailureCount,
	int OptionalFailureCount,
	long DurationMs,
	IReadOnlyList<ReflectionAccessError> Errors
);

internal sealed record ReflectionWarmupBatchResult(
	int PlanCount,
	int ResolvedCount,
	int RequiredFailureCount,
	int OptionalFailureCount,
	long DurationMs,
	IReadOnlyList<ReflectionWarmupPlanResult> Plans,
	IReadOnlyList<ReflectionAccessError> Errors
)
{
	public bool HasRequiredFailures => RequiredFailureCount > 0;
}

public sealed record ReflectionRuntimeSnapshot(
	int WarmupPlanCount,
	int WarmupResolvedCount,
	int WarmupRequiredFailureCount,
	int WarmupOptionalFailureCount,
	long WarmupDurationMs,
	long CacheHitCount,
	long CacheMissBlockedCount,
	long FallbackCount,
	IReadOnlyList<string> LastErrors
);

internal sealed class ReflectionDiagnostics
{
	private readonly object _syncRoot = new();
	private readonly List<ReflectionAccessError> _recentErrors = new();

	private int _warmupPlanCount;
	private int _warmupResolvedCount;
	private int _warmupRequiredFailureCount;
	private int _warmupOptionalFailureCount;
	private long _warmupDurationMs;
	private long _cacheHitCount;
	private long _cacheMissBlockedCount;
	private long _fallbackCount;

	public void RecordWarmup(ReflectionWarmupBatchResult result)
	{
		ArgumentNullException.ThrowIfNull(result);

		lock (_syncRoot)
		{
			_warmupPlanCount = result.PlanCount;
			_warmupResolvedCount = result.ResolvedCount;
			_warmupRequiredFailureCount = result.RequiredFailureCount;
			_warmupOptionalFailureCount = result.OptionalFailureCount;
			_warmupDurationMs = result.DurationMs;

			for (int i = 0; i < result.Errors.Count; i++)
			{
				AppendRecentError(result.Errors[i]);
			}
		}
	}

	public void RecordCacheHit()
	{
		Interlocked.Increment(ref _cacheHitCount);
	}

	public void RecordCacheMissBlocked(in ReflectionAccessError error)
	{
		Interlocked.Increment(ref _cacheMissBlockedCount);
		lock (_syncRoot)
		{
			AppendRecentError(error);
		}
	}

	public void RecordFallback(in ReflectionAccessError error)
	{
		Interlocked.Increment(ref _fallbackCount);
		lock (_syncRoot)
		{
			AppendRecentError(error);
		}
	}

	public ReflectionRuntimeSnapshot Snapshot()
	{
		lock (_syncRoot)
		{
			return new ReflectionRuntimeSnapshot(
				_warmupPlanCount,
				_warmupResolvedCount,
				_warmupRequiredFailureCount,
				_warmupOptionalFailureCount,
				_warmupDurationMs,
				Interlocked.Read(ref _cacheHitCount),
				Interlocked.Read(ref _cacheMissBlockedCount),
				Interlocked.Read(ref _fallbackCount),
				new ReadOnlyCollection<string>(BuildErrorStrings())
			);
		}
	}

	private List<string> BuildErrorStrings()
	{
		List<string> errors = new(_recentErrors.Count);
		for (int i = 0; i < _recentErrors.Count; i++)
		{
			errors.Add(_recentErrors[i].ToString());
		}

		return errors;
	}

	private void AppendRecentError(in ReflectionAccessError error)
	{
		const int maxErrors = 24;
		if (_recentErrors.Count >= maxErrors)
		{
			_recentErrors.RemoveAt(0);
		}

		_recentErrors.Add(error);
	}
}
