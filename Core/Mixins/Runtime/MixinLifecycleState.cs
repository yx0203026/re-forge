#nullable enable

using System;
using System.Collections.Generic;

namespace ReForgeFramework.Mixins.Runtime;

/// <summary>
/// Mixin 生命周期状态枚举。标示单个 Mixin 模组在安装、运行到卸载过程中的状态。
/// </summary>
public enum MixinLifecycleState
{
	/// <summary>
	/// NotInstalled：Mixin 尚未安装或初始化。这是初始状态。
	/// </summary>
	NotInstalled = 0,

	/// <summary>
	/// Installing：Mixin 正在安装过程中。表示初始化与补丁应用正在进行。
	/// </summary>
	Installing = 1,

	/// <summary>
	/// Active：Mixin 已成功安装并处于活跃状态。所有补丁都已应用。
	/// </summary>
	Active = 2,

	/// <summary>
	/// Failed：Mixin 安装失败。可能是由于扫描错误、验证失败或冲突导致。
	/// </summary>
	Failed = 3,

	/// <summary>
	/// Unloading：Mixin 正在卸载过程中。补丁正在被移除。
	/// </summary>
	Unloading = 4,

	/// <summary>
	/// Unloaded：Mixin 已成功卸载。所有补丁都已被移除。
	/// </summary>
	Unloaded = 5,
}

/// <summary>
/// Mixin 生命周期过程中累积的统计计数。
/// </summary>
public readonly record struct MixinLifecycleCounters(
	/// <summary>成功安装的注入数量。</summary>
	int Installed,

	/// <summary>安装失败的注入数量。</summary>
	int Failed,

	/// <summary>被跳过（未应用）的注入数量。</summary>
	int Skipped,

	/// <summary>扫描阶段发生的错误数量。</summary>
	int ScannerErrors,

	/// <summary>扫描阶段发生的警告数量。</summary>
	int ScannerWarnings,

	/// <summary>卸载阶段失败的补丁数量。</summary>
	int UnpatchFailures,

	/// <summary>预热阶段成功解析的反射成员数量。</summary>
	int WarmupResolved = 0,

	/// <summary>预热阶段必需成员失败数量。</summary>
	int WarmupRequiredFailures = 0,

	/// <summary>预热阶段可选成员失败数量。</summary>
	int WarmupOptionalFailures = 0,

	/// <summary>预热阶段总耗时（毫秒）。</summary>
	long WarmupDurationMs = 0
);

/// <summary>
/// 单个 Mixin 模组的完整生命周期状态快照。
/// </summary>
public sealed record MixinModLifecycleStatus(
	/// <summary>模组标识符。</summary>
	string ModId,

	/// <summary>当前生命周期状态。</summary>
	MixinLifecycleState State,

	/// <summary>是否启用严格模式。</summary>
	bool StrictMode,

	/// <summary>关联的 Harmony 实例 ID。</summary>
	string HarmonyId,

	/// <summary>Mixin 所在程序集名称。</summary>
	string AssemblyName,

	/// <summary>生命周期统计计数器。</summary>
	MixinLifecycleCounters Counters,

	/// <summary>状态说明文本。</summary>
	string Message,

	/// <summary>最后更新时间（UTC）。</summary>
	DateTimeOffset UpdatedAtUtc
);

/// <summary>
/// 所有已注册 Mixin 模组的生命周期状态快照。
/// </summary>
public sealed class MixinLifecycleSnapshot
{
	/// <summary>
	/// 初始化 <see cref="MixinLifecycleSnapshot"/> 的新实例。
	/// </summary>
	/// <param name="mods">按 modId 映射的生命周期状态字典。</param>
	/// <exception cref="ArgumentNullException">当 mods 为 null 时。</exception>
	public MixinLifecycleSnapshot(IReadOnlyDictionary<string, MixinModLifecycleStatus> mods)
	{
		ArgumentNullException.ThrowIfNull(mods);
		Mods = mods;
	}

	/// <summary>
	/// 获取所有已注册模组的生命周期状态字典（按 modId 索引）。
	/// </summary>
	public IReadOnlyDictionary<string, MixinModLifecycleStatus> Mods { get; }
}

/// <summary>
/// Mixin 安装操作的结果。
/// </summary>
public sealed record MixinLifecycleInstallResult(
	/// <summary>被安装的模组标识符。</summary>
	string ModId,

	/// <summary>安装后的生命周期状态。</summary>
	MixinLifecycleState State,

	/// <summary>是否为空操作（即已存在相同的活跃 Mixin）。</summary>
	bool NoOp,

	/// <summary>是否因严格模式由于错误而中止。</summary>
	bool AbortedByStrictMode,

	/// <summary>安装过程中的统计计数器。</summary>
	MixinLifecycleCounters Counters,

	/// <summary>安装结果说明文本。</summary>
	string Message,

	/// <summary>操作时间戳（UTC）。</summary>
	DateTimeOffset TimestampUtc
);

/// <summary>
/// Mixin 卸载操作的结果。
/// </summary>
public sealed record MixinLifecycleUnloadResult(
	/// <summary>被卸载的模组标识符。</summary>
	string ModId,

	/// <summary>卸载后的生命周期状态。</summary>
	MixinLifecycleState State,

	/// <summary>是否为空操作（即 Mixin 未处于活跃状态）。</summary>
	bool NoOp,

	/// <summary>成功移除的补丁数量。</summary>
	int RemovedCount,

	/// <summary>卸载过程中失败的补丁数量。</summary>
	int UnpatchFailures,

	/// <summary>卸载结果说明文本。</summary>
	string Message,

	/// <summary>操作时间戳（UTC）。</summary>
	DateTimeOffset TimestampUtc
);
