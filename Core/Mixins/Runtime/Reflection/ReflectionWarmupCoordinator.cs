#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;

namespace ReForgeFramework.Mixins.Runtime.Reflection;

internal sealed class ReflectionWarmupCoordinator : IReflectionAccessGate
{
	private readonly object _syncRoot = new();
	private readonly Dictionary<string, ReflectionWarmupPlan> _plansById = new(StringComparer.Ordinal);
	private readonly Dictionary<ReflectionMemberKey, bool> _requiredByMember = new();
	private readonly HashSet<ReflectionMemberKey> _prewarmedMembers = new();
	private readonly ReflectionDiagnostics _diagnostics;
	private readonly ReflectionAccessor _accessor;

	private bool _runtimeGuardEnabled;

	public ReflectionWarmupCoordinator()
	{
		_diagnostics = new ReflectionDiagnostics();
		_accessor = new ReflectionAccessor(new ReflectionAccessCache(_diagnostics, this));
	}

	public IReflectionAccessor Accessor => _accessor;

	public ReflectionDiagnostics Diagnostics => _diagnostics;

	public ReflectionRuntimeSnapshot GetRuntimeSnapshot()
	{
		return _diagnostics.Snapshot();
	}

	public void RegisterPlan(ReflectionWarmupPlan plan)
	{
		ArgumentNullException.ThrowIfNull(plan);

		lock (_syncRoot)
		{
			_plansById[plan.PlanId] = plan;
			MergeMemberRequirement(plan.RequiredMembers, required: true);
			MergeMemberRequirement(plan.OptionalMembers, required: false);
		}
	}

	public void RegisterPlansFromMixins(IReadOnlyList<global::ReForgeFramework.Mixins.Runtime.MixinDescriptor> descriptors)
	{
		ArgumentNullException.ThrowIfNull(descriptors);

		for (int i = 0; i < descriptors.Count; i++)
		{
			global::ReForgeFramework.Mixins.Runtime.MixinDescriptor descriptor = descriptors[i];
			List<ReflectionMemberKey> required = new();
			List<ReflectionMemberKey> optional = new();

			for (int injectionIndex = 0; injectionIndex < descriptor.Injections.Count; injectionIndex++)
			{
				global::ReForgeFramework.Mixins.Runtime.InjectionDescriptor injection = descriptor.Injections[injectionIndex];
				string methodName = string.IsNullOrWhiteSpace(injection.TargetMethodName)
					? injection.TargetMethod.Name
					: injection.TargetMethodName;
				string signatureKey = string.IsNullOrWhiteSpace(injection.TargetMethodSignatureKey)
					? ReflectionSignatureBuilder.BuildMethodSignature(injection.TargetMethod)
					: injection.TargetMethodSignatureKey;
				ReflectionMemberKey key = new(
					descriptor.TargetType,
					methodName,
					ReflectionMemberKind.Method,
					signatureKey,
					injection.Ordinal
				);

				if (injection.Optional)
				{
					optional.Add(key);
				}
				else
				{
					required.Add(key);
				}
			}

			for (int shadowIndex = 0; shadowIndex < descriptor.ShadowFields.Count; shadowIndex++)
			{
				global::ReForgeFramework.Mixins.Runtime.ShadowFieldDescriptor shadow = descriptor.ShadowFields[shadowIndex];
				ReflectionMemberKey mainKey = new(
					descriptor.TargetType,
					shadow.TargetName,
					ReflectionMemberKind.Field
				);
				if (shadow.Optional)
				{
					optional.Add(mainKey);
				}
				else
				{
					required.Add(mainKey);
				}

				for (int aliasIndex = 0; aliasIndex < shadow.Aliases.Count; aliasIndex++)
				{
					string alias = shadow.Aliases[aliasIndex];
					if (string.IsNullOrWhiteSpace(alias))
					{
						continue;
					}

					ReflectionMemberKey aliasKey = new(
						descriptor.TargetType,
						alias,
						ReflectionMemberKind.Field
					);
					optional.Add(aliasKey);
				}
			}

			RegisterPlan(new ReflectionWarmupPlan(
				descriptor.DescriptorKey,
				descriptor.MixinId,
				new ReadOnlyCollection<ReflectionMemberKey>(required),
				new ReadOnlyCollection<ReflectionMemberKey>(optional)
			));
		}
	}

