#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using Godot;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;

public static partial class ReForge
{
	/// <summary>
	/// ReForge 模型注册能力入口。
	/// </summary>
	public static class Models
	{
		private static readonly object _poolRegistrationSync = new();
		private static readonly HashSet<string> _poolRegistrations = new(StringComparer.Ordinal);
		private static readonly object _modelInjectionSync = new();
		private static readonly HashSet<string> _injectedModels = new(StringComparer.Ordinal);
		private static readonly object _ascensionRegistrationSync = new();
		private static readonly HashSet<string> _registeredAscensionModels = new(StringComparer.Ordinal);
		private static readonly object _pendingInjectionSync = new();
		private static readonly Dictionary<string, PendingInjection> _pendingInjections = new(StringComparer.Ordinal);
		private static int _runtimeModelRegistrationReady;

		static Models()
		{
			bool attached = ReForge.Mods.TryHookRunStartedWithRetry(OnRunStarted, "ReForge.Models");
			GD.Print($"[ReForge.Models] static init. runStartedHookAttachedImmediately={attached}.");
		}

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
		/// 官方封装：尝试将模型类型注入到 ModelDb。
		/// 若已存在则视为成功。
		/// </summary>
		public static bool TryInjectModel<TModel>(string? logOwner = null)
			where TModel : AbstractModel
		{
			string owner = string.IsNullOrWhiteSpace(logOwner) ? "ReForge.Models" : logOwner;
			Type modelType = typeof(TModel);
			GD.Print($"[{owner}] TryInjectModel start. modelType={modelType.FullName}, runtimeReady={IsRuntimeModelRegistrationReady()}.");

			if (!IsRuntimeModelRegistrationReady())
			{
				QueueModelInjection(modelType, owner);
				GD.Print($"[{owner}] TryInjectModel deferred. modelType={modelType.FullName} queued until runtime ready.");
				return true;
			}

			try
			{
				if (ModelDb.Contains(modelType))
				{
					GD.Print($"[{owner}] TryInjectModel skipped: model already present. modelType={modelType.FullName}.");
					return true;
				}

				ModelDb.Inject(modelType);
				bool contains = ModelDb.Contains(modelType);
				GD.Print($"[{owner}] TryInjectModel completed. modelType={modelType.FullName}, success={contains}.");
				return contains;
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[{owner}] failed to inject model '{typeof(TModel).FullName}'. {ex}");
				return false;
			}
		}

		/// <summary>
		/// 官方封装：同一进程生命周期内仅尝试注入一次。
		/// 注入成功后会缓存结果，后续直接返回 true。
		/// </summary>
		public static bool TryInjectModelOnce<TModel>(string? logOwner = null)
			where TModel : AbstractModel
		{
			string owner = string.IsNullOrWhiteSpace(logOwner) ? "ReForge.Models" : logOwner;
			string key = typeof(TModel).AssemblyQualifiedName ?? typeof(TModel).FullName ?? typeof(TModel).Name;

			lock (_modelInjectionSync)
			{
				if (_injectedModels.Contains(key))
				{
					GD.Print($"[{owner}] TryInjectModelOnce skipped: already cached. modelType={typeof(TModel).FullName}.");
					return true;
				}
			}

			if (!TryInjectModel<TModel>(logOwner))
			{
				GD.PrintErr($"[{owner}] TryInjectModelOnce failed. modelType={typeof(TModel).FullName}.");
				return false;
			}

			lock (_modelInjectionSync)
			{
				_injectedModels.Add(key);
			}

			GD.Print($"[{owner}] TryInjectModelOnce succeeded. modelType={typeof(TModel).FullName}.");

			return true;
		}

