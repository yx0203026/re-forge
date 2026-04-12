#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;
using Godot;

namespace ReForgeFramework.Mixins.Runtime;

/// <summary>
/// 负责扫描程序集并提取 Mixin 定义的核心组件。
/// </summary>
/// <remarks>
/// <see cref="MixinScanner"/> 执行以下职责：
/// 1. 使用反射扫描程序集中所有被 <see cref="global::ReForge.MixinAttribute"/> 标记的类型
/// 2. 验证 Mixin 类型和其关联的注入方法
/// 3. 构建描述符结构用于后续的补丁绑定
/// 4. 维护缓存以提高重复扫描的性能
/// 5. 记录扫描过程中的诊断信息（警告与错误）
/// 
/// 扫描结果包含 Mixin 描述符与诊断信息，可用于诊断问题。
/// </remarks>
internal sealed class MixinScanner
{
	private readonly object _syncRoot = new();
	private readonly Dictionary<string, MixinScanResult> _scanCache = new(StringComparer.Ordinal);
	private readonly HashSet<string> _registeredInjectionKeys = new(StringComparer.Ordinal);
	private readonly HashSet<string> _registeredShadowKeys = new(StringComparer.Ordinal);
	private readonly Dictionary<string, HashSet<string>> _assemblyInjectionKeys = new(StringComparer.Ordinal);
	private readonly Dictionary<string, HashSet<string>> _assemblyShadowKeys = new(StringComparer.Ordinal);
	private readonly MixinValidation _validation = new();
	private readonly MixinDescriptorBuilder _descriptorBuilder = new();

	/// <summary>
	/// 扫描指定程序集并提取所有 Mixin 定义。
	/// </summary>
	/// <param name="assembly">待扫描的程序集，不可为 null。</param>
	/// <param name="forceRescan">是否忽略缓存并强制重新扫描。默认为 false。</param>
	/// <returns>包含 Mixin 描述符与诊断信息的 <see cref="MixinScanResult"/> 对象。</returns>
	/// <remarks>
	/// 首次扫描时会执行完整反射与验证；后续扫描（未强制）会返回缓存结果以提高性能。
	/// 诊断信息可用于识别定义不当的 Mixin 或潜在的问题。
	/// </remarks>
	/// <exception cref="ArgumentNullException">当 assembly 为 null 时。</exception>
	public MixinScanResult Scan(Assembly assembly, bool forceRescan = false)
	{
		ArgumentNullException.ThrowIfNull(assembly);

		string cacheKey = BuildAssemblyCacheKey(assembly);
		if (!forceRescan)
		{
			lock (_syncRoot)
			{
				if (_scanCache.TryGetValue(cacheKey, out MixinScanResult? cachedResult))
				{
					return new MixinScanResult(cachedResult.AssemblyName, fromCache: true, cachedResult.Descriptors, cachedResult.Diagnostics);
				}
			}
		}

		Type[] types = SafeGetTypes(assembly);
		List<MixinDescriptor> descriptors = new();
		List<MixinScanDiagnostic> diagnostics = new();
		ResetRegisteredKeysForAssembly(cacheKey);

		for (int i = 0; i < types.Length; i++)
		{
			Type? type = types[i];
			if (type == null)
			{
				continue;
			}

			ScanType(type, cacheKey, descriptors, diagnostics);
		}

		MixinScanResult result = new(
			assembly.GetName().Name ?? assembly.FullName ?? "UnknownAssembly",
			fromCache: false,
			new ReadOnlyCollection<MixinDescriptor>(descriptors),
			new ReadOnlyCollection<MixinScanDiagnostic>(diagnostics)
		);

		lock (_syncRoot)
		{
			_scanCache[cacheKey] = result;
		}

		return result;
	}

