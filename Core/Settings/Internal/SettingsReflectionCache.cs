#nullable enable

using System;
using System.Collections.Concurrent;
using System.Reflection;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;

namespace ReForgeFramework.Settings.Internal;

/// <summary>
/// 设置系统反射缓存：按 Type 缓存高频成员查询，减少每帧/每次交互的反射开销。
/// </summary>
internal static class SettingsReflectionCache
{
	private static readonly ConcurrentDictionary<Type, MethodInfo?> _switchTabToMethodByType = new();
	private static readonly ConcurrentDictionary<Type, PropertyInfo?> _isLeftPropertyByType = new();
	private static readonly ConcurrentDictionary<Type, PropertyInfo?> _isEnabledPropertyByType = new();
	private static readonly ConcurrentDictionary<Type, MethodInfo?> _enableMethodByType = new();
	private static readonly ConcurrentDictionary<Type, MethodInfo?> _disableMethodByType = new();
	private static readonly ConcurrentDictionary<Type, FieldInfo?> _tabsFieldByType = new();
	private static readonly ConcurrentDictionary<Type, MethodInfo?> _setTextAutoSizeMethodByType = new();
	private static readonly ConcurrentDictionary<Type, PropertyInfo?> _textPropertyByType = new();

	private static readonly Type[] _switchTabToParamTypes = { typeof(NSettingsTab) };
	private static readonly Type[] _setTextAutoSizeParamTypes = { typeof(string) };

	public static MethodInfo? GetSwitchTabToMethod(Type managerType)
	{
		return _switchTabToMethodByType.GetOrAdd(managerType, static type =>
			type.GetMethod(
				"SwitchTabTo",
				BindingFlags.Instance | BindingFlags.NonPublic,
				null,
				_switchTabToParamTypes,
				null));
	}

	public static PropertyInfo? GetIsLeftProperty(Type controlType)
	{
		return _isLeftPropertyByType.GetOrAdd(controlType, static type =>
			type.GetProperty("IsLeft", BindingFlags.Instance | BindingFlags.Public));
	}

	public static PropertyInfo? GetIsEnabledProperty(Type controlType)
	{
		return _isEnabledPropertyByType.GetOrAdd(controlType, static type =>
			type.GetProperty("IsEnabled", BindingFlags.Instance | BindingFlags.Public));
	}

	public static MethodInfo? GetEnableMethod(Type controlType)
	{
		return _enableMethodByType.GetOrAdd(controlType, static type =>
			type.GetMethod("Enable", BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null));
	}

	public static MethodInfo? GetDisableMethod(Type controlType)
	{
		return _disableMethodByType.GetOrAdd(controlType, static type =>
			type.GetMethod("Disable", BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null));
	}

	public static FieldInfo? GetTabsField(Type managerType)
	{
		return _tabsFieldByType.GetOrAdd(managerType, static type =>
			type.GetField("_tabs", BindingFlags.Instance | BindingFlags.NonPublic));
	}

	public static MethodInfo? GetSetTextAutoSizeMethod(Type labelType)
	{
		return _setTextAutoSizeMethodByType.GetOrAdd(labelType, static type =>
			type.GetMethod("SetTextAutoSize", BindingFlags.Instance | BindingFlags.Public, null, _setTextAutoSizeParamTypes, null));
	}

	public static PropertyInfo? GetTextProperty(Type labelType)
	{
		return _textPropertyByType.GetOrAdd(labelType, static type =>
			type.GetProperty("Text", BindingFlags.Instance | BindingFlags.Public));
	}
}
