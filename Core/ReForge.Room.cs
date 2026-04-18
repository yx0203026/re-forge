#nullable enable

using System;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using ReForgeFramework.Networking;

public static partial class ReForge
{
	/// <summary>
	/// 房间/战斗场次相关 API：
	/// 1. 统一封装当前房间类型判断；
	/// 2. 统一封装“本局第几场战斗”统计逻辑；
	/// 3. 供模组侧稳定实现“每 N 场战斗触发一次”的规则。
	/// </summary>
	public static class Room
	{
		private static readonly object SyncRoot = new();
		private static bool _initialized;
		private static bool _handlerRegistered;
		private static readonly MessageHandlerDelegate<ReForgeRoomSyncMessage> SyncHandler = OnSyncMessage;

		public static bool IsInitialized => _initialized;

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

		/// <summary>
		/// 判断给定房间是否属于战斗房（怪物/精英/Boss）。
		/// </summary>
		public static bool IsCombatRoom(AbstractRoom? room)
		{
			if (room == null)
			{
				return false;
			}

			return room.RoomType.IsCombatRoom();
		}

		/// <summary>
		/// 判断当前房间是否是战斗房。
		/// </summary>
		public static bool IsCurrentRoomCombat(IRunState runState)
		{
			ArgumentNullException.ThrowIfNull(runState);
			return IsCombatRoom(runState.CurrentRoom);
		}

		/// <summary>
		/// 获取本局已完成的战斗房数量。
		/// 统计口径：遍历整个 MapPointHistory，按 RoomType 为战斗房计数。
		/// </summary>
		public static int GetCompletedCombatCount(IRunState runState)
		{
			ArgumentNullException.ThrowIfNull(runState);

			int count = 0;
			foreach (var mapPointEntries in runState.MapPointHistory)
			{
				foreach (var mapPointEntry in mapPointEntries)
				{
					count += mapPointEntry.Rooms.Count(static room => room.RoomType.IsCombatRoom());
				}
			}

			return count;
		}

		/// <summary>
		/// 获取当前战斗在本局中的序号（从 1 开始）。
		/// 若当前不在战斗房，则返回 0。
		/// </summary>
		public static int GetCurrentCombatOrdinal(IRunState runState)
		{
			ArgumentNullException.ThrowIfNull(runState);

			if (!IsCurrentRoomCombat(runState))
			{
				return 0;
			}

			int completed = GetCompletedCombatCount(runState);
			return completed + 1;
		}

		/// <summary>
		/// 判断当前战斗序号是否命中固定间隔（例如每 10 场）。
		/// </summary>
		public static bool IsCurrentCombatOnInterval(IRunState runState, int interval)
		{
			ArgumentNullException.ThrowIfNull(runState);

			if (interval <= 0)
			{
				throw new ArgumentOutOfRangeException(nameof(interval), interval, "Interval must be greater than 0.");
			}

			int ordinal = GetCurrentCombatOrdinal(runState);
			if (ordinal <= 0)
			{
				return false;
			}

			return ordinal % interval == 0;
		}

		/// <summary>
		/// 判断当前战斗是否处于指定回合数。
		/// </summary>
		public static bool IsCombatRound(CombatState? combatState, int roundNumber)
		{
			if (combatState == null)
			{
				return false;
			}

			if (roundNumber <= 0)
			{
				throw new ArgumentOutOfRangeException(nameof(roundNumber), roundNumber, "Round number must be greater than 0.");
			}

			return combatState.RoundNumber == roundNumber;
		}

		/// <summary>
		/// 设置当前房间（调试入口）：内部调用 RunManager.EnterRoomDebug。
		/// 联机场景下：
		/// 1. 客户端会先发送给权威端；
		/// 2. 权威端执行后广播到所有客户端。
		/// </summary>
		public static async Task<AbstractRoom?> SetCurrentRoomDebug(
			RoomType roomType,
			MapPointType pointType = MapPointType.Unassigned,
			bool showTransition = true,
			bool syncNetwork = true)
		{
			if (TrySendToAuthority(syncNetwork, new ReForgeRoomSyncMessage
			{
				Operation = ReForgeRoomSyncOperation.EnterRoomDebug,
				RoomType = roomType,
				PointType = pointType,
				ShowTransition = showTransition
			}))
			{
				return null;
			}

			RunManager? runManager = RunManager.Instance;
			if (runManager == null)
			{
				return null;
			}

			AbstractRoom room = await runManager.EnterRoomDebug(
				roomType,
				pointType,
				model: null,
				showTransition: showTransition);

			BroadcastFromAuthorityIfNeeded(syncNetwork, new ReForgeRoomSyncMessage
			{
				Operation = ReForgeRoomSyncOperation.EnterRoomDebug,
				RoomType = roomType,
				PointType = pointType,
				ShowTransition = showTransition
			});

			return room;
		}

