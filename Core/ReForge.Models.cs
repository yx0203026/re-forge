#nullable enable

using System;
using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;

public static partial class ReForge
{
	/// <summary>
	/// ReForge 模型注册能力入口。
	/// </summary>
	public static class Models
	{
		private static readonly object _poolRegistrationSync = new();
		private static readonly HashSet<string> _poolRegistrations = new(StringComparer.Ordinal);

		/// <summary>
		/// 官方封装：将模型类型注册到指定模型池。
		/// 失败时自动记录日志并返回 false。
		/// </summary>
		public static bool TryAddModelToPool<TPool, TModel>(string? logOwner = null)
			where TPool : AbstractModel, IPoolModel
			where TModel : AbstractModel
		{
			string owner = string.IsNullOrWhiteSpace(logOwner) ? "ReForge.Models" : logOwner;

			try
			{
				ModHelper.AddModelToPool<TPool, TModel>();
				return true;
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[{owner}] failed to register pool entry '{typeof(TPool).FullName}' <- '{typeof(TModel).FullName}'. {ex}");
				return false;
			}
		}

		/// <summary>
		/// 官方封装：同一进程生命周期内仅尝试注册一次。
		/// 注册成功后会缓存结果，后续直接返回 true。
		/// </summary>
		public static bool TryAddModelToPoolOnce<TPool, TModel>(string? logOwner = null)
			where TPool : AbstractModel, IPoolModel
			where TModel : AbstractModel
		{
			string key = $"{typeof(TPool).AssemblyQualifiedName}|{typeof(TModel).AssemblyQualifiedName}";

			lock (_poolRegistrationSync)
			{
				if (_poolRegistrations.Contains(key))
				{
					return true;
				}
			}

			if (!TryAddModelToPool<TPool, TModel>(logOwner))
			{
				return false;
			}

			lock (_poolRegistrationSync)
			{
				_poolRegistrations.Add(key);
			}

			return true;
		}
	}
}
