#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using Godot;
using ReForgeFramework.Mixins.Runtime.Reflection;

public static partial class ReForge
{
	public static partial class Mixins
	{
		public enum ReflectionApiMemberKind
		{
			Method = 0,
			Field = 1,
			Property = 2,
		}

		public readonly record struct ReflectionApiMemberSpec(
			Type DeclaringType,
			string MemberName,
			ReflectionApiMemberKind MemberKind,
			string SignatureKey = "",
			int Ordinal = -1,
			bool Required = true
		);

		public readonly record struct ReflectionApiContext(
			string Owner,
			string Operation,
			string DescriptorKey = "",
			string Notes = ""
		);

		public readonly record struct ReflectionApiError(
			string Code,
			string Message,
			string? TargetType,
			string MemberName,
			ReflectionApiMemberKind MemberKind,
			string SignatureSummary,
			string Context,
			string? Exception
		);

		public static class ReflectionApi
		{
			private static readonly ConcurrentDictionary<string, Type?> ReForgeTypeCache = new(StringComparer.Ordinal);

			public static bool TryResolveReForgeType(string fullName, out Type? type)
			{
				type = null;
				if (string.IsNullOrWhiteSpace(fullName))
				{
					return false;
				}

				type = ReForgeTypeCache.GetOrAdd(fullName, static key =>
				{
					Type? resolved = Type.GetType($"{key}, ReForge", throwOnError: false, ignoreCase: false);
					if (resolved != null)
					{
						return resolved;
					}

					Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
					for (int i = 0; i < assemblies.Length; i++)
					{
						Assembly assembly = assemblies[i];
						if (!string.Equals(assembly.GetName().Name, "ReForge", StringComparison.OrdinalIgnoreCase))
						{
							continue;
						}

						return assembly.GetType(key, throwOnError: false, ignoreCase: false);
					}

					return null;
				});

				return type != null;
			}

			public static void RegisterWarmupPlan(string planId, string owner, IReadOnlyList<ReflectionApiMemberSpec> members)
			{
				ArgumentNullException.ThrowIfNull(planId);
				ArgumentNullException.ThrowIfNull(owner);
				ArgumentNullException.ThrowIfNull(members);

				List<ReflectionMemberKey> required = new();
				List<ReflectionMemberKey> optional = new();

				for (int i = 0; i < members.Count; i++)
				{
					ReflectionApiMemberSpec spec = members[i];
					ReflectionMemberKey key = ToMemberKey(spec);
					if (spec.Required)
					{
						required.Add(key);
					}
					else
					{
						optional.Add(key);
					}
				}

				LifecycleManager.RegisterExternalReflectionWarmupPlan(
					planId,
					owner,
					new ReadOnlyCollection<ReflectionMemberKey>(required),
					new ReadOnlyCollection<ReflectionMemberKey>(optional)
				);
			}

			public static bool TryResolveMethod(
				in ReflectionApiMemberSpec spec,
				in ReflectionApiContext context,
				out MethodInfo? method,
				out ReflectionApiError? error)
			{
				ReflectionMemberKey key = ToMemberKey(spec);
				ReflectionAccessContext accessContext = ToAccessContext(context);
				if (LifecycleManager.TryResolveMethod(key, accessContext, out method, out ReflectionAccessError? accessError))
				{
					error = null;
					return true;
				}

				error = accessError == null ? null : ToPublicError(accessError.Value);
				return false;
			}

			public static bool TryResolveField(
				in ReflectionApiMemberSpec spec,
				in ReflectionApiContext context,
				out FieldInfo? field,
				out ReflectionApiError? error)
			{
				ReflectionMemberKey key = ToMemberKey(spec);
				ReflectionAccessContext accessContext = ToAccessContext(context);
				if (LifecycleManager.TryResolveField(key, accessContext, out field, out ReflectionAccessError? accessError))
				{
					error = null;
					return true;
				}

				error = accessError == null ? null : ToPublicError(accessError.Value);
				return false;
			}

			public static bool TryResolveProperty(
				in ReflectionApiMemberSpec spec,
				in ReflectionApiContext context,
				out PropertyInfo? property,
				out ReflectionApiError? error)
			{
				ReflectionMemberKey key = ToMemberKey(spec);
				ReflectionAccessContext accessContext = ToAccessContext(context);
				if (LifecycleManager.TryResolveProperty(key, accessContext, out property, out ReflectionAccessError? accessError))
				{
					error = null;
					return true;
				}

				error = accessError == null ? null : ToPublicError(accessError.Value);
				return false;
			}

			public static bool TryInvoke(
				MethodInfo method,
				object? instance,
				object?[]? args,
				in ReflectionApiContext context,
				out object? returnValue,
				out ReflectionApiError? error)
			{
				ReflectionAccessContext accessContext = ToAccessContext(context);
				if (LifecycleManager.TryInvoke(method, instance, args, accessContext, out returnValue, out ReflectionAccessError? accessError))
				{
					error = null;
					return true;
				}

				error = accessError == null ? null : ToPublicError(accessError.Value);
				return false;
			}

			public static bool TrySetFieldValue(
				FieldInfo field,
				object? instance,
				object? value,
				in ReflectionApiContext context,
				out ReflectionApiError? error)
			{
				ReflectionAccessContext accessContext = ToAccessContext(context);
				if (LifecycleManager.TrySetFieldValue(field, instance, value, accessContext, out ReflectionAccessError? accessError))
				{
					error = null;
					return true;
				}

				error = accessError == null ? null : ToPublicError(accessError.Value);
				return false;
			}

			private static ReflectionMemberKey ToMemberKey(in ReflectionApiMemberSpec spec)
			{
				return new ReflectionMemberKey(
					spec.DeclaringType,
					spec.MemberName,
					spec.MemberKind switch
					{
						ReflectionApiMemberKind.Method => ReflectionMemberKind.Method,
						ReflectionApiMemberKind.Field => ReflectionMemberKind.Field,
						ReflectionApiMemberKind.Property => ReflectionMemberKind.Property,
						_ => ReflectionMemberKind.Method,
					},
					spec.SignatureKey,
					spec.Ordinal
				);
			}

			private static ReflectionAccessContext ToAccessContext(in ReflectionApiContext context)
			{
				return new ReflectionAccessContext(
					context.Owner,
					context.Operation,
					context.DescriptorKey,
					context.Notes
				);
			}

			private static ReflectionApiError ToPublicError(in ReflectionAccessError error)
			{
				return new ReflectionApiError(
					error.ErrorCode.ToString(),
					error.Message,
					error.TargetType?.FullName,
					error.MemberName,
					error.MemberKind switch
					{
						ReflectionMemberKind.Method => ReflectionApiMemberKind.Method,
						ReflectionMemberKind.Field => ReflectionApiMemberKind.Field,
						ReflectionMemberKind.Property => ReflectionApiMemberKind.Property,
						_ => ReflectionApiMemberKind.Method,
					},
					error.SignatureSummary,
					error.Context.ToString(),
					error.Exception?.Message
				);
			}
		}
	}
}
