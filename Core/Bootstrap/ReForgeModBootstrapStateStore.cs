#nullable enable

using System;
using System.Collections.Generic;

/// <summary>
/// 管理 ReForgeModBase 泛型启动流程的状态，确保每个 Mod 类型独立且线程安全。
/// </summary>
internal static class ReForgeModBootstrapStateStore
{
	private static readonly object SyncRoot = new();
	private static readonly Dictionary<Type, BootstrapState> BootstrapStates = new();

	public static bool TryBegin<TMod>(Func<ReForgeModBase> instanceFactory, out ReForgeModBase? instance)
		where TMod : ReForgeModBase
	{
		Type modType = typeof(TMod);
		lock (SyncRoot)
		{
			BootstrapState state = GetOrCreateState(modType);
			if (state.Initialized || state.Initializing)
			{
				instance = null;
				return false;
			}

			state.Initializing = true;
			state.Instance ??= instanceFactory();
			instance = state.Instance;
			return true;
		}
	}

	public static void MarkInitialized<TMod>() where TMod : ReForgeModBase
	{
		lock (SyncRoot)
		{
			GetOrCreateState(typeof(TMod)).Initialized = true;
		}
	}

	public static void EndAttempt<TMod>() where TMod : ReForgeModBase
	{
		lock (SyncRoot)
		{
			GetOrCreateState(typeof(TMod)).Initializing = false;
		}
	}

	private static BootstrapState GetOrCreateState(Type modType)
	{
		if (!BootstrapStates.TryGetValue(modType, out BootstrapState? state))
		{
			state = new BootstrapState();
			BootstrapStates[modType] = state;
		}

		return state;
	}

	private sealed class BootstrapState
	{
		public bool Initialized;
		public bool Initializing;
		public ReForgeModBase? Instance;
	}
}
