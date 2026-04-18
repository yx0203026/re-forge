#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using ReForgeFramework.Api.Events;

namespace ReForgeFramework.EventWheel;

/// <summary>
/// EventModel 反射桥接器。
/// 负责读取/回写当前选项，并将规划层选项转换为运行时 EventOption。
/// </summary>
internal sealed class EventModelBridge
{
	private static readonly object AccessorSyncRoot = new();
	private static readonly Dictionary<Type, ModelAccessors> AccessorCache = new();

	private static readonly string[] CurrentOptionsPropertyCandidates =
	{
		"CurrentOptions",
		"Options"
	};

	private static readonly string[] CurrentOptionsFieldCandidates =
	{
		"_currentOptions",
		"_options",
		"<CurrentOptions>k__BackingField"
	};

	private static readonly string[] ApplyMethodCandidates =
	{
		"SetCurrentOptions",
		"SetOptions",
		"ApplyOptions"
	};

	/// <summary>
	/// 尝试快照当前 EventModel 的选项集合。
	/// </summary>
	public bool TrySnapshotCurrentOptions(EventModel model, out IReadOnlyList<EventOption> snapshot, out string message)
	{
		ArgumentNullException.ThrowIfNull(model);

		if (TryGetCurrentOptionsValue(model, out object? raw))
		{
			snapshot = ExtractEventOptions(raw);
			message = string.Empty;
			return true;
		}

		snapshot = Array.Empty<EventOption>();
		message = "Unable to read EventModel current options via reflection accessors.";
		return false;
	}

	/// <summary>
	/// 尝试将选项集合应用回 EventModel。
	/// 会按“方法 -> 属性 -> 字段”的顺序进行兼容写入。
	/// </summary>
	public bool TryApplyCurrentOptions(EventModel model, IReadOnlyList<EventOption> options, out string message)
	{
		ArgumentNullException.ThrowIfNull(model);
		ArgumentNullException.ThrowIfNull(options);

		Type modelType = model.GetType();
		ModelAccessors accessors = GetOrCreateModelAccessors(modelType);

		if (accessors.ApplyMethod != null)
		{
			if (TryInvokeApplyMethod(model, accessors.ApplyMethod, options, out message))
			{
				return true;
			}
		}

		if (accessors.CurrentOptionsProperty != null)
		{
			MethodInfo? setter = accessors.CurrentOptionsProperty.GetSetMethod(nonPublic: true);
			if (setter == null)
			{
				// 继续尝试字段写入路径。
			}
			else if (TryConvertOptions(options, accessors.CurrentOptionsProperty.PropertyType, out object? converted))
			{
				try
				{
					setter.Invoke(model, new[] { converted });
					message = string.Empty;
					return true;
				}
				catch (Exception ex)
				{
					message = $"Failed to set property '{accessors.CurrentOptionsProperty.Name}'. {ex.GetType().Name}: {ex.Message}";
					return false;
				}
			}
		}

		if (accessors.CurrentOptionsField != null)
		{
			if (!TryConvertOptions(options, accessors.CurrentOptionsField.FieldType, out object? converted))
			{
				message = $"Cannot convert options to field type '{accessors.CurrentOptionsField.FieldType.FullName}'.";
				return false;
			}

			try
			{
				accessors.CurrentOptionsField.SetValue(model, converted);
				message = string.Empty;
				return true;
			}
			catch (Exception ex)
			{
				message = $"Failed to set field '{accessors.CurrentOptionsField.Name}'. {ex.GetType().Name}: {ex.Message}";
				return false;
			}
		}

		message = "Unable to apply options to EventModel via known methods/properties/fields.";
		return false;
	}