		/// <summary>
		/// 官方封装：同一进程生命周期内仅尝试注册一次。
		/// 注册成功后会缓存结果，后续直接返回 true。
		/// </summary>
		public static bool TryAddModelToPoolOnce<TPool, TModel>(string? logOwner = null)
			where TPool : AbstractModel, IPoolModel
			where TModel : AbstractModel
		{
			string owner = string.IsNullOrWhiteSpace(logOwner) ? "ReForge.Models" : logOwner;
			string key = $"{typeof(TPool).AssemblyQualifiedName}|{typeof(TModel).AssemblyQualifiedName}";

			lock (_poolRegistrationSync)
			{
				if (_poolRegistrations.Contains(key))
				{
					GD.Print($"[{owner}] TryAddModelToPoolOnce skipped: already cached. pool={typeof(TPool).FullName}, model={typeof(TModel).FullName}.");
					return true;
				}
			}

			if (!TryAddModelToPool<TPool, TModel>(logOwner))
			{
				GD.PrintErr($"[{owner}] TryAddModelToPoolOnce failed. pool={typeof(TPool).FullName}, model={typeof(TModel).FullName}.");
				return false;
			}

			lock (_poolRegistrationSync)
			{
				_poolRegistrations.Add(key);
			}

			GD.Print($"[{owner}] TryAddModelToPoolOnce succeeded. pool={typeof(TPool).FullName}, model={typeof(TModel).FullName}.");

			return true;
		}

		/// <summary>
		/// 官方封装：注册 Ascension 模型（等级定义 + 可选效果）。
		/// 模组开发者仅需继承 ReForgeAscensionModel 并调用此方法。
		/// </summary>
		public static bool TryRegisterAscensionModel<TAscensionModel>(string? logOwner = null)
			where TAscensionModel : ReForgeAscensionModel, new()
		{
			string owner = string.IsNullOrWhiteSpace(logOwner) ? "ReForge.Models" : logOwner;
			GD.Print($"[{owner}] TryRegisterAscensionModel start. modelType={typeof(TAscensionModel).FullName}.");

			TAscensionModel model;
			try
			{
				model = new TAscensionModel();
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[{owner}] failed to construct ascension model '{typeof(TAscensionModel).FullName}'. {ex}");
				return false;
			}

			if (model.Level <= 10)
			{
				GD.PrintErr($"[{owner}] ascension model '{typeof(TAscensionModel).FullName}' has invalid level '{model.Level}'. Level must be > 10.");
				return false;
			}

			if (string.IsNullOrWhiteSpace(model.Title) || string.IsNullOrWhiteSpace(model.Description))
			{
				GD.PrintErr($"[{owner}] ascension model '{typeof(TAscensionModel).FullName}' has empty title or description.");
				return false;
			}

			try
			{
				ReForge.Ascension.RegisterLevel(model.Level, model.Title, model.Description);
				GD.Print($"[{owner}] Ascension level registered. level={model.Level}, title='{model.Title}'.");
				if (model.AutoRegisterLevelEffect)
				{
					int minimumAscension = Math.Max(11, model.EffectMinimumAscension);
					ReForge.Ascension.RegisterEffect(minimumAscension, model.OnLevelEffect);
					GD.Print($"[{owner}] Ascension level effect registered. modelType={typeof(TAscensionModel).FullName}, minAscension={minimumAscension}.");
				}

				model.RegisterRuntimeHooks(owner);
				GD.Print($"[{owner}] Ascension runtime hooks registered. modelType={typeof(TAscensionModel).FullName}.");
				model.OnRegistered();
				GD.Print($"[{owner}] TryRegisterAscensionModel completed. modelType={typeof(TAscensionModel).FullName}, level={model.Level}.");
				return true;
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[{owner}] failed to register ascension model '{typeof(TAscensionModel).FullName}'. {ex}");
				return false;
			}
		}

