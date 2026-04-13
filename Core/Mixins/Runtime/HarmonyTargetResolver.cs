#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;

namespace ReForgeFramework.Mixins.Runtime;

/// <summary>
/// Harmony 目标解析器：负责在目标类型中定位与验证注入的目标方法。
/// </summary>
/// <remarks>
/// 此解析器提供健壮的方法查询功能，支持：
/// • 按名称查询方法
/// • 处理同名方法的多个重载
/// • 通过序号明确指定目标
/// • 详细的错误报告
/// 
/// 解析策略：
/// 1. 首先尝试加速路径（如果已提前验证）
/// 2. 按方法名收集候选方法
/// 3. 按 metadata token 排序以确保一致性
/// 4. 通过 Ordinal 索引选中目标
/// </remarks>
internal sealed class HarmonyTargetResolver
{
	private static readonly ConcurrentDictionary<Type, Dictionary<string, IReadOnlyList<MethodInfo>>> _methodCandidatesByType = new();

	/// <summary>
	/// 在目标类型中解析指定的目标方法。
	/// </summary>
	/// <param name="descriptor">Mixin 描述符。</param>
	/// <param name="injection">注入描述符，包含目标方法信息。</param>
	/// <param name="targetMethod">解析出的目标方法（若成功）。</param>
	/// <param name="error">解析失败时的错误说明（若失败）。</param>
	/// <returns>是否成功解析目标方法。</returns>
	/// <remarks>
	/// 解析步骤：
	/// 1. 验证目标方法名非空
	/// 2. 尝试加速路径（已验证方法）
	/// 3. 收集同名方法候选
	/// 4. 检查序号与候选数量
	/// 5. 排序并返回对应目标
	/// </remarks>
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

		Dictionary<string, IReadOnlyList<MethodInfo>> methodIndex = GetOrBuildMethodIndex(targetType);
		if (!methodIndex.TryGetValue(targetMethodName, out IReadOnlyList<MethodInfo>? candidates))
		{
			candidates = Array.Empty<MethodInfo>();
		}

		if (candidates.Count == 0)
		{
			error =
				$"Target method not found. mixin='{descriptor.MixinType.FullName}', targetType='{targetType.FullName}', method='{targetMethodName}', descriptorKey='{injection.DescriptorKey}'.";
			return false;
		}

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

	private static Dictionary<string, IReadOnlyList<MethodInfo>> GetOrBuildMethodIndex(Type targetType)
	{
		return _methodCandidatesByType.GetOrAdd(targetType, static type =>
		{
			MethodInfo[] methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
			Dictionary<string, List<MethodInfo>> groups = new(StringComparer.Ordinal);

			for (int i = 0; i < methods.Length; i++)
			{
				MethodInfo method = methods[i];
				if (!groups.TryGetValue(method.Name, out List<MethodInfo>? bucket))
				{
					bucket = new List<MethodInfo>();
					groups[method.Name] = bucket;
				}

				bucket.Add(method);
			}

			Dictionary<string, IReadOnlyList<MethodInfo>> result = new(StringComparer.Ordinal);
			foreach ((string name, List<MethodInfo> bucket) in groups)
			{
				bucket.Sort(static (a, b) => a.MetadataToken.CompareTo(b.MetadataToken));
				result[name] = bucket;
			}

			return result;
		});
	}
}
