#nullable enable

using System;
using System.Collections.Generic;
using Godot;
using ReForgeFramework.UI.SystemAreas;

namespace ReForgeFramework.UI.Runtime;

/// <summary>
/// UI API 生命周期自动补丁：自动识别注入宿主并触发 UI 重注入。
/// </summary>
internal static class UiApiLifecyclePatcher
{
	private static readonly object _syncRoot = new();
	private static readonly List<WeakReference<ReForgeUiFacade>> _facades = new();
	private static readonly HashSet<SystemUiArea> _pendingAreas = new();
	private static readonly Callable _nodeAddedCallable = Callable.From<Node>(OnNodeAdded);
	private static readonly Callable _reinjectCallable = Callable.From(FlushQueuedReinject);

	private static SceneTree? _boundTree;
	private static bool _nodeAddedHooked;
	private static bool _reinjectQueued;

	/// <summary>
	/// 确保生命周期补丁已应用，并注册门面实例以接收后续重注入通知。
	/// </summary>
	/// <param name="facade">门面实例。</param>
	public static void EnsurePatched(ReForgeUiFacade facade)
	{
		UiRuntimeNode.Ensure();

		lock (_syncRoot)
		{
			RegisterFacade(facade);
		}

		EnsureNodeAddedHook();
		QueueReinjectAll();
	}

	private static void EnsureNodeAddedHook()
	{
		if (Engine.GetMainLoop() is not SceneTree tree)
		{
			return;
		}

		lock (_syncRoot)
		{
			if (ReferenceEquals(_boundTree, tree) && _nodeAddedHooked)
			{
				return;
			}

			if (_boundTree != null && GodotObject.IsInstanceValid(_boundTree) && _nodeAddedHooked && _boundTree.IsConnected(SceneTree.SignalName.NodeAdded, _nodeAddedCallable))
			{
				_boundTree.Disconnect(SceneTree.SignalName.NodeAdded, _nodeAddedCallable);
			}

			_boundTree = tree;
			if (!_boundTree.IsConnected(SceneTree.SignalName.NodeAdded, _nodeAddedCallable))
			{
				_boundTree.Connect(SceneTree.SignalName.NodeAdded, _nodeAddedCallable);
			}

			_nodeAddedHooked = true;
			GD.Print("[ReForge.UI] Auto lifecycle watcher attached.");
		}
	}

	private static void OnNodeAdded(Node node)
	{
		if (!UiRuntimeNode.TryDetectAreaHostFromNode(node, out SystemUiArea area))
		{
			return;
		}

		GD.Print($"[ReForge.UI] Detected injection host for area '{area}': {node.Name}.");
		QueueReinject(area);
	}

	private static void QueueReinjectAll()
	{
		QueueReinject(area: null);
	}

	private static void QueueReinject(SystemUiArea? area)
	{
		SceneTree? tree;
		lock (_syncRoot)
		{
			tree = _boundTree;
			if (tree == null || !GodotObject.IsInstanceValid(tree))
			{
				return;
			}

			if (area is { } value)
			{
				_pendingAreas.Add(value);
			}
			else
			{
				_pendingAreas.Clear();
			}

			if (_reinjectQueued)
			{
				return;
			}

			_reinjectQueued = true;
		}

		tree.Connect(SceneTree.SignalName.ProcessFrame, _reinjectCallable, (uint)GodotObject.ConnectFlags.OneShot);
	}

	private static void FlushQueuedReinject()
	{
		List<ReForgeUiFacade> aliveFacades = new();
		SystemUiArea[] pendingAreas;
		lock (_syncRoot)
		{
			_reinjectQueued = false;
			pendingAreas = new SystemUiArea[_pendingAreas.Count];
			_pendingAreas.CopyTo(pendingAreas);
			_pendingAreas.Clear();

			for (int i = _facades.Count - 1; i >= 0; i--)
			{
				if (!_facades[i].TryGetTarget(out ReForgeUiFacade? facade) || facade == null)
				{
					_facades.RemoveAt(i);
					continue;
				}

				aliveFacades.Add(facade);
			}
		}

		foreach (ReForgeUiFacade facade in aliveFacades)
		{
			if (pendingAreas.Length == 0)
			{
				facade.ReinjectSystemAreas();
				continue;
			}

			foreach (SystemUiArea area in pendingAreas)
			{
				facade.ReinjectArea(area);
			}
		}
	}

	private static void RegisterFacade(ReForgeUiFacade facade)
	{
		for (int i = _facades.Count - 1; i >= 0; i--)
		{
			if (!_facades[i].TryGetTarget(out ReForgeUiFacade? existing))
			{
				_facades.RemoveAt(i);
				continue;
			}

			if (ReferenceEquals(existing, facade))
			{
				return;
			}
		}

		_facades.Add(new WeakReference<ReForgeUiFacade>(facade));
	}
}
