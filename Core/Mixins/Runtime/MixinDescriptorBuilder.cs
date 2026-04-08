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

public sealed record MixinDescriptor(
	string DescriptorKey,
	string MixinId,
	Type MixinType,
	Type TargetType,
	int Priority,
	bool StrictMode,
	IReadOnlyList<InjectionDescriptor> Injections
);

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
		IReadOnlyList<ValidatedMixinInjection> validatedInjections)
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

		return new MixinDescriptor(
			descriptorKey,
			mixinId,
			mixinType,
			mixinAttribute.TargetType,
			mixinAttribute.Priority,
			mixinAttribute.StrictMode,
			new ReadOnlyCollection<InjectionDescriptor>(injections)
		);
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
}
