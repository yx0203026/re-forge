#nullable enable

using System;
using System.Collections.Concurrent;
using System.Reflection;

namespace ReForgeFramework.Mixins.Runtime.Reflection;

internal sealed class ReflectionAccessor : IReflectionAccessor
{
	private delegate object? MethodInvoker(object? instance, object?[]? args);

	private readonly ReflectionAccessCache _cache;
	private readonly ConcurrentDictionary<MethodInfo, MethodInvoker> _methodInvokers = new();

	public ReflectionAccessor(ReflectionAccessCache? cache = null)
	{
		_cache = cache ?? new ReflectionAccessCache();
	}

	public bool TryResolveMethod(
		in ReflectionMemberKey key,
		in ReflectionAccessContext context,
		out MethodInfo? method,
		out ReflectionAccessError? error)
	{
		return _cache.TryResolveMethod(key, context, out method, out error);
	}

	public bool TryResolveField(
		in ReflectionMemberKey key,
		in ReflectionAccessContext context,
		out FieldInfo? field,
		out ReflectionAccessError? error)
	{
		return _cache.TryResolveField(key, context, out field, out error);
	}

	public bool TryResolveProperty(
		in ReflectionMemberKey key,
		in ReflectionAccessContext context,
		out PropertyInfo? property,
		out ReflectionAccessError? error)
	{
		return _cache.TryResolveProperty(key, context, out property, out error);
	}

	public bool TryInvoke(
		MethodInfo method,
		object? instance,
		object?[]? args,
		in ReflectionAccessContext context,
		out object? returnValue,
		out ReflectionAccessError? error)
	{
		ArgumentNullException.ThrowIfNull(method);

		try
		{
			MethodInvoker invoker = _methodInvokers.GetOrAdd(method, static target =>
				(instanceArg, argsArg) => target.Invoke(instanceArg, argsArg));

			returnValue = invoker(instance, args);
			error = null;
			return true;
		}
		catch (TargetInvocationException tie) when (tie.InnerException != null)
		{
			returnValue = null;
			error = BuildInvocationError(method, context, tie.InnerException);
			return false;
		}
		catch (Exception ex)
		{
			returnValue = null;
			error = BuildInvocationError(method, context, ex);
			return false;
		}
	}

	public bool TryGetFieldValue(
		FieldInfo field,
		object? instance,
		in ReflectionAccessContext context,
		out object? value,
		out ReflectionAccessError? error)
	{
		ArgumentNullException.ThrowIfNull(field);

		try
		{
			if (!field.IsStatic && instance == null)
			{
				value = null;
				error = BuildAccessError(
					field.DeclaringType,
					field.Name,
					ReflectionMemberKind.Field,
					ReflectionErrorCode.InvalidRequest,
					"Instance field access requires a non-null instance.",
					context);
				return false;
			}

			value = field.GetValue(instance);
			error = null;
			return true;
		}
		catch (Exception ex)
		{
			value = null;
			error = BuildAccessError(
				field.DeclaringType,
				field.Name,
				ReflectionMemberKind.Field,
				ReflectionErrorCode.InvocationFailed,
				"Field getter failed.",
				context,
				ex);
			return false;
		}
	}

	public bool TrySetFieldValue(
		FieldInfo field,
		object? instance,
		object? value,
		in ReflectionAccessContext context,
		out ReflectionAccessError? error)
	{
		ArgumentNullException.ThrowIfNull(field);

		if (field.IsInitOnly || field.IsLiteral)
		{
			error = BuildAccessError(
				field.DeclaringType,
				field.Name,
				ReflectionMemberKind.Field,
				ReflectionErrorCode.UnsupportedOperation,
				"Field is readonly/const and cannot be set.",
				context);
			return false;
		}

		if (!field.IsStatic && instance == null)
		{
			error = BuildAccessError(
				field.DeclaringType,
				field.Name,
				ReflectionMemberKind.Field,
				ReflectionErrorCode.InvalidRequest,
				"Instance field set requires a non-null instance.",
				context);
			return false;
		}

		try
		{
			field.SetValue(instance, value);
			error = null;
			return true;
		}
		catch (Exception ex)
		{
			error = BuildAccessError(
				field.DeclaringType,
				field.Name,
				ReflectionMemberKind.Field,
				ReflectionErrorCode.InvocationFailed,
				"Field setter failed.",
				context,
				ex);
			return false;
		}
	}

