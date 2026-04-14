#nullable enable

using System;
using System.Reflection;

namespace ReForgeFramework.Mixins.Runtime.Reflection;

internal enum ReflectionMemberKind
{
	Method = 0,
	Field = 1,
	Property = 2,
}

internal enum ReflectionErrorCode
{
	Unknown = 0,
	InvalidRequest = 1,
	TypeNotFound = 2,
	MemberNotFound = 3,
	SignatureMismatch = 4,
	AmbiguousMember = 5,
	InvocationFailed = 6,
	AccessDenied = 7,
	UnsupportedOperation = 8,
}

internal readonly record struct ReflectionMemberKey(
	Type DeclaringType,
	string MemberName,
	ReflectionMemberKind MemberKind,
	string SignatureKey = "",
	int Ordinal = -1)
{
	public override string ToString()
	{
		return $"{DeclaringType.FullName}::{MemberName} [{MemberKind}] sig='{SignatureKey}' ord={Ordinal}";
	}
}

internal readonly record struct ReflectionAccessContext(
	string Owner,
	string Operation,
	string DescriptorKey = "",
	string Notes = "")
{
	public override string ToString()
	{
		return $"owner='{Owner}', operation='{Operation}', descriptorKey='{DescriptorKey}', notes='{Notes}'";
	}
}

internal readonly record struct ReflectionAccessError(
	ReflectionErrorCode ErrorCode,
	string Message,
	Type? TargetType,
	string MemberName,
	ReflectionMemberKind MemberKind,
	string SignatureSummary,
	ReflectionAccessContext Context,
	Exception? Exception = null)
{
	public override string ToString()
	{
		string typeName = TargetType?.FullName ?? "<null>";
		return $"[{ErrorCode}] {Message}; type='{typeName}', member='{MemberName}', kind={MemberKind}, signature='{SignatureSummary}', context=({Context})";
	}
}

internal readonly struct ReflectionAccessResult<T>
	where T : class
{
	public ReflectionAccessResult(T value)
	{
		Value = value;
		Error = null;
	}

	public ReflectionAccessResult(ReflectionAccessError error)
	{
		Value = null;
		Error = error;
	}

	public T? Value { get; }
	public ReflectionAccessError? Error { get; }
	public bool IsSuccess => Value != null;
}

internal interface IReflectionAccessor
{
	bool TryResolveMethod(
		in ReflectionMemberKey key,
		in ReflectionAccessContext context,
		out MethodInfo? method,
		out ReflectionAccessError? error);

	bool TryResolveField(
		in ReflectionMemberKey key,
		in ReflectionAccessContext context,
		out FieldInfo? field,
		out ReflectionAccessError? error);

	bool TryResolveProperty(
		in ReflectionMemberKey key,
		in ReflectionAccessContext context,
		out PropertyInfo? property,
		out ReflectionAccessError? error);

	bool TryInvoke(
		MethodInfo method,
		object? instance,
		object?[]? args,
		in ReflectionAccessContext context,
		out object? returnValue,
		out ReflectionAccessError? error);

	bool TryGetFieldValue(
		FieldInfo field,
		object? instance,
		in ReflectionAccessContext context,
		out object? value,
		out ReflectionAccessError? error);

	bool TrySetFieldValue(
		FieldInfo field,
		object? instance,
		object? value,
		in ReflectionAccessContext context,
		out ReflectionAccessError? error);

	bool TryGetPropertyValue(
		PropertyInfo property,
		object? instance,
		in ReflectionAccessContext context,
		out object? value,
		out ReflectionAccessError? error);

	bool TrySetPropertyValue(
		PropertyInfo property,
		object? instance,
		object? value,
		in ReflectionAccessContext context,
		out ReflectionAccessError? error);
}

internal interface IReflectionAccessGate
{
	bool TryAuthorize(
		in ReflectionMemberKey key,
		in ReflectionAccessContext context,
		out ReflectionAccessError? error);
}
