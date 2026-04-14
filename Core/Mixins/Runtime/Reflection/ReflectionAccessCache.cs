#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;

namespace ReForgeFramework.Mixins.Runtime.Reflection;

internal sealed class ReflectionAccessCache
{
	private const BindingFlags MemberFlags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

	private readonly ConcurrentDictionary<Type, ReflectionTypeIndex> _typeIndexByType = new();
	private readonly ConcurrentDictionary<ReflectionMemberKey, ReflectionMemberCacheEntry> _memberCache = new();
	private readonly ReflectionDiagnostics? _diagnostics;
	private readonly IReflectionAccessGate? _accessGate;

	public ReflectionAccessCache(ReflectionDiagnostics? diagnostics = null, IReflectionAccessGate? accessGate = null)
	{
		_diagnostics = diagnostics;
		_accessGate = accessGate;
	}

	public bool TryResolveMethod(
		in ReflectionMemberKey key,
		in ReflectionAccessContext context,
		out MethodInfo? method,
		out ReflectionAccessError? error)
	{
		if (!TryResolveMember(key, ReflectionMemberKind.Method, context, out ReflectionAccessResult<MethodInfo> result))
		{
			method = null;
			error = result.Error;
			return false;
		}

		method = result.Value;
		error = null;
		return true;
	}

	public bool TryResolveField(
		in ReflectionMemberKey key,
		in ReflectionAccessContext context,
		out FieldInfo? field,
		out ReflectionAccessError? error)
	{
		if (!TryResolveMember(key, ReflectionMemberKind.Field, context, out ReflectionAccessResult<FieldInfo> result))
		{
			field = null;
			error = result.Error;
			return false;
		}

		field = result.Value;
		error = null;
		return true;
	}

	public bool TryResolveProperty(
		in ReflectionMemberKey key,
		in ReflectionAccessContext context,
		out PropertyInfo? property,
		out ReflectionAccessError? error)
	{
		if (!TryResolveMember(key, ReflectionMemberKind.Property, context, out ReflectionAccessResult<PropertyInfo> result))
		{
			property = null;
			error = result.Error;
			return false;
		}

		property = result.Value;
		error = null;
		return true;
	}

	private bool TryResolveMember<TMember>(
		in ReflectionMemberKey key,
		ReflectionMemberKind expectedKind,
		in ReflectionAccessContext context,
		out ReflectionAccessResult<TMember> result)
		where TMember : MemberInfo
	{
		if (key.DeclaringType == null)
		{
			result = new ReflectionAccessResult<TMember>(BuildError(
				ReflectionErrorCode.InvalidRequest,
				"Declaring type is null.",
				key,
				context));
			return false;
		}

		if (key.MemberKind != expectedKind)
		{
			result = new ReflectionAccessResult<TMember>(BuildError(
				ReflectionErrorCode.InvalidRequest,
				$"Member kind mismatch. expected={expectedKind}, actual={key.MemberKind}.",
				key,
				context));
			return false;
		}

		if (string.IsNullOrWhiteSpace(key.MemberName))
		{
			result = new ReflectionAccessResult<TMember>(BuildError(
				ReflectionErrorCode.InvalidRequest,
				"Member name is empty.",
				key,
				context));
			return false;
		}

		if (_accessGate != null && !_accessGate.TryAuthorize(key, context, out ReflectionAccessError? gateError))
		{
			result = new ReflectionAccessResult<TMember>(gateError ?? BuildError(
				ReflectionErrorCode.AccessDenied,
				"Reflection access denied by runtime gate.",
				key,
				context));
			return false;
		}

		ReflectionMemberCacheEntry entry;
		if (_memberCache.TryGetValue(key, out ReflectionMemberCacheEntry existing))
		{
			entry = existing;
			if (entry.IsSuccess)
			{
				_diagnostics?.RecordCacheHit();
			}
		}
		else
		{
			entry = _memberCache.GetOrAdd(key, static (cacheKey, state) =>
				state.Resolve(cacheKey), this);
		}

		if (!entry.IsSuccess || entry.Member is not TMember typedMember)
		{
			result = new ReflectionAccessResult<TMember>(entry.Error ?? BuildError(
				ReflectionErrorCode.Unknown,
				"Member resolve failed with unknown reason.",
				key,
				context));
			return false;
		}

		result = new ReflectionAccessResult<TMember>(typedMember);
		return true;
	}

