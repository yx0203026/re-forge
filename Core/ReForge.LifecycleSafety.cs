#nullable enable

using System;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Runs;
using Sts2Player = MegaCrit.Sts2.Core.Entities.Players.Player;

public static partial class ReForge
{
	/// <summary>
	/// ReForge 内部生命周期安全监管控制器：
	/// - 统一判断 UI/节点/联机运行时是否可安全操作；
	/// - 提供异步等待能力，避免“未准备好就往节点上塞内容”。
	/// </summary>
	internal static class LifecycleSafety
	{
		internal const int DefaultMaxFramesToWait = 600;

		internal static bool IsMainLoopReady()
		{
			if (Engine.GetMainLoop() is not SceneTree tree)
			{
				return false;
			}

			return tree.Root != null;
		}

		internal static bool IsNodeMutationSafe(Node? parent)
		{
			if (!GodotObject.IsInstanceValid(parent) || parent == null)
			{
				return false;
			}

			if (!IsMainLoopReady())
			{
				return false;
			}

			return parent.IsInsideTree();
		}

		internal static bool TryAddChild(Node parent, Node child, out string reason)
		{
			return TryAttachOrReparent(parent, child, keepGlobalTransform: false, out reason);
		}

		internal static bool TryAttachOrReparent(Node parent, Node child, bool keepGlobalTransform, out string reason)
		{
			if (!GodotObject.IsInstanceValid(parent) || !GodotObject.IsInstanceValid(child) || parent == null || child == null)
			{
				reason = "Parent or child is null/invalid.";
				return false;
			}

			if (!IsNodeMutationSafe(parent))
			{
				reason = "Parent is not safe for node mutation yet.";
				return false;
			}

			if (child.GetParent() == parent)
			{
				reason = string.Empty;
				return true;
			}

			try
			{
				if (child.GetParent() == null)
				{
					parent.AddChild(child);
				}
				else
				{
					child.Reparent(parent, keepGlobalTransform);
				}

				reason = string.Empty;
				return true;
			}
			catch (Exception exception)
			{
				reason = $"Node attach/reparent failed. {exception.GetType().Name}: {exception.Message}";
				return false;
			}
		}

		internal static bool TryScheduleOnNextProcessFrame(Node owner, string pendingMetaKey, Action action, out string reason)
		{
			ArgumentNullException.ThrowIfNull(owner);
			ArgumentException.ThrowIfNullOrWhiteSpace(pendingMetaKey);
			ArgumentNullException.ThrowIfNull(action);

			if (!GodotObject.IsInstanceValid(owner))
			{
				reason = "Owner node is invalid.";
				return false;
			}

			if (owner.HasMeta(pendingMetaKey))
			{
				try
				{
					if (owner.GetMeta(pendingMetaKey).AsBool())
					{
						reason = "Retry already pending on owner.";
						return false;
					}
				}
				catch
				{
					// Meta value type mismatch, overwrite below.
				}
			}

			if (Engine.GetMainLoop() is not SceneTree tree)
			{
				reason = "SceneTree is not ready.";
				return false;
			}

			owner.SetMeta(pendingMetaKey, true);

			Error connectResult = tree.Connect(SceneTree.SignalName.ProcessFrame, Callable.From(() =>
			{
				if (GodotObject.IsInstanceValid(owner))
				{
					owner.SetMeta(pendingMetaKey, false);
				}

				if (!GodotObject.IsInstanceValid(owner))
				{
					return;
				}

				action();
			}), (uint)GodotObject.ConnectFlags.OneShot);

			if (connectResult != Error.Ok)
			{
				if (GodotObject.IsInstanceValid(owner))
				{
					owner.SetMeta(pendingMetaKey, false);
				}

				reason = $"Failed to hook ProcessFrame. Error={connectResult}.";
				return false;
			}

			reason = string.Empty;
			return true;
		}

		internal static bool TryGetOverlayStack(out NOverlayStack? stack, out string reason)
		{
			if (!IsMainLoopReady())
			{
				stack = null;
				reason = "SceneTree is not ready.";
				return false;
			}

			stack = NOverlayStack.Instance;
			if (stack == null)
			{
				reason = "NOverlayStack.Instance is null.";
				return false;
			}

			if (!GodotObject.IsInstanceValid(stack))
			{
				reason = "NOverlayStack.Instance is invalid.";
				return false;
			}

			if (!stack.IsInsideTree())
			{
				reason = "NOverlayStack is not inside scene tree yet.";
				return false;
			}

			if (!stack.IsNodeReady())
			{
				reason = "NOverlayStack is not node-ready yet.";
				return false;
			}

			if (stack.GetNodeOrNull<Control>("OverlayBackstop") == null)
			{
				reason = "NOverlayStack.OverlayBackstop is not initialized yet.";
				return false;
			}

			reason = string.Empty;
			return true;
		}

