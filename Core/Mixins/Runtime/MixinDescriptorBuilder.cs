#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;

namespace ReForgeFramework.Mixins.Runtime;

public enum MixinScanDiagnosticSeverity
{
	Info = 0,
	Warning = 1,
	Error = 2,
}

public sealed record MixinScanDiagnostic(
	MixinScanDiagnosticSeverity Severity,
	string Message,
	Type? MixinType,
	MethodInfo? HandlerMethod,
	global::ReForge.InjectionAttributeBase? Attribute
);

public sealed record InjectionDescriptor(
	string DescriptorKey,
	InjectionKind Kind,
	MethodInfo HandlerMethod,
	MethodInfo TargetMethod,
	string TargetMethodName,
	string At,
	int Ordinal,
	int Priority,
	bool Optional,
	int? ArgumentIndex,
	string ConstantExpression
);

internal sealed record ValidatedShadowField(
	FieldInfo MixinField,
	string TargetName,
	IReadOnlyList<string> Aliases,
	bool Optional
);

public sealed record ShadowFieldDescriptor(
	string DescriptorKey,
	FieldInfo MixinField,
	string TargetName,
	IReadOnlyList<string> Aliases,
	bool Optional
);

public sealed record MixinDescriptor(
	string DescriptorKey,
	string MixinId,
	Type MixinType,
	Type TargetType,
	int Priority,
	bool StrictMode,
	IReadOnlyList<InjectionDescriptor> Injections
)
{
	/// <summary>
	/// 字段级映射描述，默认空集合以保持旧调用方兼容。
	/// </summary>
	public IReadOnlyList<ShadowFieldDescriptor> ShadowFields { get; init; } = Array.Empty<ShadowFieldDescriptor>();
}

public sealed class MixinScanResult
{
	public MixinScanResult(
		string assemblyName,
		bool fromCache,
		IReadOnlyList<MixinDescriptor> descriptors,
		IReadOnlyList<MixinScanDiagnostic> diagnostics)
	{
		ArgumentNullException.ThrowIfNull(assemblyName);
		ArgumentNullException.ThrowIfNull(descriptors);
		ArgumentNullException.ThrowIfNull(diagnostics);

		AssemblyName = assemblyName;
		FromCache = fromCache;
		Descriptors = descriptors;
		Diagnostics = diagnostics;
	}

	public string AssemblyName { get; }

	public bool FromCache { get; }

	public IReadOnlyList<MixinDescriptor> Descriptors { get; }

	public IReadOnlyList<MixinScanDiagnostic> Diagnostics { get; }

	public int ErrorCount
	{
		get
		{
			int count = 0;
			for (int i = 0; i < Diagnostics.Count; i++)
			{
				if (Diagnostics[i].Severity == MixinScanDiagnosticSeverity.Error)
				{
					count++;
				}
			}

			return count;
		}
	}
}

internal sealed class MixinDescriptorBuilder
{
	public MixinDescriptor Build(
		Type mixinType,
		global::ReForge.MixinAttribute mixinAttribute,
		IReadOnlyList<ValidatedMixinInjection> validatedInjections,
		IReadOnlyList<ValidatedShadowField>? validatedShadowFields = null)
	{
		ArgumentNullException.ThrowIfNull(mixinType);
		ArgumentNullException.ThrowIfNull(mixinAttribute);
		ArgumentNullException.ThrowIfNull(validatedInjections);

		string mixinId = string.IsNullOrWhiteSpace(mixinAttribute.Id)
			? (mixinType.FullName ?? mixinType.Name)
			: mixinAttribute.Id.Trim();
		string descriptorKey = $"{mixinId}:{mixinAttribute.TargetType.AssemblyQualifiedName}";

		List<InjectionDescriptor> injections = new(validatedInjections.Count);
		for (int i = 0; i < validatedInjections.Count; i++)
		{
			injections.Add(BuildInjection(mixinType, mixinAttribute.TargetType, validatedInjections[i]));
		}

		List<ShadowFieldDescriptor> shadowFields = new();
		if (validatedShadowFields != null)
		{
			for (int i = 0; i < validatedShadowFields.Count; i++)
			{
				shadowFields.Add(BuildShadowField(mixinType, mixinAttribute.TargetType, validatedShadowFields[i]));
			}
		}

		return new MixinDescriptor(
			descriptorKey,
			mixinId,
			mixinType,
			mixinAttribute.TargetType,
			mixinAttribute.Priority,
			mixinAttribute.StrictMode,
			new ReadOnlyCollection<InjectionDescriptor>(injections)
		)
		{
			ShadowFields = shadowFields.Count == 0
				? Array.Empty<ShadowFieldDescriptor>()
				: new ReadOnlyCollection<ShadowFieldDescriptor>(shadowFields),
		};
	}

	private static InjectionDescriptor BuildInjection(
		Type mixinType,
		Type targetType,
		ValidatedMixinInjection validated)
	{
		global::ReForge.InjectionAttributeBase attribute = validated.Attribute;
		string descriptorKey = string.Concat(
			mixinType.AssemblyQualifiedName,
			":",
			targetType.AssemblyQualifiedName,
			":",
			validated.HandlerMethod.MetadataToken,
			":",
			validated.TargetMethod.MetadataToken,
			":",
			attribute.Kind,
			":",
			attribute.TargetMethod,
			":",
			attribute.Ordinal
		);

		return new InjectionDescriptor(
			descriptorKey,
			attribute.Kind,
			validated.HandlerMethod,
			validated.TargetMethod,
			attribute.TargetMethod,
			attribute.At,
			attribute.Ordinal,
			attribute.Priority,
			attribute.Optional,
			validated.ArgumentIndex,
			validated.ConstantExpression
		);
	}

	private static ShadowFieldDescriptor BuildShadowField(
		Type mixinType,
		Type targetType,
		ValidatedShadowField validated)
	{
		ArgumentNullException.ThrowIfNull(mixinType);
		ArgumentNullException.ThrowIfNull(targetType);
		ArgumentNullException.ThrowIfNull(validated);

		string targetName = string.IsNullOrWhiteSpace(validated.TargetName)
			? string.Empty
			: validated.TargetName.Trim();
		IReadOnlyList<string> aliases = NormalizeAliases(validated.Aliases);

		string descriptorKey = string.Concat(
			mixinType.AssemblyQualifiedName,
			":",
			targetType.AssemblyQualifiedName,
			":",
			validated.MixinField.MetadataToken,
			":",
			targetName,
			":",
			BuildAliasKey(aliases),
			":",
			validated.Optional ? "1" : "0"
		);

		return new ShadowFieldDescriptor(
			descriptorKey,
			validated.MixinField,
			targetName,
			aliases,
			validated.Optional
		);
	}

	private static IReadOnlyList<string> NormalizeAliases(IReadOnlyList<string> aliases)
	{
		if (aliases.Count == 0)
		{
			return Array.Empty<string>();
		}

		List<string> normalized = new(aliases.Count);
		for (int i = 0; i < aliases.Count; i++)
		{
			string? alias = aliases[i];
			if (string.IsNullOrWhiteSpace(alias))
			{
				continue;
			}

			normalized.Add(alias.Trim());
		}

		if (normalized.Count == 0)
		{
			return Array.Empty<string>();
		}

		return new ReadOnlyCollection<string>(normalized);
	}

	private static string BuildAliasKey(IReadOnlyList<string> aliases)
	{
		if (aliases.Count == 0)
		{
			return string.Empty;
		}

		return string.Join("|", aliases);
	}
}