	private ReflectionMemberCacheEntry Resolve(in ReflectionMemberKey key)
	{
		ReflectionTypeIndex typeIndex = _typeIndexByType.GetOrAdd(key.DeclaringType, static type => ReflectionTypeIndex.Build(type));

		switch (key.MemberKind)
		{
			case ReflectionMemberKind.Method:
				return ResolveMethod(typeIndex, key);
			case ReflectionMemberKind.Field:
				return ResolveField(typeIndex, key);
			case ReflectionMemberKind.Property:
				return ResolveProperty(typeIndex, key);
			default:
				return ReflectionMemberCacheEntry.Fail(new ReflectionAccessError(
					ReflectionErrorCode.InvalidRequest,
					$"Unsupported member kind: {key.MemberKind}.",
					key.DeclaringType,
					key.MemberName,
					key.MemberKind,
					key.SignatureKey,
					new ReflectionAccessContext("ReflectionAccessCache", "Resolve")));
		}
	}

	private static ReflectionMemberCacheEntry ResolveMethod(in ReflectionTypeIndex typeIndex, in ReflectionMemberKey key)
	{
		if (!typeIndex.MethodsByName.TryGetValue(key.MemberName, out IReadOnlyList<MethodInfo>? methods) || methods.Count == 0)
		{
			return ReflectionMemberCacheEntry.Fail(CreateMemberNotFoundError(key));
		}

		List<MethodInfo> filtered = FilterMethods(methods, key.SignatureKey);
		if (filtered.Count == 0)
		{
			return ReflectionMemberCacheEntry.Fail(CreateSignatureMismatchError(key));
		}

		if (key.Ordinal >= 0)
		{
			if (key.Ordinal >= filtered.Count)
			{
				return ReflectionMemberCacheEntry.Fail(new ReflectionAccessError(
					ReflectionErrorCode.SignatureMismatch,
					$"Ordinal out of range. ordinal={key.Ordinal}, candidates={filtered.Count}.",
					key.DeclaringType,
					key.MemberName,
					key.MemberKind,
					key.SignatureKey,
					new ReflectionAccessContext("ReflectionAccessCache", "ResolveMethod")));
			}

			return ReflectionMemberCacheEntry.Success(filtered[key.Ordinal]);
		}

		if (filtered.Count > 1)
		{
			return ReflectionMemberCacheEntry.Fail(new ReflectionAccessError(
				ReflectionErrorCode.AmbiguousMember,
				$"Method is ambiguous. candidateCount={filtered.Count}. Specify signatureKey or ordinal.",
				key.DeclaringType,
				key.MemberName,
				key.MemberKind,
				key.SignatureKey,
				new ReflectionAccessContext("ReflectionAccessCache", "ResolveMethod")));
		}

		return ReflectionMemberCacheEntry.Success(filtered[0]);
	}

	private static ReflectionMemberCacheEntry ResolveField(in ReflectionTypeIndex typeIndex, in ReflectionMemberKey key)
	{
		if (!typeIndex.FieldsByName.TryGetValue(key.MemberName, out FieldInfo? field))
		{
			return ReflectionMemberCacheEntry.Fail(CreateMemberNotFoundError(key));
		}

		return ReflectionMemberCacheEntry.Success(field);
	}

	private static ReflectionMemberCacheEntry ResolveProperty(in ReflectionTypeIndex typeIndex, in ReflectionMemberKey key)
	{
		if (!typeIndex.PropertiesByName.TryGetValue(key.MemberName, out PropertyInfo? property))
		{
			return ReflectionMemberCacheEntry.Fail(CreateMemberNotFoundError(key));
		}

		return ReflectionMemberCacheEntry.Success(property);
	}

	private static List<MethodInfo> FilterMethods(IReadOnlyList<MethodInfo> methods, string signatureKey)
	{
		if (string.IsNullOrWhiteSpace(signatureKey))
		{
			return new List<MethodInfo>(methods);
		}

		List<MethodInfo> result = new();
		for (int i = 0; i < methods.Count; i++)
		{
			MethodInfo method = methods[i];
			if (string.Equals(ReflectionSignatureBuilder.BuildMethodSignature(method), signatureKey, StringComparison.Ordinal))
			{
				result.Add(method);
			}
		}

		return result;
	}

	private static ReflectionAccessError CreateMemberNotFoundError(in ReflectionMemberKey key)
	{
		return new ReflectionAccessError(
			ReflectionErrorCode.MemberNotFound,
			"Member not found.",
			key.DeclaringType,
			key.MemberName,
			key.MemberKind,
			key.SignatureKey,
			new ReflectionAccessContext("ReflectionAccessCache", "Resolve"));
	}