	public ReflectionWarmupBatchResult WarmupAll()
	{
		List<ReflectionWarmupPlan> plans;
		lock (_syncRoot)
		{
			_runtimeGuardEnabled = false;
			plans = new List<ReflectionWarmupPlan>(_plansById.Values);
		}

		plans.Sort(static (a, b) => string.CompareOrdinal(a.PlanId, b.PlanId));
		Stopwatch stopwatch = Stopwatch.StartNew();
		List<ReflectionWarmupPlanResult> planResults = new(plans.Count);
		List<ReflectionAccessError> allErrors = new();
		int resolvedCount = 0;
		int requiredFailures = 0;
		int optionalFailures = 0;

		for (int i = 0; i < plans.Count; i++)
		{
			ReflectionWarmupPlan plan = plans[i];
			Stopwatch planWatch = Stopwatch.StartNew();
			int planResolved = 0;
			int planRequiredFailures = 0;
			int planOptionalFailures = 0;
			List<ReflectionAccessError> errors = new();

			WarmupPlanMembers(
				plan,
				plan.RequiredMembers,
				required: true,
				ref planResolved,
				ref planRequiredFailures,
				ref planOptionalFailures,
				errors
			);
			WarmupPlanMembers(
				plan,
				plan.OptionalMembers,
				required: false,
				ref planResolved,
				ref planRequiredFailures,
				ref planOptionalFailures,
				errors
			);

			resolvedCount += planResolved;
			requiredFailures += planRequiredFailures;
			optionalFailures += planOptionalFailures;
			allErrors.AddRange(errors);

			planResults.Add(new ReflectionWarmupPlanResult(
				plan.PlanId,
				plan.Owner,
				planResolved,
				planRequiredFailures,
				planOptionalFailures,
				planWatch.ElapsedMilliseconds,
				new ReadOnlyCollection<ReflectionAccessError>(errors)
			));
		}

		stopwatch.Stop();
		ReflectionWarmupBatchResult result = new(
			plans.Count,
			resolvedCount,
			requiredFailures,
			optionalFailures,
			stopwatch.ElapsedMilliseconds,
			new ReadOnlyCollection<ReflectionWarmupPlanResult>(planResults),
			new ReadOnlyCollection<ReflectionAccessError>(allErrors)
		);

		_diagnostics.RecordWarmup(result);
		lock (_syncRoot)
		{
			_runtimeGuardEnabled = true;
		}

		return result;
	}

	public bool TryAuthorize(
		in ReflectionMemberKey key,
		in ReflectionAccessContext context,
		out ReflectionAccessError? error)
	{
		bool guardEnabled;
		bool isPrewarmed;
		bool required = true;
		lock (_syncRoot)
		{
			guardEnabled = _runtimeGuardEnabled;
			isPrewarmed = _prewarmedMembers.Contains(key);
			if (_requiredByMember.TryGetValue(key, out bool memberRequired))
			{
				required = memberRequired;
			}
		}

		if (!guardEnabled || isPrewarmed)
		{
			error = null;
			return true;
		}

		string message = required
			? "Runtime reflection request blocked because member was not prewarmed."
			: "Runtime reflection request degraded because optional member was not prewarmed.";
		error = new ReflectionAccessError(
			required ? ReflectionErrorCode.MemberNotFound : ReflectionErrorCode.UnsupportedOperation,
			message,
			key.DeclaringType,
			key.MemberName,
			key.MemberKind,
			key.SignatureKey,
			context
		);

		if (required)
		{
			_diagnostics.RecordCacheMissBlocked(error.Value);
		}
		else
		{
			_diagnostics.RecordFallback(error.Value);
		}

		return false;
	}

	private void WarmupPlanMembers(
		in ReflectionWarmupPlan plan,
		IReadOnlyList<ReflectionMemberKey> members,
		bool required,
		ref int planResolved,
		ref int planRequiredFailures,
		ref int planOptionalFailures,
		List<ReflectionAccessError> errors)
	{
		for (int memberIndex = 0; memberIndex < members.Count; memberIndex++)
		{
			ReflectionMemberKey key = members[memberIndex];
			ReflectionAccessContext context = new(
				Owner: plan.Owner,
				Operation: "warmup",
				DescriptorKey: plan.PlanId,
				Notes: required ? "required" : "optional"
			);

			if (TryWarmupMember(key, context, out ReflectionAccessError? error))
			{
				lock (_syncRoot)
				{
					_prewarmedMembers.Add(key);
				}
				planResolved++;
				continue;
			}

			if (error != null)
			{
				errors.Add(error.Value);
			}

			if (required)
			{
				planRequiredFailures++;
			}
			else
			{
				planOptionalFailures++;
			}
		}
	}

	private bool TryWarmupMember(
		in ReflectionMemberKey key,
		in ReflectionAccessContext context,
		out ReflectionAccessError? error)
	{
		switch (key.MemberKind)
		{
			case ReflectionMemberKind.Method:
				return _accessor.TryResolveMethod(key, context, out _, out error);
			case ReflectionMemberKind.Field:
				return _accessor.TryResolveField(key, context, out _, out error);
			case ReflectionMemberKind.Property:
				return _accessor.TryResolveProperty(key, context, out _, out error);
			default:
				error = new ReflectionAccessError(
					ReflectionErrorCode.InvalidRequest,
					$"Unsupported member kind for warmup: {key.MemberKind}.",
					key.DeclaringType,
					key.MemberName,
					key.MemberKind,
					key.SignatureKey,
					context
				);
				return false;
		}
	}

	private void MergeMemberRequirement(IReadOnlyList<ReflectionMemberKey> members, bool required)
	{
		for (int i = 0; i < members.Count; i++)
		{
			ReflectionMemberKey key = members[i];
			if (_requiredByMember.TryGetValue(key, out bool existingRequired))
			{
				_requiredByMember[key] = existingRequired || required;
				continue;
			}

			_requiredByMember[key] = required;
		}
	}
}
