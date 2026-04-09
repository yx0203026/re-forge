#nullable enable

using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace ReForgeFramework.Mixins.Runtime;

/// <summary>
/// Mixin 注册请求来源的枚举。标示注册由哪个模块发起。
/// </summary>
public enum MixinRegistrationSource
{
	/// <summary>
	/// MainClassExplicit：注册由 Mixin 主类显式发起，通常是在模组初始化时调用。
	/// </summary>
	MainClassExplicit = 0,
}

/// <summary>
/// Mixin 注册追踪状态的枚举。记录按 modId 维度的注册生命周期。
/// </summary>
public enum MixinRegistrationState
{
	/// <summary>
	/// NotRegistered：模组尚未进行 Mixin 注册或已完全卸载。
	/// </summary>
	NotRegistered = 0,

	/// <summary>
	/// Registered：模组已成功注册 Mixin 系统并处于活跃状态。
	/// </summary>
	Registered = 1,

	/// <summary>
	/// Unregistered：模组已从 Mixin 系统中注销（卸载）。
	/// </summary>
	Unregistered = 2,
}

/// <summary>
/// 主类发起 Mixin 注册时传入的配置选项。
/// </summary>
/// <remarks>
/// 此类包装了注册所需的核心信息：待扫描程序集、模组标识、Harmony 实例等。
/// 通常通过 <see cref="CreateMainClassOptions"/> 工厂方法创建。
/// </remarks>
public sealed class MixinRegistrationOptions
{
	/// <summary>
	/// 初始化 <see cref="MixinRegistrationOptions"/> 的新实例。
	/// </summary>
	/// <param name="assembly">包含 Mixin 类型的程序集，不可为 null。</param>
	/// <param name="modId">模组标识符，用于追踪与管理此注册，不可为 null 或空。</param>
	/// <param name="harmony">Harmony 实例，用于应用补丁，不可为 null。</param>
	/// <param name="strictMode">是否启用严格模式。严格模式下扫描错误会导致注册中止。</param>
	/// <param name="source">注册来源标记，默认为显式注册。</param>
	/// <exception cref="ArgumentNullException">当 assembly、modId、harmony 为 null 时。</exception>
	/// <exception cref="ArgumentException">当 modId 为空或仅为空白时。</exception>
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

	/// <summary>
	/// 获取包含 Mixin 类型定义的程序集。
	/// </summary>
	public Assembly Assembly { get; }

	/// <summary>
	/// 获取模组的唯一标识符。
	/// </summary>
	public string ModId { get; }

	/// <summary>
	/// 获取 Harmony 实例，用于补丁应用。
	/// </summary>
	public Harmony Harmony { get; }

	/// <summary>
	/// 获取是否启用严格模式。启用时，扫描或验证错误将导致注册中止。
	/// </summary>
	public bool StrictMode { get; }

	/// <summary>
	/// 获取注册来源标记。
	/// </summary>
	public MixinRegistrationSource Source { get; }

	/// <summary>
	/// 工厂方法：创建主类显式注册的标准配置。
	/// </summary>
	/// <param name="assembly">待扫描的程序集。</param>
	/// <param name="modId">模组标识符。</param>
	/// <param name="harmony">Harmony 实例。</param>
	/// <param name="strictMode">是否启用严格模式。</param>
	/// <returns>新建的 <see cref="MixinRegistrationOptions"/> 实例。</returns>
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
/// 单次 Mixin 注册操作的统计摘要。
/// </summary>
public readonly record struct MixinRegistrationSummary(
	/// <summary>成功安装的 Mixin 数量。</summary>
	int Installed,

	/// <summary>安装失败的 Mixin 数量。</summary>
	int Failed,

	/// <summary>被跳过的 Mixin 数量。</summary>
	int Skipped
);

/// <summary>
/// 单个模组的最新 Mixin 注册结果。
/// </summary>
public sealed record MixinRegistrationResult(
	/// <summary>模组标识符。</summary>
	string ModId,

	/// <summary>注册来源标记。</summary>
	MixinRegistrationSource Source,

	/// <summary>注册状态。</summary>
	MixinRegistrationState State,

	/// <summary>此注册是否采用严格模式。</summary>
	bool StrictMode,

	/// <summary>注册结果摘要统计。</summary>
	MixinRegistrationSummary Summary,

	/// <summary>注册结果说明文本。</summary>
	string Message,

	/// <summary>注册操作时间戳（UTC）。</summary>
	DateTimeOffset TimestampUtc
);

/// <summary>
/// Mixin 卸载操作的结果记录。
/// </summary>
public sealed record MixinUnregisterResult(
	/// <summary>被卸载的模组标识符。</summary>
	string ModId,

	/// <summary>是否成功移除了 Mixin。</summary>
	bool Removed,

	/// <summary>被移除的已安装 Mixin 数量。</summary>
	int RemovedInstalledCount,

	/// <summary>被移除的失败 Mixin 数量。</summary>
	int RemovedFailedCount,

	/// <summary>卸载结果说明文本。</summary>
	string Message,

	/// <summary>卸载操作时间戳（UTC）。</summary>
	DateTimeOffset TimestampUtc
);

/// <summary>
/// Mixin 系统对外暴露的注册状态快照。主要用于调试、诊断与信息查询。
/// </summary>
public sealed class MixinStatusSnapshot
{
	/// <summary>
	/// 初始化 <see cref="MixinStatusSnapshot"/> 的新实例。
	/// </summary>
	/// <param name="isExplicitlyRegistered">是否有模组已显式注册 Mixin。</param>
	/// <param name="registeredModCount">当前已注册的模组数量。</param>
	/// <param name="registrations">所有注册的模组及其结果字典（按 modId 索引）。</param>
	/// <exception cref="ArgumentNullException">当 registrations 为 null 时。</exception>
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

	/// <summary>
	/// 获取是否有模组已显式注册 Mixin 系统。
	/// </summary>
	public bool IsExplicitlyRegistered { get; }

	/// <summary>
	/// 获取当前已注册的模组总数。
	/// </summary>
	public int RegisteredModCount { get; }

	/// <summary>
	/// 获取所有已注册模组的结果字典（按 modId 索引）。
	/// </summary>
	public IReadOnlyDictionary<string, MixinRegistrationResult> Registrations { get; }
}
