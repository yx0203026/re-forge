#nullable enable

using System;
using System.Collections.Generic;
using System.Reflection;

namespace ReForgeFramework.Mixins.Runtime;

internal sealed record ValidatedMixinInjection(
	MethodInfo HandlerMethod,
	MethodInfo TargetMethod,
	global::ReForge.InjectionAttributeBase Attribute,
	int? ArgumentIndex,
	string ConstantExpression
);

internal sealed class MixinValidation
{
	public bool TryValidateMixinType(
		Type mixinType,
		global::ReForge.MixinAttribute mixinAttribute,
		out string error)
	{
		ArgumentNullException.ThrowIfNull(mixinType);
		ArgumentNullException.ThrowIfNull(mixinAttribute);

		error = string.Empty;
		if (!mixinType.IsClass)
		{
			error = $"Mixin type must be class. mixinType='{mixinType.FullName}'.";
			return false;
		}

		if (mixinAttribute.TargetType == null)
		{
			error = $"Mixin target type cannot be null. mixinType='{mixinType.FullName}'.";
			return false;
		}

		if (mixinAttribute.TargetType.ContainsGenericParameters)
		{
			error = $"Mixin target type cannot be open generic. mixinType='{mixinType.FullName}', targetType='{mixinAttribute.TargetType.FullName}'.";
			return false;
		}

		return true;
	}

	public bool TryValidateInjection(
		Type mixinType,
		MethodInfo handlerMethod,
		global::ReForge.InjectionAttributeBase attribute,
		Type targetType,
		out ValidatedMixinInjection? validated,
		out string error)
	{
		ArgumentNullException.ThrowIfNull(mixinType);
		ArgumentNullException.ThrowIfNull(handlerMethod);
		ArgumentNullException.ThrowIfNull(attribute);
		ArgumentNullException.ThrowIfNull(targetType);

		validated = null;
		error = string.Empty;
		string methodId = $"{mixinType.FullName}.{handlerMethod.Name}";

		if (!handlerMethod.IsStatic)
		{
			error = $"Injection handler must be static. method='{methodId}', kind='{attribute.Kind}'.";
			return false;
		}

		if (handlerMethod.ContainsGenericParameters)
		{
			error = $"Injection handler cannot be generic. method='{methodId}'.";
			return false;
		}

		ParameterInfo[] handlerParameters = handlerMethod.GetParameters();
		for (int i = 0; i < handlerParameters.Length; i++)
		{
			if (handlerParameters[i].ParameterType.IsByRef)
			{
				error = $"Injection handler parameter cannot be ref/out. method='{methodId}', parameter='{handlerParameters[i].Name}'.";
				return false;
			}
		}

		if (!TryResolveTargetMethod(targetType, attribute.TargetMethod, attribute.Ordinal, out MethodInfo? targetMethodCandidate, out string resolveError))
		{
			error = $"{resolveError} method='{methodId}', kind='{attribute.Kind}'.";
			return false;
		}

		if (targetMethodCandidate == null)
		{
			error = $"Target method resolution returned null unexpectedly. method='{methodId}', kind='{attribute.Kind}'.";
			return false;
		}

		MethodInfo targetMethod = targetMethodCandidate;

		int? argumentIndex = null;
		string constantExpression = string.Empty;
		if (attribute is global::ReForge.ModifyArgAttribute modifyArgAttribute)
		{
			ParameterInfo[] targetParameters = targetMethod.GetParameters();
			if (modifyArgAttribute.ArgumentIndex < 0 || modifyArgAttribute.ArgumentIndex >= targetParameters.Length)
			{
				error =
					$"ModifyArg index out of range. method='{methodId}', target='{targetType.FullName}.{targetMethod.Name}', index={modifyArgAttribute.ArgumentIndex}, parameterCount={targetParameters.Length}.";
				return false;
			}

			Type expectedType = targetParameters[modifyArgAttribute.ArgumentIndex].ParameterType;
			if (handlerMethod.ReturnType != expectedType)
			{
				error =
					$"ModifyArg handler must return target parameter type. method='{methodId}', expectedReturn='{expectedType.FullName}', actualReturn='{handlerMethod.ReturnType.FullName}'.";
				return false;
			}

			argumentIndex = modifyArgAttribute.ArgumentIndex;
		}

		if (attribute is global::ReForge.ModifyConstantAttribute modifyConstantAttribute)
		{
			if (handlerMethod.ReturnType == typeof(void))
			{
				error = $"ModifyConstant handler must return a value. method='{methodId}'.";
				return false;
			}

			constantExpression = modifyConstantAttribute.ConstantExpression;
		}

		if (attribute is global::ReForge.OverwriteAttribute)
		{
			if (handlerMethod.ReturnType != targetMethod.ReturnType)
			{
				error =
					$"Overwrite return type mismatch. method='{methodId}', targetReturn='{targetMethod.ReturnType.FullName}', actualReturn='{handlerMethod.ReturnType.FullName}'.";
				return false;
			}

			if (handlerMethod.GetParameters().Length != targetMethod.GetParameters().Length)
			{
				error =
					$"Overwrite parameter count mismatch. method='{methodId}', target='{targetMethod.DeclaringType?.FullName}.{targetMethod.Name}', expected={targetMethod.GetParameters().Length}, actual={handlerMethod.GetParameters().Length}.";
				return false;
			}
		}

		validated = new ValidatedMixinInjection(handlerMethod, targetMethod, attribute, argumentIndex, constantExpression);
		return true;
	}

	private static bool TryResolveTargetMethod(
		Type targetType,
		string targetMethodName,
		int ordinal,
		out MethodInfo? targetMethod,
		out string error)
	{
		ArgumentNullException.ThrowIfNull(targetType);
		ArgumentNullException.ThrowIfNull(targetMethodName);

		targetMethod = null;
		error = string.Empty;
		if (string.IsNullOrWhiteSpace(targetMethodName))
		{
			error = $"Target method name cannot be empty. targetType='{targetType.FullName}'.";
			return false;
		}

		List<MethodInfo> candidates = new();
		MethodInfo[] methods = targetType.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
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
			error = $"Target method not found. targetType='{targetType.FullName}', method='{targetMethodName}'.";
			return false;
		}

		candidates.Sort(static (a, b) =>
		{
			int byName = string.CompareOrdinal(a.Name, b.Name);
			if (byName != 0)
			{
				return byName;
			}

			return a.MetadataToken.CompareTo(b.MetadataToken);
		});

		if (ordinal >= 0)
		{
			if (ordinal >= candidates.Count)
			{
				error =
					$"Target method ordinal out of range. targetType='{targetType.FullName}', method='{targetMethodName}', ordinal={ordinal}, candidateCount={candidates.Count}.";
				return false;
			}

			targetMethod = candidates[ordinal];
			return true;
		}

		if (candidates.Count > 1)
		{
			error =
				$"Target method is ambiguous; specify Ordinal. targetType='{targetType.FullName}', method='{targetMethodName}', candidateCount={candidates.Count}.";
			return false;
		}

		targetMethod = candidates[0];
		return true;
	}
}
