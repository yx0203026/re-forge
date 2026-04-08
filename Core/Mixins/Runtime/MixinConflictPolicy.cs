#nullable enable

using System;

namespace ReForgeFramework.Mixins.Runtime;

public enum MixinConflictResolution
{
	Fail = 0,
	Overwrite = 1,
	Skip = 2,
}

public enum MixinConflictType
{
	DuplicateInjection = 0,
	TargetSlotConflict = 1,
}

public sealed class MixinConflictPolicyOptions
{
	public MixinConflictResolution DuplicateResolution { get; init; } = MixinConflictResolution.Skip;

	public MixinConflictResolution TargetConflictResolution { get; init; } = MixinConflictResolution.Fail;
}

public sealed record MixinConflictContext(
	MixinConflictType ConflictType,
	MixinDescriptor Descriptor,
	InjectionDescriptor Injection,
	MixinAppliedEntry? ExistingEntry,
	string ConflictKey
);

public sealed record MixinConflictDecision(
	MixinConflictResolution Resolution,
	string Reason
);

internal sealed class MixinConflictPolicy
{
	private readonly MixinConflictPolicyOptions _options;

	public MixinConflictPolicy(MixinConflictPolicyOptions? options = null)
	{
		_options = options ?? new MixinConflictPolicyOptions();
	}

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
