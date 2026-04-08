#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;

namespace ReForgeFramework.Mixins.Runtime;

public sealed record MixinAppliedEntry(
	string InjectionDescriptorKey,
	string ConflictKey,
	string MixinId,
	InjectionKind Kind,
	string HarmonyId,
	MethodBase TargetMethod,
	MethodInfo HandlerMethod,
	DateTimeOffset AppliedAtUtc
);

internal sealed class MixinAppliedRegistry
{
	private readonly object _syncRoot = new();
	private readonly Dictionary<string, MixinAppliedEntry> _byInjectionKey = new(StringComparer.Ordinal);
	private readonly Dictionary<string, MixinAppliedEntry> _byConflictKey = new(StringComparer.Ordinal);

	public bool TryGetByInjectionKey(string injectionDescriptorKey, out MixinAppliedEntry? entry)
	{
		ArgumentNullException.ThrowIfNull(injectionDescriptorKey);
		lock (_syncRoot)
		{
			return _byInjectionKey.TryGetValue(injectionDescriptorKey, out entry);
		}
	}

	public bool TryGetByConflictKey(string conflictKey, out MixinAppliedEntry? entry)
	{
		ArgumentNullException.ThrowIfNull(conflictKey);
		lock (_syncRoot)
		{
			return _byConflictKey.TryGetValue(conflictKey, out entry);
		}
	}

	public void Register(MixinAppliedEntry entry)
	{
		ArgumentNullException.ThrowIfNull(entry);
		lock (_syncRoot)
		{
			_byInjectionKey[entry.InjectionDescriptorKey] = entry;
			_byConflictKey[entry.ConflictKey] = entry;
		}
	}

	public bool UnregisterByInjectionKey(string injectionDescriptorKey)
	{
		ArgumentNullException.ThrowIfNull(injectionDescriptorKey);
		lock (_syncRoot)
		{
			if (!_byInjectionKey.Remove(injectionDescriptorKey, out MixinAppliedEntry? existing) || existing == null)
			{
				return false;
			}

			_byConflictKey.Remove(existing.ConflictKey);
			return true;
		}
	}

	public int RemoveByHarmonyId(string harmonyId)
	{
		ArgumentNullException.ThrowIfNull(harmonyId);

		List<MixinAppliedEntry> toRemove = new();
		lock (_syncRoot)
		{
			foreach (KeyValuePair<string, MixinAppliedEntry> pair in _byInjectionKey)
			{
				if (!string.Equals(pair.Value.HarmonyId, harmonyId, StringComparison.Ordinal))
				{
					continue;
				}

				toRemove.Add(pair.Value);
			}

			for (int i = 0; i < toRemove.Count; i++)
			{
				MixinAppliedEntry entry = toRemove[i];
				_byInjectionKey.Remove(entry.InjectionDescriptorKey);
				_byConflictKey.Remove(entry.ConflictKey);
			}
		}

		return toRemove.Count;
	}

	public IReadOnlyList<MixinAppliedEntry> Snapshot()
	{
		lock (_syncRoot)
		{
			List<MixinAppliedEntry> snapshot = new(_byInjectionKey.Values);
			snapshot.Sort(static (a, b) => string.CompareOrdinal(a.InjectionDescriptorKey, b.InjectionDescriptorKey));
			return new ReadOnlyCollection<MixinAppliedEntry>(snapshot);
		}
	}

	public static string BuildConflictKey(MethodBase targetMethod, string patchSlot)
	{
		ArgumentNullException.ThrowIfNull(targetMethod);
		ArgumentNullException.ThrowIfNull(patchSlot);
		Module module = targetMethod.Module;
		return string.Concat(module.ModuleVersionId, ":", targetMethod.MetadataToken, ":", patchSlot);
	}
}