	/// <summary>
	/// 基于规划选项构建运行时选项。
	/// 优先复用已有选项，其次通过工厂或占位构造生成新选项。
	/// </summary>
	public IReadOnlyList<EventOption> BuildRuntimeOptions(
		EventModel model,
		IReadOnlyList<EventMutationPlannedOption> plannedOptions,
		IReadOnlyList<EventOption> existingOptions,
		string eventId,
		string sourceModId,
		List<EventMutationWarning> warnings)
	{
		ArgumentNullException.ThrowIfNull(model);
		ArgumentNullException.ThrowIfNull(plannedOptions);
		ArgumentNullException.ThrowIfNull(existingOptions);
		ArgumentNullException.ThrowIfNull(warnings);

		Dictionary<string, EventOption> existingByKey = new(StringComparer.Ordinal);
		for (int i = 0; i < existingOptions.Count; i++)
		{
			EventOption existing = existingOptions[i];
			if (existing == null)
			{
				continue;
			}

			string textKey = existing.TextKey?.Trim() ?? string.Empty;
			if (textKey.Length == 0)
			{
				continue;
			}

			if (!existingByKey.ContainsKey(textKey))
			{
				existingByKey[textKey] = existing;
			}
		}

		HashSet<string> dedupe = new(StringComparer.Ordinal);
		List<EventOption> runtime = new(plannedOptions.Count);
		for (int i = 0; i < plannedOptions.Count; i++)
		{
			EventMutationPlannedOption planned = plannedOptions[i];
			if (planned == null)
			{
				warnings.Add(new EventMutationWarning(
					Severity: EventWheelSeverity.Warning,
					Code: "execute.planned_option_null",
					Message: $"Skipped null planned option at index {i}.",
					EventId: eventId,
					SourceModId: sourceModId
				));
				continue;
			}

			if (!dedupe.Add(planned.OptionKey))
			{
				warnings.Add(new EventMutationWarning(
					Severity: EventWheelSeverity.Warning,
					Code: "execute.planned_option_duplicate",
					Message: $"Skipped duplicate planned option during runtime build. optionKey='{planned.OptionKey}'.",
					EventId: eventId,
					SourceModId: sourceModId,
					TargetOptionKey: planned.OptionKey
				));
				continue;
			}

			if (existingByKey.TryGetValue(planned.OptionKey, out EventOption? existing))
			{
				runtime.Add(planned.IsLocked ? CreateLockedOption(model, existing) : existing);
				continue;
			}

			try
			{
				EventOption created = CreatePlaceholderOption(model, planned);
				runtime.Add(planned.IsLocked ? CreateLockedOption(model, created) : created);
			}
			catch (Exception ex)
			{
				warnings.Add(new EventMutationWarning(
					Severity: EventWheelSeverity.Error,
					Code: "execute.placeholder_create_failed",
					Message: $"Failed to create placeholder option. optionKey='{planned.OptionKey}'. {ex.GetType().Name}: {ex.Message}",
					EventId: eventId,
					SourceModId: sourceModId,
					TargetOptionKey: planned.OptionKey
				));
			}
		}

		return runtime;
	}

	private static EventOption CreateLockedOption(EventModel model, EventOption source)
	{
		EventOption locked = new EventOption(
			model,
			onChosen: null,
			source.Title,
			source.Description,
			source.TextKey,
			source.HoverTips);

		if (source.Relic != null)
		{
			locked.WithRelic(source.Relic);
		}

		return locked;
	}

	private static EventOption CreatePlaceholderOption(EventModel model, EventMutationPlannedOption planned)
	{
		if (ReForge.EventWheel.TryCreateOption(model, planned, out EventOption? factoryOption) && factoryOption != null)
		{
			return factoryOption;
		}

		LocString title = ResolveLocString(model, planned.TitleKey, forTitle: true)
			?? new LocString("events", planned.TitleKey);
		LocString description = ResolveLocString(model, planned.DescriptionKey, forTitle: false)
			?? new LocString("events", planned.DescriptionKey);

		return new EventOption(
			model,
			onChosen: null,
			title,
			description,
			planned.OptionKey,
			hoverTips: null!);
	}

	private static LocString? ResolveLocString(EventModel model, string key, bool forTitle)
	{
		ModelAccessors accessors = GetOrCreateModelAccessors(model.GetType());
		MethodInfo? method = forTitle ? accessors.GetOptionTitleMethod : accessors.GetOptionDescriptionMethod;

		if (method == null)
		{
			return null;
		}

		try
		{
			return method.Invoke(model, new object?[] { key }) as LocString;
		}
		catch (Exception ex)
		{
			string methodName = forTitle ? "GetOptionTitle" : "GetOptionDescription";
			GD.PrintErr($"[ReForge.EventWheel] Failed to resolve loc string by '{methodName}' for key '{key}'. {ex.GetType().Name}: {ex.Message}");
			return null;
		}
	}

	private static bool TryGetCurrentOptionsValue(EventModel model, out object? raw)
	{
		ModelAccessors accessors = GetOrCreateModelAccessors(model.GetType());
		if (accessors.CurrentOptionsProperty?.GetGetMethod(nonPublic: true) != null)
		{
			try
			{
				raw = accessors.CurrentOptionsProperty.GetValue(model);
				return true;
			}
			catch
			{
				// 继续尝试字段读取路径。
			}
		}

		if (accessors.CurrentOptionsField != null)
		{
			try
			{
				raw = accessors.CurrentOptionsField.GetValue(model);
				return true;
			}
			catch
			{
				// ignore
			}
		}

		raw = null;
		return false;
	}

	private static MethodInfo? ResolveApplyMethod(Type modelType, string methodName)
	{
		MethodInfo[] methods = modelType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		for (int i = 0; i < methods.Length; i++)
		{
			MethodInfo method = methods[i];
			if (!StringComparer.Ordinal.Equals(method.Name, methodName))
			{
				continue;
			}

			ParameterInfo[] parameters = method.GetParameters();
			if (parameters.Length == 1)
			{
				return method;
			}
		}

		return null;
	}

