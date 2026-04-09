#nullable enable

using System;

namespace ReForgeFramework.Mixins.Runtime;

/// <summary>
/// Mixin 冲突解决策略的枚举。定义当检测到冲突时系统应采取的行动。
/// </summary>
public enum MixinConflictResolution
{
	/// <summary>
	/// Fail：中止注册过程并报告错误。用于关键冲突不容许忽略的情景。
	/// </summary>
	Fail = 0,

	/// <summary>
	/// Overwrite：使用后来的 Mixin 覆盖已有的补丁。仅在确保兼容的情况下使用。
	/// </summary>
	Overwrite = 1,

	/// <summary>
	/// Skip：忽略新注入，保持现有补丁不变。当多个 Mixin 提供相同功能时适用。
	/// </summary>
	Skip = 2,
}

/// <summary>
/// Mixin 冲突类型的枚举。标示冲突发生的场景。
/// </summary>
public enum MixinConflictType
{
	/// <summary>
	/// DuplicateInjection：两个 Mixin 尝试对同一方法注入相同类型的补丁。
	/// </summary>
	DuplicateInjection = 0,

	/// <summary>
	/// TargetSlotConflict：多个 Mixin 尝试使用同一注入点（同一目标方法的同一位置）。
	/// </summary>
	TargetSlotConflict = 1,
}

/// <summary>
/// Mixin 冲突策略的配置选项。
/// </summary>
public sealed class MixinConflictPolicyOptions
{
	/// <summary>
	/// 获取或设置当检测到重复注入时的处理策略。默认为 <see cref="MixinConflictResolution.Skip"/>。
	/// </summary>
	public MixinConflictResolution DuplicateResolution { get; init; } = MixinConflictResolution.Skip;

	/// <summary>
	/// 获取或设置当检测到目标槽位冲突时的处理策略。默认为 <see cref="MixinConflictResolution.Fail"/>。
	/// </summary>
	public MixinConflictResolution TargetConflictResolution { get; init; } = MixinConflictResolution.Fail;
}

/// <summary>
/// Mixin 冲突的上下文信息。包含冲突相关的所有元数据。
/// </summary>
public sealed record MixinConflictContext(
	/// <summary>冲突类型。</summary>
	MixinConflictType ConflictType,
	
	/// <summary>当前尝试注册的 Mixin 描述符。</summary>
	MixinDescriptor Descriptor,
	
	/// <summary>当前尝试注入的注射描述符。</summary>
	InjectionDescriptor Injection,
	
	/// <summary>已存在的相冲突的注入记录（若有）。</summary>
	MixinAppliedEntry? ExistingEntry,
	
	/// <summary>用于冲突判定的键值，通常为目标方法的标识。</summary>
	string ConflictKey
);

/// <summary>
/// Mixin 冲突的处理决策。
/// </summary>
public sealed record MixinConflictDecision(
	/// <summary>采用的解决策略。</summary>
	MixinConflictResolution Resolution,
	
	/// <summary>说明此决策的原因文本。</summary>
	string Reason
);

/// <summary>
/// Mixin 冲突策略评估器。根据配置和冲突上下文生成决策。
/// </summary>
internal sealed class MixinConflictPolicy
{
	private readonly MixinConflictPolicyOptions _options;

	/// <summary>
	/// 初始化 <see cref="MixinConflictPolicy"/> 的新实例。
	/// </summary>
	/// <param name="options">冲突处理配置，默认使用标准配置。</param>
	public MixinConflictPolicy(MixinConflictPolicyOptions? options = null)
	{
		_options = options ?? new MixinConflictPolicyOptions();
	}

	/// <summary>
	/// 根据冲突上下文评估并生成处理决策。
	/// </summary>
	/// <param name="context">包含冲突信息的上下文。</param>
	/// <returns>包含决策和原因的 <see cref="MixinConflictDecision"/> 对象。</returns>
	/// <exception cref="ArgumentNullException">当 context 为 null 时。</exception>
	public MixinConflictDecision Evaluate(MixinConflictContext context)
	{
		ArgumentNullException.ThrowIfNull(context);

		return context.ConflictType switch
		{
			MixinConflictType.DuplicateInjection => new MixinConflictDecision(
				_options.DuplicateResolution,
				$"Duplicate injection detected. injectionKey='{context.Injection.DescriptorKey}', conflictKey='{context.ConflictKey}'."
			),
			MixinConflictType.TargetSlotConflict => new MixinConflictDecision(
				_options.TargetConflictResolution,
				$"Target slot conflict detected. injectionKey='{context.Injection.DescriptorKey}', conflictKey='{context.ConflictKey}', existingInjectionKey='{context.ExistingEntry?.InjectionDescriptorKey}'."
			),
			_ => new MixinConflictDecision(MixinConflictResolution.Fail, "Unknown conflict type."),
		};
	}
}