		/// <summary>
		/// 官方封装：同一进程生命周期内仅尝试注册一次 Ascension 模型。
		/// </summary>
		public static bool TryRegisterAscensionModelOnce<TAscensionModel>(string? logOwner = null)
			where TAscensionModel : ReForgeAscensionModel, new()
		{
			string owner = string.IsNullOrWhiteSpace(logOwner) ? "ReForge.Models" : logOwner;
			string key = typeof(TAscensionModel).AssemblyQualifiedName
				?? typeof(TAscensionModel).FullName
				?? typeof(TAscensionModel).Name;

			lock (_ascensionRegistrationSync)
			{
				if (_registeredAscensionModels.Contains(key))
				{
					GD.Print($"[{owner}] TryRegisterAscensionModelOnce skipped: already cached. modelType={typeof(TAscensionModel).FullName}.");
					return true;
				}
			}

			if (!TryRegisterAscensionModel<TAscensionModel>(logOwner))
			{
				GD.PrintErr($"[{owner}] TryRegisterAscensionModelOnce failed. modelType={typeof(TAscensionModel).FullName}.");
				return false;
			}

			lock (_ascensionRegistrationSync)
			{
				_registeredAscensionModels.Add(key);
			}

			GD.Print($"[{owner}] TryRegisterAscensionModelOnce succeeded. modelType={typeof(TAscensionModel).FullName}.");

			return true;
		}

		private static bool IsRuntimeModelRegistrationReady()
		{
			if (Volatile.Read(ref _runtimeModelRegistrationReady) != 0)
			{
				return true;
			}

			return RunManager.Instance?.DebugOnlyGetState() != null;
		}

		private static void QueueModelInjection(Type modelType, string owner)
		{
			string key = modelType.AssemblyQualifiedName ?? modelType.FullName ?? modelType.Name;

			lock (_pendingInjectionSync)
			{
				_pendingInjections[key] = new PendingInjection(modelType, owner);
				GD.Print($"[{owner}] QueueModelInjection. modelType={modelType.FullName}, pendingCount={_pendingInjections.Count}.");
			}
		}

		private static void OnRunStarted(RunState _)
		{
			GD.Print("[ReForge.Models] OnRunStarted received. runtime model registration is now ready.");
			Interlocked.Exchange(ref _runtimeModelRegistrationReady, 1);
			FlushPendingModelRegistrations();
		}

		private static void FlushPendingModelRegistrations()
		{
			KeyValuePair<string, PendingInjection>[] pendingInjections;
			lock (_pendingInjectionSync)
			{
				pendingInjections = new List<KeyValuePair<string, PendingInjection>>(_pendingInjections).ToArray();
			}

			GD.Print($"[ReForge.Models] FlushPendingModelRegistrations start. pendingCount={pendingInjections.Length}.");

			for (int i = 0; i < pendingInjections.Length; i++)
			{
				KeyValuePair<string, PendingInjection> item = pendingInjections[i];
				if (!TryInjectModelType(item.Value.ModelType, item.Value.LogOwner))
				{
					GD.PrintErr($"[{item.Value.LogOwner}] FlushPendingModelRegistrations injection failed. modelType={item.Value.ModelType.FullName}.");
					continue;
				}

				lock (_pendingInjectionSync)
				{
					_pendingInjections.Remove(item.Key);
				}

				GD.Print($"[{item.Value.LogOwner}] FlushPendingModelRegistrations injected and removed. modelType={item.Value.ModelType.FullName}.");
			}

			lock (_pendingInjectionSync)
			{
				GD.Print($"[ReForge.Models] FlushPendingModelRegistrations completed. remaining={_pendingInjections.Count}.");
			}
		}

		private static bool TryInjectModelType(Type modelType, string owner)
		{
			try
			{
				if (ModelDb.Contains(modelType))
				{
					return true;
				}

				ModelDb.Inject(modelType);
				return ModelDb.Contains(modelType);
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[{owner}] failed to inject model '{modelType.FullName}'. {ex}");
				return false;
			}
		}

		private readonly record struct PendingInjection(Type ModelType, string LogOwner);
	}
}
