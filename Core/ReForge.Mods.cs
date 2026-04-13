#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using Godot;
using MegaCrit.Sts2.Core.Runs;

public static partial class ReForge
{
	/// <summary>
	/// ReForge 面向第三方模组的启动辅助入口，目标是减少 ModMain 样板代码。
	/// </summary>
	public static class Mods
	{
		private static readonly object _runStartedSync = new();
		private static readonly HashSet<Action<RunState>> _pendingRunStartedHandlers = new();
		private static bool _processFrameSubscribed;

		/// <summary>
		/// 仅执行一次初始化逻辑。
		/// initState: 0=未初始化, 1=初始化中, 2=已完成。
		/// </summary>
		public static bool TryInitializeOnce(string modId, ref int initState, Action initializeAction)
		{
			ArgumentNullException.ThrowIfNull(initializeAction);

			if (Volatile.Read(ref initState) == 2)
			{
				return false;
			}

			if (Interlocked.CompareExchange(ref initState, 1, 0) != 0)
			{
				return false;
			}

			try
			{
				initializeAction();
				Volatile.Write(ref initState, 2);
				return true;
			}
			catch (Exception ex)
			{
				Interlocked.Exchange(ref initState, 0);
				string owner = string.IsNullOrWhiteSpace(modId) ? "ReForge.Mods" : modId;
				GD.PrintErr($"[{owner}] initialize failed. {ex}");
				return false;
			}
		}

		/// <summary>
		/// 尝试挂载 RunManager.RunStarted；若运行时尚未就绪则自动按帧重试。
		/// </summary>
		public static bool TryHookRunStartedWithRetry(Action<RunState> handler, string? logOwner = null)
		{
			ArgumentNullException.ThrowIfNull(handler);
			if (TryAttachRunStarted(handler))
			{
				return true;
			}

			string owner = string.IsNullOrWhiteSpace(logOwner) ? "ReForge.Mods" : logOwner;

			lock (_runStartedSync)
			{
				_pendingRunStartedHandlers.Add(handler);

				if (_processFrameSubscribed)
				{
					return false;
				}

				if (Engine.GetMainLoop() is not SceneTree tree)
				{
					GD.PrintErr($"[{owner}] failed to subscribe RunStarted retry: SceneTree unavailable.");
					return false;
				}

				tree.ProcessFrame += OnProcessFrameRetryRunStarted;
				_processFrameSubscribed = true;
			}

			return false;
		}

		private static bool TryAttachRunStarted(Action<RunState> handler)
		{
			RunManager? runManager = RunManager.Instance;
			if (runManager == null)
			{
				return false;
			}

			runManager.RunStarted -= handler;
			runManager.RunStarted += handler;
			return true;
		}

		private static void OnProcessFrameRetryRunStarted()
		{
			List<Action<RunState>> snapshot;
			lock (_runStartedSync)
			{
				snapshot = new List<Action<RunState>>(_pendingRunStartedHandlers);
			}

			if (snapshot.Count == 0)
			{
				TryUnsubscribeProcessFrameIfIdle();
				return;
			}

			for (int i = 0; i < snapshot.Count; i++)
			{
				Action<RunState> handler = snapshot[i];
				if (!TryAttachRunStarted(handler))
				{
					continue;
				}

				lock (_runStartedSync)
				{
					_pendingRunStartedHandlers.Remove(handler);
				}
			}

			TryUnsubscribeProcessFrameIfIdle();
		}

		private static void TryUnsubscribeProcessFrameIfIdle()
		{
			lock (_runStartedSync)
			{
				if (_pendingRunStartedHandlers.Count > 0 || !_processFrameSubscribed)
				{
					return;
				}

				if (Engine.GetMainLoop() is SceneTree tree)
				{
					tree.ProcessFrame -= OnProcessFrameRetryRunStarted;
				}

				_processFrameSubscribed = false;
			}
		}
	}
}