		/// <summary>
		/// 跳转到指定地图坐标：内部调用 RunManager.EnterMapCoord。
		/// 联机场景会自动按“客户端请求 -> 主机执行 -> 主机广播”流程同步。
		/// </summary>
		public static async Task<bool> JumpToMapCoord(MapCoord coord, bool syncNetwork = true)
		{
			if (TrySendToAuthority(syncNetwork, new ReForgeRoomSyncMessage
			{
				Operation = ReForgeRoomSyncOperation.EnterMapCoord,
				MapCoordCol = coord.col,
				MapCoordRow = coord.row
			}))
			{
				return true;
			}

			RunManager? runManager = RunManager.Instance;
			if (runManager == null)
			{
				return false;
			}

			await runManager.EnterMapCoord(coord);

			BroadcastFromAuthorityIfNeeded(syncNetwork, new ReForgeRoomSyncMessage
			{
				Operation = ReForgeRoomSyncOperation.EnterMapCoord,
				MapCoordCol = coord.col,
				MapCoordRow = coord.row
			});

			return true;
		}

		/// <summary>
		/// 按地图点类型与层数跳转：内部调用 RunManager.EnterMapPointInternal。
		/// preFinishedRoom 固定为空，供模组侧做标准跳转。
		/// </summary>
		public static async Task<bool> JumpToMapPoint(
			int actFloor,
			MapPointType pointType,
			bool saveGame = true,
			bool syncNetwork = true)
		{
			if (TrySendToAuthority(syncNetwork, new ReForgeRoomSyncMessage
			{
				Operation = ReForgeRoomSyncOperation.EnterMapPoint,
				ActFloor = actFloor,
				PointType = pointType,
				SaveGame = saveGame
			}))
			{
				return true;
			}

			RunManager? runManager = RunManager.Instance;
			if (runManager == null)
			{
				return false;
			}

			await runManager.EnterMapPointInternal(actFloor, pointType, preFinishedRoom: null, saveGame);

			BroadcastFromAuthorityIfNeeded(syncNetwork, new ReForgeRoomSyncMessage
			{
				Operation = ReForgeRoomSyncOperation.EnterMapPoint,
				ActFloor = actFloor,
				PointType = pointType,
				SaveGame = saveGame
			});

			return true;
		}

		/// <summary>
		/// 跳转到指定幕：内部调用 RunManager.EnterAct。
		/// </summary>
		public static async Task<bool> JumpToAct(int actIndex, bool doTransition = true, bool syncNetwork = true)
		{
			if (TrySendToAuthority(syncNetwork, new ReForgeRoomSyncMessage
			{
				Operation = ReForgeRoomSyncOperation.EnterAct,
				ActIndex = actIndex,
				DoTransition = doTransition
			}))
			{
				return true;
			}

			RunManager? runManager = RunManager.Instance;
			if (runManager == null)
			{
				return false;
			}

			await runManager.EnterAct(actIndex, doTransition);

			BroadcastFromAuthorityIfNeeded(syncNetwork, new ReForgeRoomSyncMessage
			{
				Operation = ReForgeRoomSyncOperation.EnterAct,
				ActIndex = actIndex,
				DoTransition = doTransition
			});

			return true;
		}

		private static bool TrySendToAuthority(bool syncNetwork, ReForgeRoomSyncMessage message)
		{
			if (!syncNetwork || !ReForge.Network.IsConnected)
			{
				return false;
			}

			if (ReForge.Network.IsHostAuthority)
			{
				return false;
			}

			ulong hostPeerId = ReForge.Network.HostPeerId;
			if (hostPeerId == 0)
			{
				return false;
			}

			ReForge.Network.SendTo(hostPeerId, message);
			return true;
		}

		private static void BroadcastFromAuthorityIfNeeded(bool syncNetwork, ReForgeRoomSyncMessage message)
		{
			if (!syncNetwork || !ReForge.Network.IsConnected)
			{
				return;
			}

			if (!ReForge.Network.IsHostAuthority)
			{
				return;
			}

			ReForge.Network.Send(message);
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
				Godot.GD.PrintErr($"[ReForge.Room] Failed to register room sync handler. {ex.GetType().Name}: {ex.Message}");
			}
		}

		private static void OnSyncMessage(ReForgeRoomSyncMessage message, ulong senderId)
		{
			if (senderId == ReForge.Network.LocalPeerId)
			{
				return;
			}

			_ = ApplySyncMessageAsync(message, senderId);
		}

		private static async Task ApplySyncMessageAsync(ReForgeRoomSyncMessage message, ulong senderId)
		{
			try
			{
				switch (message.Operation)
				{
					case ReForgeRoomSyncOperation.EnterRoomDebug:
						await SetCurrentRoomDebug(
							message.RoomType,
							message.PointType,
							message.ShowTransition,
							syncNetwork: false);
						break;

					case ReForgeRoomSyncOperation.EnterMapCoord:
						await JumpToMapCoord(
							new MapCoord(message.MapCoordCol, message.MapCoordRow),
							syncNetwork: false);
						break;

					case ReForgeRoomSyncOperation.EnterMapPoint:
						await JumpToMapPoint(
							message.ActFloor,
							message.PointType,
							message.SaveGame,
							syncNetwork: false);
						break;

					case ReForgeRoomSyncOperation.EnterAct:
						await JumpToAct(
							message.ActIndex,
							message.DoTransition,
							syncNetwork: false);
						break;

					default:
						return;
				}

				if (ReForge.Network.IsConnected && ReForge.Network.IsHostAuthority && senderId != ReForge.Network.LocalPeerId)
				{
					ReForge.Network.Send(message);
				}
			}
			catch (Exception ex)
			{
				Godot.GD.PrintErr($"[ReForge.Room] Room sync handler failed. {ex.GetType().Name}: {ex.Message}");
			}
		}
	}
}