	public bool TryGetPropertyValue(
		PropertyInfo property,
		object? instance,
		in ReflectionAccessContext context,
		out object? value,
		out ReflectionAccessError? error)
	{
		ArgumentNullException.ThrowIfNull(property);

		if (!property.CanRead)
		{
			value = null;
			error = BuildAccessError(
				property.DeclaringType,
				property.Name,
				ReflectionMemberKind.Property,
				ReflectionErrorCode.UnsupportedOperation,
				"Property does not expose a getter.",
				context);
			return false;
		}

		MethodInfo? getter = property.GetGetMethod(true);
		if (getter == null)
		{
			value = null;
			error = BuildAccessError(
				property.DeclaringType,
				property.Name,
				ReflectionMemberKind.Property,
				ReflectionErrorCode.AccessDenied,
				"Property getter method is unavailable.",
				context);
			return false;
		}

		if (!getter.IsStatic && instance == null)
		{
			value = null;
			error = BuildAccessError(
				property.DeclaringType,
				property.Name,
				ReflectionMemberKind.Property,
				ReflectionErrorCode.InvalidRequest,
				"Instance property get requires a non-null instance.",
				context);
			return false;
		}

		try
		{
			value = property.GetValue(instance, null);
			error = null;
			return true;
		}
		catch (Exception ex)
		{
			value = null;
			error = BuildAccessError(
				property.DeclaringType,
				property.Name,
				ReflectionMemberKind.Property,
				ReflectionErrorCode.InvocationFailed,
				"Property getter failed.",
				context,
				ex);
			return false;
		}
	}

	public bool TrySetPropertyValue(
		PropertyInfo property,
		object? instance,
		object? value,
		in ReflectionAccessContext context,
		out ReflectionAccessError? error)
	{
		ArgumentNullException.ThrowIfNull(property);

		if (!property.CanWrite)
		{
			error = BuildAccessError(
				property.DeclaringType,
				property.Name,
				ReflectionMemberKind.Property,
				ReflectionErrorCode.UnsupportedOperation,
				"Property does not expose a setter.",
				context);
			return false;
		}

		MethodInfo? setter = property.GetSetMethod(true);
		if (setter == null)
		{
			error = BuildAccessError(
				property.DeclaringType,
				property.Name,
				ReflectionMemberKind.Property,
				ReflectionErrorCode.AccessDenied,
				"Property setter method is unavailable.",
				context);
			return false;
		}

		if (!setter.IsStatic && instance == null)
		{
			error = BuildAccessError(
				property.DeclaringType,
				property.Name,
				ReflectionMemberKind.Property,
				ReflectionErrorCode.InvalidRequest,
				"Instance property set requires a non-null instance.",
				context);
			return false;
		}

		try
		{
			property.SetValue(instance, value, null);
			error = null;
			return true;
		}
		catch (Exception ex)
		{
			error = BuildAccessError(
				property.DeclaringType,
				property.Name,
				ReflectionMemberKind.Property,
				ReflectionErrorCode.InvocationFailed,
				"Property setter failed.",
				context,
				ex);
			return false;
		}
	}

	private static ReflectionAccessError BuildInvocationError(MethodInfo method, in ReflectionAccessContext context, Exception exception)
	{
		return new ReflectionAccessError(
			ReflectionErrorCode.InvocationFailed,
			$"Method invocation failed: {exception.Message}",
			method.DeclaringType,
			method.Name,
			ReflectionMemberKind.Method,
			ReflectionSignatureBuilder.BuildMethodSignature(method),
			context,
			exception);
	}

	private static ReflectionAccessError BuildAccessError(
		Type? declaringType,
		string memberName,
		ReflectionMemberKind memberKind,
		ReflectionErrorCode code,
		string message,
		in ReflectionAccessContext context,
		Exception? exception = null)
	{
		return new ReflectionAccessError(
			code,
			message,
			declaringType,
			memberName,
			memberKind,
			string.Empty,
			context,
			exception);
	}
}
