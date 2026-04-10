#nullable enable

using System;
using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;

namespace ReForgeFramework.Settings.Runtime;

/// <summary>
/// 监听设置相关节点生命周期，在官方设置页重建时自动触发重注入。
/// </summary>
internal static class SettingsLifecycleBridge
{
	private static readonly object Sync = new();
	private static readonly List<WeakReference<ReForgeSettingsApi>> Apis = new();
	private static bool _hooked;
	private static bool _reinjectQueued;

	public static void EnsurePatched(ReForgeSettingsApi api)
	{
		ArgumentNullException.ThrowIfNull(api);

		lock (Sync)
		{
			RegisterApi(api);
			if (_hooked)
			{
				return;
			}

			if (Engine.GetMainLoop() is not SceneTree tree)
			{
				return;
			}

			Error connectResult = tree.Connect(SceneTree.SignalName.NodeAdded, Callable.From<Node>(OnNodeAdded));
			if (connectResult != Error.Ok)
			{
				GD.PrintErr($"[ReForge.Settings] Failed to attach lifecycle watcher. error={connectResult}");
				return;
			}

			_hooked = true;
		}

		GD.Print("[ReForge.Settings] Lifecycle watcher attached.");
	}

	private static void OnNodeAdded(Node node)
	{
		if (node is not NSettingsTabManager)
		{
			return;
		}

		QueueReinject();
	}

	private static void QueueReinject()
	{
		lock (Sync)
		{
			if (_reinjectQueued)
			{
				return;
			}

			_reinjectQueued = true;
		}

		if (Engine.GetMainLoop() is not SceneTree tree)
		{
			return;
		}

		tree.Connect(SceneTree.SignalName.ProcessFrame, Callable.From(() =>
		{
			List<ReForgeSettingsApi> aliveApis = new();
			lock (Sync)
			{
				_reinjectQueued = false;
				for (int i = Apis.Count - 1; i >= 0; i--)
				{
					if (!Apis[i].TryGetTarget(out ReForgeSettingsApi? api) || api == null)
					{
						Apis.RemoveAt(i);
						continue;
					}

					aliveApis.Add(api);
				}
			}

			foreach (ReForgeSettingsApi api in aliveApis)
			{
				api.ReinjectSettings();
			}
		}), (uint)GodotObject.ConnectFlags.OneShot);
	}

	private static void RegisterApi(ReForgeSettingsApi api)
	{
		for (int i = Apis.Count - 1; i >= 0; i--)
		{
			if (!Apis[i].TryGetTarget(out ReForgeSettingsApi? existing) || existing == null)
			{
				Apis.RemoveAt(i);
				continue;
			}

			if (ReferenceEquals(existing, api))
			{
				return;
			}
		}

		Apis.Add(new WeakReference<ReForgeSettingsApi>(api));
	}
}
