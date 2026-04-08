#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;
using Godot;

namespace ReForgeFramework.Mixins.Runtime;

internal sealed class MixinScanner
{
	private readonly object _syncRoot = new();
	private readonly Dictionary<string, MixinScanResult> _scanCache = new(StringComparer.Ordinal);
	private readonly HashSet<string> _registeredInjectionKeys = new(StringComparer.Ordinal);
	private readonly MixinValidation _validation = new();
	private readonly MixinDescriptorBuilder _descriptorBuilder = new();

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

		for (int i = 0; i < types.Length; i++)
		{
			Type? type = types[i];
			if (type == null)
			{
				continue;
			}

			ScanType(type, descriptors, diagnostics);
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

	public void InvalidateCache(Assembly? assembly = null)
	{
		lock (_syncRoot)
		{
			if (assembly == null)
			{
				_scanCache.Clear();
				_registeredInjectionKeys.Clear();
				return;
			}

			_scanCache.Remove(BuildAssemblyCacheKey(assembly));
		}
	}

	private void ScanType(Type mixinType, List<MixinDescriptor> descriptors, List<MixinScanDiagnostic> diagnostics)
	{
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

			List<ValidatedMixinInjection> validatedInjections = ScanInjectionMethods(mixinType, mixinAttribute, diagnostics);
			if (validatedInjections.Count == 0)
			{
				RecordWarning(
					diagnostics,
					$"Mixin has no valid injection methods. mixinType='{mixinType.FullName}', targetType='{mixinAttribute.TargetType.FullName}'.",
					mixinType,
					handlerMethod: null,
					attribute: null
				);
				continue;
			}

			MixinDescriptor descriptor = _descriptorBuilder.Build(mixinType, mixinAttribute, validatedInjections);
			descriptors.Add(descriptor);
		}
	}

	private List<ValidatedMixinInjection> ScanInjectionMethods(
		Type mixinType,
		global::ReForge.MixinAttribute mixinAttribute,
		List<MixinScanDiagnostic> diagnostics)
	{
		MethodInfo[] methods = mixinType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
		List<ValidatedMixinInjection> validatedInjections = new();
		HashSet<string> localDedupKeys = new(StringComparer.Ordinal);

		for (int i = 0; i < methods.Length; i++)
		{
			MethodInfo handlerMethod = methods[i];
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
				}

				validatedInjections.Add(validated);
			}
		}

		return validatedInjections;
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