	private static ReflectionAccessError CreateSignatureMismatchError(in ReflectionMemberKey key)
	{
		return new ReflectionAccessError(
			ReflectionErrorCode.SignatureMismatch,
			"No member matched signatureKey.",
			key.DeclaringType,
			key.MemberName,
			key.MemberKind,
			key.SignatureKey,
			new ReflectionAccessContext("ReflectionAccessCache", "Resolve"));
	}

	private static ReflectionAccessError BuildError(
		ReflectionErrorCode code,
		string message,
		in ReflectionMemberKey key,
		in ReflectionAccessContext context,
		Exception? exception = null)
	{
		return new ReflectionAccessError(
			code,
			message,
			key.DeclaringType,
			key.MemberName,
			key.MemberKind,
			key.SignatureKey,
			context,
			exception);
	}

	private sealed class ReflectionTypeIndex
	{
		public ReflectionTypeIndex(
			Dictionary<string, IReadOnlyList<MethodInfo>> methodsByName,
			Dictionary<string, FieldInfo> fieldsByName,
			Dictionary<string, PropertyInfo> propertiesByName)
		{
			MethodsByName = methodsByName;
			FieldsByName = fieldsByName;
			PropertiesByName = propertiesByName;
		}

		public Dictionary<string, IReadOnlyList<MethodInfo>> MethodsByName { get; }
		public Dictionary<string, FieldInfo> FieldsByName { get; }
		public Dictionary<string, PropertyInfo> PropertiesByName { get; }

		public static ReflectionTypeIndex Build(Type type)
		{
			MethodInfo[] methods = type.GetMethods(MemberFlags);
			FieldInfo[] fields = type.GetFields(MemberFlags);
			PropertyInfo[] properties = type.GetProperties(MemberFlags);

			Dictionary<string, List<MethodInfo>> methodGroups = new(StringComparer.Ordinal);
			for (int i = 0; i < methods.Length; i++)
			{
				MethodInfo method = methods[i];
				if (!methodGroups.TryGetValue(method.Name, out List<MethodInfo>? bucket))
				{
					bucket = new List<MethodInfo>();
					methodGroups[method.Name] = bucket;
				}

				bucket.Add(method);
			}

			Dictionary<string, IReadOnlyList<MethodInfo>> methodsByName = new(StringComparer.Ordinal);
			foreach ((string name, List<MethodInfo> bucket) in methodGroups)
			{
				bucket.Sort(static (a, b) => a.MetadataToken.CompareTo(b.MetadataToken));
				methodsByName[name] = bucket;
			}

			Dictionary<string, FieldInfo> fieldsByName = new(StringComparer.Ordinal);
			for (int i = 0; i < fields.Length; i++)
			{
				FieldInfo field = fields[i];
				if (!fieldsByName.ContainsKey(field.Name))
				{
					fieldsByName[field.Name] = field;
				}
			}

			Dictionary<string, PropertyInfo> propertiesByName = new(StringComparer.Ordinal);
			for (int i = 0; i < properties.Length; i++)
			{
				PropertyInfo property = properties[i];
				if (!propertiesByName.ContainsKey(property.Name))
				{
					propertiesByName[property.Name] = property;
				}
			}

			return new ReflectionTypeIndex(methodsByName, fieldsByName, propertiesByName);
		}
	}

	private readonly struct ReflectionMemberCacheEntry
	{
		private ReflectionMemberCacheEntry(MemberInfo? member, ReflectionAccessError? error)
		{
			Member = member;
			Error = error;
		}

		public MemberInfo? Member { get; }
		public ReflectionAccessError? Error { get; }
		public bool IsSuccess => Member != null && Error == null;

		public static ReflectionMemberCacheEntry Success(MemberInfo member)
		{
			return new ReflectionMemberCacheEntry(member, null);
		}

		public static ReflectionMemberCacheEntry Fail(ReflectionAccessError error)
		{
			return new ReflectionMemberCacheEntry(null, error);
		}
	}
}

internal static class ReflectionSignatureBuilder
{
	public static string BuildMethodSignature(MethodInfo method)
	{
		ParameterInfo[] parameters = method.GetParameters();
		string[] parameterTypeNames = new string[parameters.Length];
		for (int i = 0; i < parameters.Length; i++)
		{
			parameterTypeNames[i] = parameters[i].ParameterType.FullName ?? parameters[i].ParameterType.Name;
		}

		string returnType = method.ReturnType.FullName ?? method.ReturnType.Name;
		return $"({string.Join(",", parameterTypeNames)})->{returnType}";
	}
}
