#nullable enable

using System;
using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Runs;

internal static class ReForgeRunStartedHookService
{
	private static readonly object SyncRoot = new();
	private static readonly HashSet<Action<RunState>> PendingRunStartedHandlers = new();
	private static bool _processFrameSubscribed;

	public static bool TryHookRunStartedWithRetry(Action<RunState> handler, string owner)
	{
		ArgumentNullException.ThrowIfNull(handler);
		ArgumentException.ThrowIfNullOrWhiteSpace(owner);

		if (TryAttachRunStarted(handler))
		{
			GD.Print($"[{owner}] RunStarted hook attached immediately.");
			return true;
		}

		GD.Print($"[{owner}] RunStarted hook not ready. scheduling ProcessFrame retry.");

		lock (SyncRoot)
		{
			PendingRunStartedHandlers.Add(handler);

			if (_processFrameSubscribed)
			{
				GD.Print($"[{owner}] RunStarted retry already active. handler queued.");
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
		lock (SyncRoot)
		{
			snapshot = new List<Action<RunState>>(PendingRunStartedHandlers);
		}

		if (snapshot.Count > 0)
		{
			GD.Print($"[ReForge.Mods] RunStarted retry tick. pendingHandlers={snapshot.Count}.");
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

			lock (SyncRoot)
			{
				PendingRunStartedHandlers.Remove(handler);
			}

			GD.Print("[ReForge.Mods] RunStarted retry attached one pending handler.");
		}

		TryUnsubscribeProcessFrameIfIdle();
	}

	private static void TryUnsubscribeProcessFrameIfIdle()
	{
		lock (SyncRoot)
		{
			if (PendingRunStartedHandlers.Count > 0 || !_processFrameSubscribed)
			{
				return;
			}

			if (Engine.GetMainLoop() is SceneTree tree)
			{
				tree.ProcessFrame -= OnProcessFrameRetryRunStarted;
			}

			_processFrameSubscribed = false;
			GD.Print("[ReForge.Mods] RunStarted retry unsubscribed (idle).");
		}
	}
}