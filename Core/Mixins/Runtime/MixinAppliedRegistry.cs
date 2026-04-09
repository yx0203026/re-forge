#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;

namespace ReForgeFramework.Mixins.Runtime;

/// <summary>
/// 已应用补丁的条目记录，包含补丁应用的完整元数据。
/// </summary>
public sealed record MixinAppliedEntry(
	/// <summary>注入描述符的唯一键。</summary>
	string InjectionDescriptorKey,

	/// <summary>冲突检测键。</summary>
	string ConflictKey,

	/// <summary>应用此补丁的 Mixin 标识。</summary>
	string MixinId,

	/// <summary>注入类型。</summary>
	InjectionKind Kind,

	/// <summary>使用的 Harmony 实例 ID。</summary>
	string HarmonyId,

	/// <summary>被修补的目标方法。</summary>
	MethodBase TargetMethod,

	/// <summary>实际应用的补丁方法。</summary>
	MethodInfo AppliedPatchMethod,

	/// <summary>在 Mixin 中声明的处理方法。</summary>
	MethodInfo DeclaredHandlerMethod,

	/// <summary>补丁应用的时间戳（UTC）。</summary>
	DateTimeOffset AppliedAtUtc
);

/// <summary>
/// 已应用补丁的注册表。维护当前所有活跃补丁的映射，用于冲突检测与卸载。
/// </summary>
/// <remarks>
/// 此注册表采用双重索引结构：
/// • _byInjectionKey：按注入描述符键索引，用于精确查询
/// • _byConflictKey：按冲突键索引，用于冲突检测
/// 
/// 线程安全：所有操作都在 _syncRoot 锁保护下执行。
/// </remarks>
internal sealed class MixinAppliedRegistry
{
	private readonly object _syncRoot = new();
	private readonly Dictionary<string, MixinAppliedEntry> _byInjectionKey = new(StringComparer.Ordinal);
	private readonly Dictionary<string, MixinAppliedEntry> _byConflictKey = new(StringComparer.Ordinal);

	/// <summary>
	/// 尝试按注入键查询已应用的补丁。
	/// </summary>
	/// <param name="injectionDescriptorKey">注入描述符键。</param>
	/// <param name="entry">查询到的补丁条目（若存在）。</param>
	/// <returns>是否查询到补丁。</returns>
	/// <exception cref="ArgumentNullException">当 injectionDescriptorKey 为 null 时。</exception>
	public bool TryGetByInjectionKey(string injectionDescriptorKey, out MixinAppliedEntry? entry)
	{
		ArgumentNullException.ThrowIfNull(injectionDescriptorKey);
		lock (_syncRoot)
		{
			return _byInjectionKey.TryGetValue(injectionDescriptorKey, out entry);
		}
	}

	/// <summary>
	/// 尝试按冲突键查询已应用的补丁。
	/// </summary>
	/// <param name="conflictKey">冲突检测键。</param>
	/// <param name="entry">查询到的补丁条目（若存在）。</param>
	/// <returns>是否查询到补丁。</returns>
	/// <exception cref="ArgumentNullException">当 conflictKey 为 null 时。</exception>
	public bool TryGetByConflictKey(string conflictKey, out MixinAppliedEntry? entry)
	{
		ArgumentNullException.ThrowIfNull(conflictKey);
		lock (_syncRoot)
		{
			return _byConflictKey.TryGetValue(conflictKey, out entry);
		}
	}

	/// <summary>
	/// 注册一个已应用的补丁。
	/// </summary>
	/// <param name="entry">待注册的补丁条目。</param>
	/// <exception cref="ArgumentNullException">当 entry 为 null 时。</exception>
	public void Register(MixinAppliedEntry entry)
	{
		ArgumentNullException.ThrowIfNull(entry);
		lock (_syncRoot)
		{
			_byInjectionKey[entry.InjectionDescriptorKey] = entry;
			_byConflictKey[entry.ConflictKey] = entry;
		}
	}

	/// <summary>
	/// 按注入键反注册补丁。
	/// </summary>
	/// <param name="injectionDescriptorKey">注入描述符键。</param>
	/// <returns>是否成功反注册（即补丁确实存在并已移除）。</returns>
	/// <exception cref="ArgumentNullException">当 injectionDescriptorKey 为 null 时。</exception>
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

	/// <summary>
	/// 移除指定 Harmony ID 对应的所有补丁。
	/// </summary>
	/// <param name="harmonyId">Harmony 实例 ID。</param>
	/// <returns>成功移除的补丁数量。</returns>
	/// <exception cref="ArgumentNullException">当 harmonyId 为 null 时。</exception>
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
