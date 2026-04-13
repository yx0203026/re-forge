#nullable enable

using System;
using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Runs;

/// <summary>
/// ReForge 模组基类：提供统一初始化流程，尽量减少第三方 ModMain 样板代码。
/// </summary>
public abstract class ReForgeModBase
{
	private static readonly object SyncRoot = new();
	private static readonly Dictionary<Type, BootstrapState> BootstrapStates = new();

	protected abstract string ModId { get; }

	/// <summary>
	/// 是否在初始化期间自动注册 RunStarted 回调（带重试）。
	/// </summary>
	protected virtual bool EnableRunStartedHook => false;

	/// <summary>
	/// Mixin 严格模式，默认关闭以提高第三方模组容错。
	/// </summary>
	protected virtual bool StrictMixinMode => false;

	/// <summary>
	/// 需注册到模型池的模型对。
	/// </summary>
	protected virtual IEnumerable<Action> ModelPoolRegistrations
	{
		get
		{
			yield break;
		}
	}

	/// <summary>
	/// 需注册的卡牌立绘路径对 (modelEntry, resourcePath)。
	/// </summary>
	protected virtual IEnumerable<(string modelEntry, string resourcePath)> CardPortraitRegistrations
	{
		get
		{
			yield break;
		}
	}

	/// <summary>
	/// 子类初始化入口（在 Mixin 注册前执行）。
	/// </summary>
	protected virtual void OnInitialize()
	{
	}

	/// <summary>
	/// 子类 RunStarted 回调。
	/// </summary>
	protected virtual void OnRunStarted(RunState _)
	{
	}

	/// <summary>
	/// 供 ModMain 的静态 Initialize 调用。
	/// </summary>
	protected static void Bootstrap<TMod>() where TMod : ReForgeModBase, new()
	{
		BootstrapState state;
		lock (SyncRoot)
		{
			if (!BootstrapStates.TryGetValue(typeof(TMod), out state!))
			{
				state = new BootstrapState();
				BootstrapStates[typeof(TMod)] = state;
			}

			if (state.Initialized || state.Initializing)
			{
				return;
			}

			state.Initializing = true;
			state.Instance ??= new TMod();
		}

		ReForgeModBase instance = state.Instance!;
		try
		{
			instance.InitializeInternal();

			lock (SyncRoot)
			{
				state.Initialized = true;
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[{instance.ModId}] initialize failed. {ex}");
		}
		finally
		{
			lock (SyncRoot)
			{
				state.Initializing = false;
			}
		}
	}

	private void InitializeInternal()
	{
		if (EnableRunStartedHook)
		{
			ReForge.Mods.TryHookRunStartedWithRetry(HandleRunStarted, ModId);
		}

		OnInitialize();

		foreach (Action registration in ModelPoolRegistrations)
		{
			registration();
		}

		foreach ((string modelEntry, string resourcePath) in CardPortraitRegistrations)
		{
			_ = ReForge.ModelTextures.TryRegisterCardPortraitFromModResource(modelEntry, resourcePath);
		}

		_ = ReForge.Mixins.TryRegister(GetType().Assembly, ModId, strictMode: StrictMixinMode);
		GD.Print($"[{ModId}] initialized.");
	}

	private void HandleRunStarted(RunState run)
	{
		OnRunStarted(run);
	}

	private sealed class BootstrapState
	{
		public bool Initialized;

		public bool Initializing;

		public ReForgeModBase? Instance;
	}
}
