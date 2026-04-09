#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;

namespace ReForgeFramework.Mixins.Runtime;

/// <summary>
/// Mixin 扫描诊断信息的严重级别。
/// </summary>
public enum MixinScanDiagnosticSeverity
{
	/// <summary>
	/// Info：信息级别，不影响 Mixin 安装。
	/// </summary>
	Info = 0,

	/// <summary>
	/// Warning：警告级别，可能预示问题但通常不会中止安装。
	/// </summary>
	Warning = 1,

	/// <summary>
	/// Error：错误级别，在严格模式下可能中止安装。
	/// </summary>
	Error = 2,
}

/// <summary>
/// Mixin 扫描过程中产生的单一诊断信息记录。
/// </summary>
public sealed record MixinScanDiagnostic(
	/// <summary>诊断信息的严重级别。</summary>
	MixinScanDiagnosticSeverity Severity,

	/// <summary>诊断说明文本。</summary>
	string Message,

	/// <summary>相关的 Mixin 类型（若有）。</summary>
	Type? MixinType,

	/// <summary>相关的处理方法（若有）。</summary>
	MethodInfo? HandlerMethod,

	/// <summary>相关的注入特性（若有）。</summary>
	global::ReForge.InjectionAttributeBase? Attribute
);

/// <summary>
/// 单次注入的完整描述符，包含源与目标方法的所有元数据。
/// </summary>
public sealed record InjectionDescriptor(
	/// <summary>描述符的唯一键值，用于冲突检测。</summary>
	string DescriptorKey,

	/// <summary>注入类型（Prefix、Postfix、Finalizer 等）。</summary>
	InjectionKind Kind,

	/// <summary>实现注入逻辑的处理方法。</summary>
	MethodInfo HandlerMethod,

	/// <summary>目标类型中被注入的方法。</summary>
	MethodInfo TargetMethod,

	/// <summary>目标方法名称。</summary>
	string TargetMethodName,

	/// <summary>注入锚点描述（如 IL 标记）。</summary>
	string At,

	/// <summary>当同名方法有多个重载时，指定目标序号（-1 表示未指定）。</summary>
	int Ordinal,

	/// <summary>注入优先级（数值越大执行越晚）。</summary>
	int Priority,

	/// <summary>是否允许未找到目标时跳过此注入。</summary>
	bool Optional,

	/// <summary>修改参数时的参数索引（ModifyArg 使用）。</summary>
	int? ArgumentIndex,

	/// <summary>修改常量时的常量表达式（ModifyConstant 使用）。</summary>
	string ConstantExpression
);

/// <summary>
/// 验证通过的 Shadow 字段映射信息。
/// </summary>
internal sealed record ValidatedShadowField(
	/// <summary>Mixin 类中的静态字段。</summary>
	FieldInfo MixinField,

	/// <summary>目标类型中的字段主名称。</summary>
	string TargetName,

	/// <summary>目标字段的备用名称列表。</summary>
	IReadOnlyList<string> Aliases,

	/// <summary>字段缺失时是否允许跳过。</summary>
	bool Optional
);

/// <summary>
/// Shadow 字段映射的完整描述符。
/// </summary>
public sealed record ShadowFieldDescriptor(
	/// <summary>描述符的唯一键值。</summary>
	string DescriptorKey,

	/// <summary>Mixin 类中的字段。</summary>
	FieldInfo MixinField,

	/// <summary>目标类型中的字段主名称。</summary>
	string TargetName,

	/// <summary>目标字段的备用名称列表。</summary>
	IReadOnlyList<string> Aliases,

	/// <summary>字段缺失时是否允许跳过。</summary>
	bool Optional
);

/// <summary>
/// 单个 Mixin 的完整描述符，包含其所有注入与 Shadow 字段映射的元数据。
/// </summary>
public sealed record MixinDescriptor(
	/// <summary>描述符的唯一键值。</summary>
	string DescriptorKey,

	/// <summary>Mixin 的唯一标识符。</summary>
	string MixinId,

	/// <summary>Mixin 实现类型。</summary>
	Type MixinType,

	/// <summary>此 Mixin 的目标类型。</summary>
	Type TargetType,

	/// <summary>注入优先级（数值越大执行越晚）。</summary>
	int Priority,

	/// <summary>是否启用严格模式。</summary>
	bool StrictMode,

	/// <summary>包含的所有注入描述符集合。</summary>
	IReadOnlyList<InjectionDescriptor> Injections
)
{
	/// <summary>
	/// 字段级 Shadow 映射描述集合，默认为空以保持向后兼容。
	/// </summary>
	public IReadOnlyList<ShadowFieldDescriptor> ShadowFields { get; init; } = Array.Empty<ShadowFieldDescriptor>();
}

/// <summary>
/// 单次程序集扫描的完整结果。包含所有发现的 Mixin 描述符与诊断信息。
/// </summary>
public sealed class MixinScanResult
{
	/// <summary>
	/// 初始化 <see cref="MixinScanResult"/> 的新实例。
	/// </summary>
	/// <param name="assemblyName">被扫描的程序集名称。</param>
	/// <param name="fromCache">此结果是否来自缓存而非新的扫描。</param>
	/// <param name="descriptors">发现的 Mixin 描述符集合。</param>
	/// <param name="diagnostics">扫描过程中产生的诊断信息。</param>
	/// <exception cref="ArgumentNullException">当任何参数为 null 时。</exception>
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

	/// <summary>
	/// 获取被扫描的程序集名称。
	/// </summary>
	public string AssemblyName { get; }

	/// <summary>
	/// 获取此结果是否来自缓存。
	/// </summary>
	public bool FromCache { get; }

	/// <summary>
	/// 获取发现的 Mixin 描述符集合（按定义顺序）。
	/// </summary>
	public IReadOnlyList<MixinDescriptor> Descriptors { get; }

	/// <summary>
	/// 获取扫描过程中产生的诊断信息集合。
	/// </summary>
	public IReadOnlyList<MixinScanDiagnostic> Diagnostics { get; }

	/// <summary>
	/// 获取诊断信息中的"错误级别"信息数量。
	/// </summary>
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

/// <summary>
/// Mixin 描述符的构建器。负责从验证通过的数据构建最终的描述符对象。
/// </summary>
internal sealed class MixinDescriptorBuilder
{
	/// <summary>
	/// 构建单个 Mixin 的完整描述符。
	/// </summary>
	/// <param name="mixinType">Mixin 实现类型。</param>
	/// <param name="mixinAttribute">应用到 Mixin 的特性。</param>
	/// <param name="validatedInjections">已验证通过的注入集合。</param>
	/// <param name="validatedShadowFields">已验证通过的 Shadow 字段集合（可选）。</param>
	/// <returns>构建完成的 <see cref="MixinDescriptor"/> 对象。</returns>
	/// <exception cref="ArgumentNullException">当任何必要参数为 null 时。</exception>
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