	/// <summary>
	/// 清除扫描缓存。
	/// </summary>
	/// <param name="assembly">
	/// 待清除的程序集。若为 null，将清除所有缓存。
	/// 若指定具体程序集，仅清除该程序集的缓存及其相关注册记录。
	/// </param>
	public void InvalidateCache(Assembly? assembly = null)
	{
		lock (_syncRoot)
		{
			if (assembly == null)
			{
				_scanCache.Clear();
				_registeredInjectionKeys.Clear();
				_registeredShadowKeys.Clear();
				_assemblyInjectionKeys.Clear();
				_assemblyShadowKeys.Clear();
				return;
			}

			string cacheKey = BuildAssemblyCacheKey(assembly);
			_scanCache.Remove(cacheKey);
			if (_assemblyInjectionKeys.Remove(cacheKey, out HashSet<string>? assemblyKeys) && assemblyKeys != null)
			{
				foreach (string key in assemblyKeys)
				{
					_registeredInjectionKeys.Remove(key);
				}
			}

			if (_assemblyShadowKeys.Remove(cacheKey, out HashSet<string>? assemblyShadowKeys) && assemblyShadowKeys != null)
			{
				foreach (string key in assemblyShadowKeys)
				{
					_registeredShadowKeys.Remove(key);
				}
			}
		}
	}

	private void ResetRegisteredKeysForAssembly(string cacheKey)
	{
		lock (_syncRoot)
		{
			if (_assemblyInjectionKeys.Remove(cacheKey, out HashSet<string>? existingKeys) && existingKeys != null)
			{
				foreach (string key in existingKeys)
				{
					_registeredInjectionKeys.Remove(key);
				}
			}

			if (_assemblyShadowKeys.Remove(cacheKey, out HashSet<string>? existingShadowKeys) && existingShadowKeys != null)
			{
				foreach (string key in existingShadowKeys)
				{
					_registeredShadowKeys.Remove(key);
				}
			}
		}
	}

	private void ScanType(
		Type mixinType,
		string assemblyCacheKey,
		List<MixinDescriptor> descriptors,
		List<MixinScanDiagnostic> diagnostics)
	{
		if (!Attribute.IsDefined(mixinType, typeof(global::ReForge.MixinAttribute), inherit: false))
		{
			return;
		}

		object[] mixinAttributes = mixinType.GetCustomAttributes(typeof(global::ReForge.MixinAttribute), inherit: false);
		if (mixinAttributes.Length == 0)
		{
			return;
		}

		for (int i = 0; i < mixinAttributes.Length; i++)
		{
			global::ReForge.MixinAttribute? mixinAttribute = mixinAttributes[i] as global::ReForge.MixinAttribute;
			if (mixinAttribute == null)
			{
				continue;
			}

			if (!_validation.TryValidateMixinType(mixinType, mixinAttribute, out string mixinError))
			{
				RecordError(diagnostics, mixinError, mixinType, handlerMethod: null, attribute: null);
				continue;
			}

			List<ValidatedMixinInjection> validatedInjections = ScanInjectionMethods(mixinType, mixinAttribute, assemblyCacheKey, diagnostics);
			List<ValidatedShadowField> validatedShadowFields = ScanShadowFields(mixinType, mixinAttribute, assemblyCacheKey, diagnostics);
			if (validatedInjections.Count == 0 && validatedShadowFields.Count == 0)
			{
				RecordWarning(
					diagnostics,
					$"Mixin has no valid injection methods or shadow fields. mixinType='{mixinType.FullName}', targetType='{mixinAttribute.TargetType.FullName}'.",
					mixinType,
					handlerMethod: null,
					attribute: null
				);
				continue;
			}

			MixinDescriptor descriptor = _descriptorBuilder.Build(mixinType, mixinAttribute, validatedInjections, validatedShadowFields);
			descriptors.Add(descriptor);
		}
	}

