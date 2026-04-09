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
		if (RequiresNonByRefHandlerParameters(attribute.Kind))
		{
			for (int i = 0; i < handlerParameters.Length; i++)
			{
				if (handlerParameters[i].ParameterType.IsByRef)
				{
					error = $"Injection handler parameter cannot be ref/out for this injection kind. method='{methodId}', kind='{attribute.Kind}', parameter='{handlerParameters[i].Name}'.";
					return false;
				}
			}
		}

		if (!TryResolveTargetMethod(targetType, attribute, handlerMethod, out MethodInfo? targetMethodCandidate, out string resolveError))
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
			if (expectedType.IsByRef)
			{
				error =
					$"ModifyArg does not support by-ref target parameters. method='{methodId}', target='{targetType.FullName}.{targetMethod.Name}', index={modifyArgAttribute.ArgumentIndex}, parameterType='{expectedType.FullName}'.";
				return false;
			}

			if (handlerMethod.ReturnType != expectedType)
			{
				error =
					$"ModifyArg handler must return target parameter type. method='{methodId}', expectedReturn='{expectedType.FullName}', actualReturn='{handlerMethod.ReturnType.FullName}'.";
				return false;
			}

			if (handlerParameters.Length != 1 || handlerParameters[0].ParameterType != expectedType)
			{
				error =
					$"ModifyArg handler must have exactly one parameter matching target parameter type. method='{methodId}', expectedParameterType='{expectedType.FullName}', actualParameterCount={handlerParameters.Length}.";
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

			if (handlerParameters.Length != 1 || handlerParameters[0].ParameterType != handlerMethod.ReturnType)
			{
				error =
					$"ModifyConstant handler must have one parameter and return the same type. method='{methodId}', parameterCount={handlerParameters.Length}, returnType='{handlerMethod.ReturnType.FullName}'.";
				return false;
			}

			constantExpression = modifyConstantAttribute.ConstantExpression;
		}

		if (attribute is global::ReForge.RedirectAttribute redirectAttribute)
		{
			if (string.IsNullOrWhiteSpace(redirectAttribute.At))
			{
				error = $"Redirect requires non-empty At expression. method='{methodId}'.";
				return false;
			}

			if (!redirectAttribute.At.TrimStart().StartsWith("INVOKE:", StringComparison.OrdinalIgnoreCase))
			{
				error = $"Redirect At expression must start with 'INVOKE:'. method='{methodId}', at='{redirectAttribute.At}'.";
				return false;
			}
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

			ParameterInfo[] targetParameters = targetMethod.GetParameters();
			ParameterInfo[] overwriteHandlerParameters = handlerMethod.GetParameters();
			for (int i = 0; i < targetParameters.Length; i++)
			{
				if (overwriteHandlerParameters[i].ParameterType != targetParameters[i].ParameterType)
				{
					error =
						$"Overwrite parameter type mismatch. method='{methodId}', target='{targetMethod.DeclaringType?.FullName}.{targetMethod.Name}', index={i}, expected='{targetParameters[i].ParameterType.FullName}', actual='{overwriteHandlerParameters[i].ParameterType.FullName}'.";
					return false;
				}
			}
		}

		validated = new ValidatedMixinInjection(handlerMethod, targetMethod, attribute, argumentIndex, constantExpression);
		return true;
	}

	public bool TryValidateShadowField(
		Type mixinType,
		FieldInfo mixinField,
		global::ReForge.ShadowAttribute shadowAttribute,
		Type targetType,
		out ValidatedShadowField? validated,
		out string error)
	{
		ArgumentNullException.ThrowIfNull(mixinType);
		ArgumentNullException.ThrowIfNull(mixinField);
		ArgumentNullException.ThrowIfNull(shadowAttribute);
		ArgumentNullException.ThrowIfNull(targetType);

		validated = null;
		error = string.Empty;

		string mixinName = mixinType.FullName ?? mixinType.Name;
		string targetNameForLog = targetType.FullName ?? targetType.Name;
		string memberName = mixinField.Name;

		if (!mixinField.IsStatic)
		{
			error =
				$"Shadow field must be static. mixin='{mixinName}', targetType='{targetNameForLog}', member='{memberName}'.";
			return false;
		}

		if (mixinField.IsInitOnly)
		{
			error =
				$"Shadow field cannot be readonly because runtime binding requires assignment. mixin='{mixinName}', targetType='{targetNameForLog}', member='{memberName}'.";
			return false;
		}

		if (!mixinField.FieldType.IsAssignableFrom(typeof(FieldInfo)))
		{
			error =
				$"Shadow field type must be assignable from System.Reflection.FieldInfo. mixin='{mixinName}', targetType='{targetNameForLog}', member='{memberName}', fieldType='{mixinField.FieldType.FullName}'.";
			return false;
		}

		string targetFieldName = ResolveShadowTargetName(mixinField, shadowAttribute);
		if (string.IsNullOrWhiteSpace(targetFieldName))
		{
			error =
				$"Shadow target field name resolved to empty. mixin='{mixinName}', targetType='{targetNameForLog}', member='{memberName}'.";
			return false;
		}

		IReadOnlyList<string> aliases = NormalizeShadowAliases(targetFieldName, shadowAttribute.Aliases);
		validated = new ValidatedShadowField(mixinField, targetFieldName, aliases, shadowAttribute.Optional);
		return true;
	}

	private static bool TryResolveTargetMethod(
		Type targetType,
		global::ReForge.InjectionAttributeBase attribute,
		MethodInfo handlerMethod,
		out MethodInfo? targetMethod,
		out string error)
	{
		ArgumentNullException.ThrowIfNull(targetType);
		ArgumentNullException.ThrowIfNull(attribute);
		ArgumentNullException.ThrowIfNull(handlerMethod);

		targetMethod = null;
		error = string.Empty;
		string targetMethodName = attribute.TargetMethod;
		int ordinal = attribute.Ordinal;
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
			if (TryResolveBySignature(candidates, attribute, handlerMethod, out MethodInfo? resolvedBySignature) && resolvedBySignature != null)
			{
				targetMethod = resolvedBySignature;
				return true;
			}

			error =
				$"Target method is ambiguous; specify Ordinal. targetType='{targetType.FullName}', method='{targetMethodName}', candidateCount={candidates.Count}.";
			return false;
		}

		targetMethod = candidates[0];
		return true;
	}

	private static bool RequiresNonByRefHandlerParameters(InjectionKind kind)
	{
		return kind == InjectionKind.ModifyArg
			|| kind == InjectionKind.ModifyConstant
			|| kind == InjectionKind.Redirect;
	}

	private static bool TryResolveBySignature(
		IReadOnlyList<MethodInfo> candidates,
		global::ReForge.InjectionAttributeBase attribute,
		MethodInfo handlerMethod,
		out MethodInfo? resolved)
	{
		resolved = null;

		if (attribute is global::ReForge.OverwriteAttribute)
		{
			for (int i = 0; i < candidates.Count; i++)
			{
				MethodInfo candidate = candidates[i];
				if (candidate.ReturnType != handlerMethod.ReturnType)
				{
					continue;
				}

				ParameterInfo[] candidateParameters = candidate.GetParameters();
				ParameterInfo[] handlerParameters = handlerMethod.GetParameters();
				if (candidateParameters.Length != handlerParameters.Length)
				{
					continue;
				}

				bool allMatched = true;
				for (int j = 0; j < candidateParameters.Length; j++)
				{
					if (candidateParameters[j].ParameterType != handlerParameters[j].ParameterType)
					{
						allMatched = false;
						break;
					}
				}

				if (allMatched)
				{
					resolved = candidate;
					return true;
				}
			}

			return false;
		}

		if (attribute is global::ReForge.ModifyArgAttribute modifyArgAttribute)
		{
			for (int i = 0; i < candidates.Count; i++)
			{
				MethodInfo candidate = candidates[i];
				ParameterInfo[] candidateParameters = candidate.GetParameters();
				if (modifyArgAttribute.ArgumentIndex < 0 || modifyArgAttribute.ArgumentIndex >= candidateParameters.Length)
				{
					continue;
				}

				if (candidateParameters[modifyArgAttribute.ArgumentIndex].ParameterType != handlerMethod.ReturnType)
				{
					continue;
				}

				resolved = candidate;
				return true;
			}
		}

		return false;
	}

	private static string ResolveShadowTargetName(FieldInfo mixinField, global::ReForge.ShadowAttribute shadowAttribute)
	{
		if (!string.IsNullOrWhiteSpace(shadowAttribute.TargetName))
		{
			return shadowAttribute.TargetName.Trim();
		}

		string memberName = mixinField.Name;
		if (memberName.StartsWith("shadow$", StringComparison.Ordinal))
		{
			string fallback = memberName.Substring("shadow$".Length);
			if (!string.IsNullOrWhiteSpace(fallback))
			{
				return fallback.Trim();
			}
		}

		return memberName.Trim();
	}

	private static IReadOnlyList<string> NormalizeShadowAliases(string primaryName, IReadOnlyList<string> aliases)
	{
		if (aliases.Count == 0)
		{
			return Array.Empty<string>();
		}

		List<string> normalized = new(aliases.Count);
		HashSet<string> dedup = new(StringComparer.Ordinal);
		dedup.Add(primaryName);

		for (int i = 0; i < aliases.Count; i++)
		{
			string? alias = aliases[i];
			if (string.IsNullOrWhiteSpace(alias))
			{
				continue;
			}

			string trimmed = alias.Trim();
			if (!dedup.Add(trimmed))
			{
				continue;
			}

			normalized.Add(trimmed);
		}

		if (normalized.Count == 0)
		{
			return Array.Empty<string>();
		}

		return normalized;
	}
}
