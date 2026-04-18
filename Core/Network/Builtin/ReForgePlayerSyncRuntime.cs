#nullable enable

using System;
using Godot;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace ReForgeFramework.Networking;

internal static class ReForgePlayerSyncRuntime
{
	private static readonly object SyncRoot = new();
	private static bool _initialized;
	private static bool _handlerRegistered;
	private static readonly MessageHandlerDelegate<ReForgePlayerSyncMessage> SyncHandler = OnSyncMessage;

	public static void Initialize()
	{
		lock (SyncRoot)
		{
			if (_initialized)
			{
				return;
			}

			_initialized = true;
		}

		TryRegisterNetworkHandler();
	}

	public static SerializablePlayer CaptureSnapshot(Player player)
	{
		ArgumentNullException.ThrowIfNull(player);
		return player.ToSerializable();
	}

	public static void ApplySnapshot(Player player, SerializablePlayer snapshot)
	{
		ArgumentNullException.ThrowIfNull(player);
		player.SyncWithSerializedPlayer(snapshot);
	}

	public static bool SyncSnapshot(Player player)
	{
		ArgumentNullException.ThrowIfNull(player);

		if (!ReForge.Network.IsConnected)
		{
			return false;
		}

		if (ReForge.Network.IsHostAuthority)
		{
			return SyncSnapshotToAll(player);
		}

		return SyncSnapshotToAuthority(player);
	}

	public static bool SyncSnapshotToAll(Player player)
	{
		ArgumentNullException.ThrowIfNull(player);

		if (!ReForge.Network.IsConnected)
		{
			return false;
		}

		ReForge.Network.Send(new ReForgePlayerSyncMessage
		{
			Snapshot = CaptureSnapshot(player)
		});
		return true;
	}

	public static bool SyncSnapshotToAuthority(Player player)
	{
		ArgumentNullException.ThrowIfNull(player);

		if (!ReForge.Network.IsConnected)
		{
			return false;
		}

		ulong hostPeerId = ReForge.Network.HostPeerId;
		if (hostPeerId == 0)
		{
			return false;
		}

		ReForge.Network.SendTo(hostPeerId, new ReForgePlayerSyncMessage
		{
			Snapshot = CaptureSnapshot(player)
		});
		return true;
	}

	public static bool SyncSnapshotToPeer(Player player, ulong peerId)
	{
		ArgumentNullException.ThrowIfNull(player);

		if (!ReForge.Network.IsConnected)
		{
			return false;
		}

		ReForge.Network.SendTo(peerId, new ReForgePlayerSyncMessage
		{
			Snapshot = CaptureSnapshot(player)
		});
		return true;
	}

	private static void TryRegisterNetworkHandler()
	{
		if (_handlerRegistered)
		{
			return;
		}

		try
		{
			ReForge.Network.RegisterHandler(SyncHandler);
			_handlerRegistered = true;
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[ReForge.Player] Failed to register player sync handler. {ex.GetType().Name}: {ex.Message}");
		}
	}

	private static void OnSyncMessage(ReForgePlayerSyncMessage message, ulong senderId)
	{
		try
		{
			RunState? runState = RunManager.Instance?.DebugOnlyGetState();
			if (runState == null)
			{
				return;
			}

			Player? targetPlayer = runState.GetPlayer(message.Snapshot.NetId);
			if (targetPlayer == null)
			{
				GD.PrintErr($"[ReForge.Player] Sync target not found. sender={senderId}, netId={message.Snapshot.NetId}.");
				return;
			}

			targetPlayer.SyncWithSerializedPlayer(message.Snapshot);

			if (ReForge.Network.IsConnected && ReForge.Network.IsHostAuthority && senderId != ReForge.Network.LocalPeerId)
			{
				ReForge.Network.Send(new ReForgePlayerSyncMessage
				{
					Snapshot = message.Snapshot
				});
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[ReForge.Player] Player sync handler failed. {ex.GetType().Name}: {ex.Message}");
		}
	}
}