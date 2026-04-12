#nullable enable

using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using Godot;
using LinqExpression = System.Linq.Expressions.Expression;

namespace ReForgeFramework.EventBus;

internal sealed class EventBusAttributeScanner
{
	private readonly EventBusRegistry _registry;
	private readonly object _syncRoot = new();
	private readonly HashSet<string> _registeredKeys = new(StringComparer.Ordinal);
	private readonly HashSet<string> _scannedTypeKeys = new(StringComparer.Ordinal);
	private readonly Dictionary<MethodInfo, Action<IEventArg?>> _compiledHandlerCache = new();

	public EventBusAttributeScanner(EventBusRegistry registry)
	{
		_registry = registry;
	}

	public void ScanAndRegister(Assembly assembly)
	{
		ArgumentNullException.ThrowIfNull(assembly);

		Type[] types;
		try
		{
			types = assembly.GetTypes();
		}
		catch (ReflectionTypeLoadException exception)
		{
			types = Array.FindAll(exception.Types, type => type != null)!;
			GD.PrintErr($"[ReForge.EventBus] Partial type load in assembly '{assembly.FullName}'. {exception}");
		}

		for (int i = 0; i < types.Length; i++)
		{
			Type? type = types[i];
			if (type == null)
			{
				continue;
			}

			ScanAndRegister(type);
		}
	}

	public void ScanAndRegister(Type rootType)
	{
		ArgumentNullException.ThrowIfNull(rootType);

		string typeKey = BuildTypeCacheKey(rootType);
		lock (_syncRoot)
		{
			if (!_scannedTypeKeys.Add(typeKey))
			{
				return;
			}
		}

		MethodInfo[] methods = rootType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
		for (int i = 0; i < methods.Length; i++)
		{
			MethodInfo method = methods[i];
			object[] attributes = method.GetCustomAttributes(typeof(global::ReForge.EventBus.ListenerAttribute), inherit: false);
			if (attributes.Length == 0)
			{
				continue;
			}

			for (int j = 0; j < attributes.Length; j++)
			{
				global::ReForge.EventBus.ListenerAttribute? listener = attributes[j] as global::ReForge.EventBus.ListenerAttribute;
				if (listener == null)
				{
					continue;
				}

				RegisterMethod(rootType, method, listener);
			}
		}
	}

	private void RegisterMethod(Type ownerType, MethodInfo method, global::ReForge.EventBus.ListenerAttribute attribute)
	{
		if (!method.IsStatic)
		{
			GD.PrintErr($"[ReForge.EventBus] Attribute listener must be static. method='{ownerType.FullName}.{method.Name}'.");
			return;
		}

		if (string.IsNullOrWhiteSpace(attribute.Id) || string.IsNullOrWhiteSpace(attribute.BusId))
		{
			GD.PrintErr($"[ReForge.EventBus] Listener attribute missing Id/BusId. method='{ownerType.FullName}.{method.Name}'.");
			return;
		}

		ParameterInfo[] parameters = method.GetParameters();
		if (parameters.Length > 1)
		{
			GD.PrintErr($"[ReForge.EventBus] Listener method accepts at most one parameter. method='{ownerType.FullName}.{method.Name}'.");
			return;
		}

		if (parameters.Length == 1 && parameters[0].ParameterType.IsByRef)
		{
			GD.PrintErr($"[ReForge.EventBus] Listener parameter cannot be ref/out. method='{ownerType.FullName}.{method.Name}'.");
			return;
		}

		if (parameters.Length == 1 && !typeof(IEventArg).IsAssignableFrom(parameters[0].ParameterType))
		{
			GD.PrintErr($"[ReForge.EventBus] Listener parameter must implement IEventArg. method='{ownerType.FullName}.{method.Name}', parameter='{parameters[0].ParameterType.FullName}'.");
			return;
		}

		string registrationKey = $"{ownerType.AssemblyQualifiedName}:{method.MetadataToken}:{attribute.Id}:{attribute.BusId}";
		lock (_syncRoot)
		{
			if (!_registeredKeys.Add(registrationKey))
			{
				return;
			}
		}

		Type? parameterType = parameters.Length == 0 ? null : parameters[0].ParameterType;
		Action<IEventArg?> handler = GetOrCreateHandler(method, parameterType);

		bool replaced = _registry.Upsert(
			new EventSubscription(
				attribute.Id,
				attribute.BusId,
				parameterType,
				handler,
				source: $"Attribute:{ownerType.FullName}.{method.Name}"
			)
		);

		if (replaced)
		{
			GD.Print($"[ReForge.EventBus] Attribute listener replaced. eventId='{attribute.Id}', busId='{attribute.BusId}', method='{ownerType.FullName}.{method.Name}'.");
		}
	}

	private Action<IEventArg?> GetOrCreateHandler(MethodInfo method, Type? parameterType)
	{
		lock (_syncRoot)
		{
			if (_compiledHandlerCache.TryGetValue(method, out Action<IEventArg?>? cached))
			{
				return cached;
			}
		}

		Action<IEventArg?> compiled = CreateHandler(method, parameterType);
		lock (_syncRoot)
		{
			_compiledHandlerCache[method] = compiled;
		}

		return compiled;
	}

	private static Action<IEventArg?> CreateHandler(MethodInfo method, Type? parameterType)
	{
		if (parameterType == null)
		{
			Action action = (Action)method.CreateDelegate(typeof(Action));
			return _ => action();
		}

		ParameterExpression payload = LinqExpression.Parameter(typeof(IEventArg), "payload");
		UnaryExpression castPayload = LinqExpression.Convert(payload, parameterType);
		MethodCallExpression invoke = LinqExpression.Call(method, castPayload);
		return LinqExpression.Lambda<Action<IEventArg?>>(invoke, payload).Compile();
	}

	private static string BuildTypeCacheKey(Type type)
	{
		return string.Concat(type.AssemblyQualifiedName ?? type.FullName ?? type.Name, ":", type.MetadataToken);
	}
}