		internal static bool IsOverlayReady()
		{
			return TryGetOverlayStack(out _, out _);
		}

		internal static async Task<bool> WaitForOverlayReadyAsync(int maxFramesToWait = DefaultMaxFramesToWait)
		{
			if (maxFramesToWait < 0)
			{
				throw new ArgumentOutOfRangeException(nameof(maxFramesToWait), maxFramesToWait, "maxFramesToWait must be >= 0.");
			}

			int waited = 0;
			while (!IsOverlayReady() && waited < maxFramesToWait)
			{
				waited++;
				await AwaitNextProcessFrameAsync();
			}

			return IsOverlayReady();
		}

		internal static void EnsureOverlayReady()
		{
			if (!TryGetOverlayStack(out _, out string reason))
			{
				throw new InvalidOperationException($"Overlay runtime is not ready. {reason}");
			}
		}

		internal static bool IsPlayerChoiceReady(Sts2Player player, bool requireOverlayForLocal = true)
		{
			ArgumentNullException.ThrowIfNull(player);
			return TryGetPlayerChoiceReadinessFailure(player, requireOverlayForLocal) == null;
		}

		internal static async Task<bool> WaitForPlayerChoiceReadyAsync(
			Sts2Player player,
			int maxFramesToWait = DefaultMaxFramesToWait,
			bool requireOverlayForLocal = true)
		{
			ArgumentNullException.ThrowIfNull(player);

			if (maxFramesToWait < 0)
			{
				throw new ArgumentOutOfRangeException(nameof(maxFramesToWait), maxFramesToWait, "maxFramesToWait must be >= 0.");
			}

			int waited = 0;
			while (!IsPlayerChoiceReady(player, requireOverlayForLocal) && waited < maxFramesToWait)
			{
				waited++;
				await AwaitNextProcessFrameAsync();
			}

			return IsPlayerChoiceReady(player, requireOverlayForLocal);
		}

		private static Task AwaitNextProcessFrameAsync()
		{
			if (Engine.GetMainLoop() is not SceneTree tree)
			{
				return Task.CompletedTask;
			}

			TaskCompletionSource<bool> completionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
			Error connectResult = tree.Connect(SceneTree.SignalName.ProcessFrame, Callable.From(() =>
			{
				completionSource.TrySetResult(true);
			}), (uint)GodotObject.ConnectFlags.OneShot);

			if (connectResult != Error.Ok)
			{
				return Task.CompletedTask;
			}

			return completionSource.Task;
		}

		internal static void EnsurePlayerChoiceReady(Sts2Player player, bool requireOverlayForLocal = true)
		{
			ArgumentNullException.ThrowIfNull(player);

			string? failure = TryGetPlayerChoiceReadinessFailure(player, requireOverlayForLocal);
			if (failure != null)
			{
				throw new InvalidOperationException(
					"Player choice runtime is not ready. " +
					failure +
					" Use lifecycle-safe waiting helpers before showing selection UI."
				);
			}
		}

		private static string? TryGetPlayerChoiceReadinessFailure(Sts2Player player, bool requireOverlayForLocal)
		{
			if (!IsMainLoopReady())
			{
				return "SceneTree is not ready.";
			}

			if (!LocalContext.NetId.HasValue)
			{
				return "LocalContext.NetId is not assigned yet.";
			}

			RunManager? runManager = RunManager.Instance;
			if (runManager == null)
			{
				return "RunManager.Instance is null.";
			}

			if (runManager.PlayerChoiceSynchronizer == null)
			{
				return "RunManager.PlayerChoiceSynchronizer is null.";
			}

			IRunState? runState = player.RunState;
			if (runState == null)
			{
				return "Player.RunState is null.";
			}

			if (runManager.DebugOnlyGetState() == null)
			{
				return "RunState has not been activated by RunManager yet.";
			}

			NRun? runNode = NRun.Instance;
			if (runNode == null || !GodotObject.IsInstanceValid(runNode) || !runNode.IsInsideTree())
			{
				return "NRun scene is not active yet.";
			}

			if (runState.CurrentRoom == null)
			{
				return "CurrentRoom is not entered yet.";
			}

			if (runManager.NetService == null)
			{
				return "RunManager.NetService is null.";
			}

			if (LocalContext.GetMe(runState) == null)
			{
				return "Local player is not resolved from run state yet.";
			}

			bool isMultiplayer = runManager.NetService.Type.IsMultiplayer();
			if (isMultiplayer && LocalContext.GetMe(runState) == null)
			{
				return "Local player is not resolved for multiplayer run yet.";
			}

			if (!isMultiplayer && !LocalContext.IsMe(player))
			{
				return "Target player is not local in singleplayer context yet.";
			}

			if (requireOverlayForLocal && LocalContext.IsMe(player) && !TryGetOverlayStack(out _, out string overlayReason))
			{
				return "Overlay runtime is not ready for local player UI. " + overlayReason;
			}

			return null;
		}
	}
}
