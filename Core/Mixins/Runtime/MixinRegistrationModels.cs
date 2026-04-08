#nullable enable

using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace ReForgeFramework.Mixins.Runtime;

/// <summary>
/// 标记 Mixin 注册请求来自何处，当前仅允许主类显式注册。
/// </summary>
public enum MixinRegistrationSource
{
	MainClassExplicit = 0,
}

/// <summary>
/// Mixin 子系统生命周期状态（按 modId 维度）。
/// </summary>
public enum MixinRegistrationState
{
	NotRegistered = 0,
	Registered = 1,
	Unregistered = 2,
}

/// <summary>
/// 主类发起 Mixin 注册时传入的配置。
/// </summary>
public sealed class MixinRegistrationOptions
{
	public MixinRegistrationOptions(
		Assembly assembly,
		string modId,
		Harmony harmony,
		bool strictMode = true,
		MixinRegistrationSource source = MixinRegistrationSource.MainClassExplicit)
	{
		ArgumentNullException.ThrowIfNull(assembly);
		ArgumentNullException.ThrowIfNull(modId);
		ArgumentNullException.ThrowIfNull(harmony);
		if (string.IsNullOrWhiteSpace(modId))
		{
			throw new ArgumentException("Value cannot be empty.", nameof(modId));
		}

		Assembly = assembly;
		ModId = modId;
		Harmony = harmony;
		StrictMode = strictMode;
		Source = source;
	}

	public Assembly Assembly { get; }

	public string ModId { get; }

	public Harmony Harmony { get; }

	public bool StrictMode { get; }

	public MixinRegistrationSource Source { get; }

	public static MixinRegistrationOptions CreateMainClassOptions(
		Assembly assembly,
		string modId,
		Harmony harmony,
		bool strictMode = true)
	{
		return new MixinRegistrationOptions(
			assembly,
			modId,
			harmony,
			strictMode,
			MixinRegistrationSource.MainClassExplicit
		);
	}
}

/// <summary>
/// 单次注册结果摘要，便于主类输出安装统计。
/// </summary>
public readonly record struct MixinRegistrationSummary(int Installed, int Failed, int Skipped);

/// <summary>
/// 单个 modId 的最新注册结果。
/// </summary>
public sealed record MixinRegistrationResult(
	string ModId,
	MixinRegistrationSource Source,
	MixinRegistrationState State,
	bool StrictMode,
	MixinRegistrationSummary Summary,
	string Message,
	DateTimeOffset TimestampUtc
);

/// <summary>
/// 卸载结果摘要。
/// </summary>
public sealed record MixinUnregisterResult(
	string ModId,
	bool Removed,
	int RemovedInstalledCount,
	int RemovedFailedCount,
	string Message,
	DateTimeOffset TimestampUtc
);

/// <summary>
/// 对外状态快照，用于调试与诊断查询。
/// </summary>
public sealed class MixinStatusSnapshot
{
	public MixinStatusSnapshot(
		bool isExplicitlyRegistered,
		int registeredModCount,
		IReadOnlyDictionary<string, MixinRegistrationResult> registrations)
	{
		ArgumentNullException.ThrowIfNull(registrations);

		IsExplicitlyRegistered = isExplicitlyRegistered;
		RegisteredModCount = registeredModCount;
		Registrations = registrations;
	}

	public bool IsExplicitlyRegistered { get; }

	public int RegisteredModCount { get; }

	public IReadOnlyDictionary<string, MixinRegistrationResult> Registrations { get; }
}