	private List<ValidatedMixinInjection> ScanInjectionMethods(
		Type mixinType,
		global::ReForge.MixinAttribute mixinAttribute,
		string assemblyCacheKey,
		List<MixinScanDiagnostic> diagnostics)
	{
		MethodInfo[] methods = mixinType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
		List<ValidatedMixinInjection> validatedInjections = new();
		HashSet<string> localDedupKeys = new(StringComparer.Ordinal);

		for (int i = 0; i < methods.Length; i++)
		{
			MethodInfo handlerMethod = methods[i];
			if (!Attribute.IsDefined(handlerMethod, typeof(global::ReForge.InjectionAttributeBase), inherit: false))
			{
				continue;
			}

			object[] attributes = handlerMethod.GetCustomAttributes(typeof(global::ReForge.InjectionAttributeBase), inherit: false);
			if (attributes.Length == 0)
			{
				continue;
			}

			for (int j = 0; j < attributes.Length; j++)
			{
				global::ReForge.InjectionAttributeBase? injectionAttribute = attributes[j] as global::ReForge.InjectionAttributeBase;
				if (injectionAttribute == null)
				{
					continue;
				}

				if (!_validation.TryValidateInjection(
						mixinType,
						handlerMethod,
						injectionAttribute,
						mixinAttribute.TargetType,
						out ValidatedMixinInjection? validated,
						out string error))
				{
					RecordError(diagnostics, error, mixinType, handlerMethod, injectionAttribute);
					continue;
				}

				if (validated == null)
				{
					RecordError(
						diagnostics,
						$"Validation returned null result. mixinType='{mixinType.FullName}', method='{handlerMethod.Name}'.",
						mixinType,
						handlerMethod,
						injectionAttribute
					);
					continue;
				}

				string dedupKey = BuildInjectionDedupKey(mixinType, mixinAttribute.TargetType, validated);
				if (!localDedupKeys.Add(dedupKey))
				{
					RecordWarning(
						diagnostics,
						$"Duplicate injection declaration ignored in mixin scope. key='{dedupKey}'.",
						mixinType,
						handlerMethod,
						injectionAttribute
					);
					continue;
				}

				lock (_syncRoot)
				{
					if (!_registeredInjectionKeys.Add(dedupKey))
					{
						RecordWarning(
							diagnostics,
							$"Duplicate injection declaration ignored globally. key='{dedupKey}'.",
							mixinType,
							handlerMethod,
							injectionAttribute
						);
						continue;
					}

					if (!_assemblyInjectionKeys.TryGetValue(assemblyCacheKey, out HashSet<string>? assemblyKeys) || assemblyKeys == null)
					{
						assemblyKeys = new HashSet<string>(StringComparer.Ordinal);
						_assemblyInjectionKeys[assemblyCacheKey] = assemblyKeys;
					}

					assemblyKeys.Add(dedupKey);
				}

				validatedInjections.Add(validated);
			}
		}

		return validatedInjections;
	}

