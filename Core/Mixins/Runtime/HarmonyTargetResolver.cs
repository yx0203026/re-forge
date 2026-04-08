#nullable enable

using System;
using System.Collections.Generic;
using System.Reflection;

namespace ReForgeFramework.Mixins.Runtime;

internal sealed class HarmonyTargetResolver
{
	public bool TryResolveTargetMethod(
		MixinDescriptor descriptor,
		InjectionDescriptor injection,
		out MethodBase? targetMethod,
		out string error)
	{
		ArgumentNullException.ThrowIfNull(descriptor);
		ArgumentNullException.ThrowIfNull(injection);

		targetMethod = null;
		error = string.Empty;
		Type targetType = descriptor.TargetType;
		string targetMethodName = injection.TargetMethodName;

		if (string.IsNullOrWhiteSpace(targetMethodName))
		{
			error =
				$"Target method name is empty. mixin='{descriptor.MixinType.FullName}', targetType='{targetType.FullName}', descriptorKey='{injection.DescriptorKey}'.";
			return false;
		}

		if (injection.TargetMethod != null)
		{
			if (string.Equals(injection.TargetMethod.Name, targetMethodName, StringComparison.Ordinal)
				&& injection.TargetMethod.DeclaringType != null
				&& injection.TargetMethod.DeclaringType.IsAssignableFrom(targetType))
			{
				targetMethod = injection.TargetMethod;
				return true;
			}
		}

		MethodInfo[] methods = targetType.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
		List<MethodInfo> candidates = new();
		for (int i = 0; i < methods.Length; i++)
		{
			MethodInfo method = methods[i];
			if (!string.Equals(method.Name, targetMethodName, StringComparison.Ordinal))
			{
				continue;
			}

			candidates.Add(method);
		}

		if (candidates.Count == 0)
		{
			error =
				$"Target method not found. mixin='{descriptor.MixinType.FullName}', targetType='{targetType.FullName}', method='{targetMethodName}', descriptorKey='{injection.DescriptorKey}'.";
			return false;
		}

		candidates.Sort(static (a, b) => a.MetadataToken.CompareTo(b.MetadataToken));

		if (injection.Ordinal >= 0)
		{
			if (injection.Ordinal >= candidates.Count)
			{
				error =
					$"Target method ordinal out of range. mixin='{descriptor.MixinType.FullName}', targetType='{targetType.FullName}', method='{targetMethodName}', ordinal={injection.Ordinal}, candidateCount={candidates.Count}, descriptorKey='{injection.DescriptorKey}'.";
				return false;
			}

			targetMethod = candidates[injection.Ordinal];
			return true;
		}

		if (candidates.Count > 1)
		{
			error =
				$"Target method is ambiguous; set Ordinal explicitly. mixin='{descriptor.MixinType.FullName}', targetType='{targetType.FullName}', method='{targetMethodName}', candidateCount={candidates.Count}, descriptorKey='{injection.DescriptorKey}'.";
			return false;
		}

		targetMethod = candidates[0];
		return true;
	}
}