	private static ModelAccessors GetOrCreateModelAccessors(Type modelType)
	{
		lock (AccessorSyncRoot)
		{
			if (AccessorCache.TryGetValue(modelType, out ModelAccessors? cached))
			{
				return cached;
			}

			ModelAccessors created = BuildModelAccessors(modelType);
			AccessorCache[modelType] = created;
			return created;
		}
	}

	private static ModelAccessors BuildModelAccessors(Type modelType)
	{
		MethodInfo? applyMethod = null;
		for (int i = 0; i < ApplyMethodCandidates.Length; i++)
		{
			applyMethod = ResolveApplyMethod(modelType, ApplyMethodCandidates[i]);
			if (applyMethod != null)
			{
				break;
			}
		}

		PropertyInfo? optionsProperty = null;
		for (int i = 0; i < CurrentOptionsPropertyCandidates.Length; i++)
		{
			optionsProperty = modelType.GetProperty(
				CurrentOptionsPropertyCandidates[i],
				BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (optionsProperty != null)
			{
				break;
			}
		}

		FieldInfo? optionsField = null;
		for (int i = 0; i < CurrentOptionsFieldCandidates.Length; i++)
		{
			optionsField = modelType.GetField(
				CurrentOptionsFieldCandidates[i],
				BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (optionsField != null)
			{
				break;
			}
		}

		MethodInfo? getOptionTitleMethod = modelType.GetMethod(
			"GetOptionTitle",
			BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
			null,
			new[] { typeof(string) },
			null);
		MethodInfo? getOptionDescriptionMethod = modelType.GetMethod(
			"GetOptionDescription",
			BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
			null,
			new[] { typeof(string) },
			null);

		return new ModelAccessors(
			applyMethod,
			optionsProperty,
			optionsField,
			getOptionTitleMethod,
			getOptionDescriptionMethod);
	}

	private static bool TryInvokeApplyMethod(
		EventModel model,
		MethodInfo method,
		IReadOnlyList<EventOption> options,
		out string message)
	{
		ParameterInfo parameter = method.GetParameters()[0];
		if (!TryConvertOptions(options, parameter.ParameterType, out object? converted))
		{
			message = $"Cannot convert options to method parameter type '{parameter.ParameterType.FullName}'.";
			return false;
		}

		try
		{
			object? ignoredResult = method.Invoke(model, new[] { converted });
			message = string.Empty;
			return true;
		}
		catch (Exception ex)
		{
			message = $"Failed to invoke '{method.Name}'. {ex.GetType().Name}: {ex.Message}";
			return false;
		}
	}

	private static bool TryConvertOptions(IReadOnlyList<EventOption> source, Type destinationType, out object? converted)
	{
		if (destinationType.IsAssignableFrom(typeof(IReadOnlyList<EventOption>)))
		{
			converted = source;
			return true;
		}

		if (destinationType.IsAssignableFrom(typeof(List<EventOption>)))
		{
			converted = new List<EventOption>(source);
			return true;
		}

		if (destinationType.IsArray && destinationType.GetElementType() == typeof(EventOption))
		{
			EventOption[] arr = new EventOption[source.Count];
			for (int i = 0; i < source.Count; i++)
			{
				arr[i] = source[i];
			}
			converted = arr;
			return true;
		}

		if (typeof(IEnumerable).IsAssignableFrom(destinationType))
		{
			converted = new List<EventOption>(source);
			return true;
		}

		converted = null;
		return false;
	}

	private static IReadOnlyList<EventOption> ExtractEventOptions(object? raw)
	{
		if (raw == null)
		{
			return Array.Empty<EventOption>();
		}

		if (raw is IReadOnlyList<EventOption> readOnly)
		{
			EventOption[] copied = new EventOption[readOnly.Count];
			for (int i = 0; i < readOnly.Count; i++)
			{
				copied[i] = readOnly[i];
			}
			return copied;
		}

		if (raw is IEnumerable<EventOption> typedEnumerable)
		{
			List<EventOption> list = new();
			foreach (EventOption option in typedEnumerable)
			{
				list.Add(option);
			}
			return list.ToArray();
		}

		if (raw is IEnumerable enumerable)
		{
			List<EventOption> list = new();
			foreach (object? item in enumerable)
			{
				if (item is EventOption option)
				{
					list.Add(option);
				}
			}
			return list.ToArray();
		}

		return Array.Empty<EventOption>();
	}

	private sealed record ModelAccessors(
		MethodInfo? ApplyMethod,
		PropertyInfo? CurrentOptionsProperty,
		FieldInfo? CurrentOptionsField,
		MethodInfo? GetOptionTitleMethod,
		MethodInfo? GetOptionDescriptionMethod);
}