	private List<ValidatedShadowField> ScanShadowFields(
		Type mixinType,
		global::ReForge.MixinAttribute mixinAttribute,
		string assemblyCacheKey,
		List<MixinScanDiagnostic> diagnostics)
	{
		FieldInfo[] fields = mixinType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
		List<ValidatedShadowField> validatedShadows = new();
		HashSet<string> localDedupKeys = new(StringComparer.Ordinal);

		for (int i = 0; i < fields.Length; i++)
		{
			FieldInfo mixinField = fields[i];
			if (!Attribute.IsDefined(mixinField, typeof(global::ReForge.ShadowAttribute), inherit: false))
			{
				continue;
			}

			object[] attributes = mixinField.GetCustomAttributes(typeof(global::ReForge.ShadowAttribute), inherit: false);
			if (attributes.Length == 0)
			{
				continue;
			}

			for (int j = 0; j < attributes.Length; j++)
			{
				global::ReForge.ShadowAttribute? shadowAttribute = attributes[j] as global::ReForge.ShadowAttribute;
				if (shadowAttribute == null)
				{
					continue;
				}

				if (!_validation.TryValidateShadowField(
						mixinType,
						mixinField,
						shadowAttribute,
						mixinAttribute.TargetType,
						out ValidatedShadowField? validated,
						out string error))
				{
					RecordError(diagnostics, error, mixinType, handlerMethod: null, attribute: null);
					continue;
				}

				if (validated == null)
				{
					RecordError(
						diagnostics,
						$"Shadow validation returned null result. mixinType='{mixinType.FullName}', targetType='{mixinAttribute.TargetType.FullName}', member='{mixinField.Name}'.",
						mixinType,
						handlerMethod: null,
						attribute: null
					);
					continue;
				}

				string dedupKey = BuildShadowDedupKey(mixinType, mixinAttribute.TargetType, validated);
				if (!localDedupKeys.Add(dedupKey))
				{
					RecordWarning(
						diagnostics,
						$"Duplicate shadow declaration ignored in mixin scope. key='{dedupKey}'.",
						mixinType,
						handlerMethod: null,
						attribute: null
					);
					continue;
				}

				lock (_syncRoot)
				{
					if (!_registeredShadowKeys.Add(dedupKey))
					{
						RecordWarning(
							diagnostics,
							$"Duplicate shadow declaration ignored globally. key='{dedupKey}'.",
							mixinType,
							handlerMethod: null,
							attribute: null
						);
						continue;
					}

					if (!_assemblyShadowKeys.TryGetValue(assemblyCacheKey, out HashSet<string>? assemblyShadowKeys) || assemblyShadowKeys == null)
					{
						assemblyShadowKeys = new HashSet<string>(StringComparer.Ordinal);
						_assemblyShadowKeys[assemblyCacheKey] = assemblyShadowKeys;
					}

					assemblyShadowKeys.Add(dedupKey);
				}

				validatedShadows.Add(validated);
			}
		}

		return validatedShadows;
	}

	private static string BuildAssemblyCacheKey(Assembly assembly)
	{
		return string.Concat(assembly.FullName ?? assembly.GetName().Name ?? "Unknown", ":", assembly.ManifestModule.ModuleVersionId);
	}

	private static Type[] SafeGetTypes(Assembly assembly)
	{
		try
		{
			return assembly.GetTypes();
		}
		catch (ReflectionTypeLoadException exception)
		{
			Type[] partialTypes = Array.FindAll(exception.Types, static type => type != null)!;
			GD.PrintErr($"[ReForge.Mixins] Partial type load in assembly '{assembly.FullName}'. {exception}");
			return partialTypes;
		}
	}

	private static string BuildInjectionDedupKey(Type mixinType, Type targetType, ValidatedMixinInjection validated)
	{
		global::ReForge.InjectionAttributeBase attribute = validated.Attribute;
		return string.Concat(
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
	}

	private static string BuildShadowDedupKey(Type mixinType, Type targetType, ValidatedShadowField validated)
	{
		return string.Concat(
			mixinType.AssemblyQualifiedName,
			":",
			targetType.AssemblyQualifiedName,
			":",
			validated.MixinField.MetadataToken,
			":",
			validated.TargetName,
			":",
			validated.Optional ? "1" : "0"
		);
	}

	private static void RecordError(
		List<MixinScanDiagnostic> diagnostics,
		string message,
		Type mixinType,
		MethodInfo? handlerMethod,
		global::ReForge.InjectionAttributeBase? attribute)
	{
		diagnostics.Add(new MixinScanDiagnostic(MixinScanDiagnosticSeverity.Error, message, mixinType, handlerMethod, attribute));
		GD.PrintErr($"[ReForge.Mixins] {message}");
	}

	private static void RecordWarning(
		List<MixinScanDiagnostic> diagnostics,
		string message,
		Type mixinType,
		MethodInfo? handlerMethod,
		global::ReForge.InjectionAttributeBase? attribute)
	{
		diagnostics.Add(new MixinScanDiagnostic(MixinScanDiagnosticSeverity.Warning, message, mixinType, handlerMethod, attribute));
		GD.Print($"[ReForge.Mixins] {message}");
	}
}
