#nullable enable

using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.Core.Rooms;

namespace ReForgeFramework.Networking;

/// <summary>
/// 房间操作同步指令类型。
/// </summary>
internal enum ReForgeRoomSyncOperation
{
	EnterRoomDebug = 0,
	EnterMapCoord = 1,
	EnterMapPoint = 2,
	EnterAct = 3
}

/// <summary>
/// 房间设置/跳转同步消息。
/// </summary>
internal sealed class ReForgeRoomSyncMessage : INetMessage
{
	public ReForgeRoomSyncOperation Operation { get; set; }

	public RoomType RoomType { get; set; } = RoomType.Unassigned;

	public MapPointType PointType { get; set; } = MapPointType.Unassigned;

	public bool ShowTransition { get; set; } = true;

	public int MapCoordCol { get; set; }

	public int MapCoordRow { get; set; }

	public int ActFloor { get; set; }

	public bool SaveGame { get; set; } = true;

	public int ActIndex { get; set; }

	public bool DoTransition { get; set; } = true;

	public bool ShouldBroadcast => false;

	public NetTransferMode Mode => NetTransferMode.Reliable;

	public LogLevel LogLevel => LogLevel.Info;

	public void Serialize(PacketWriter writer)
	{
		writer.WriteEnum(Operation);
		writer.WriteEnum(RoomType);
		writer.WriteEnum(PointType);
		writer.WriteBool(ShowTransition);
		writer.WriteInt(MapCoordCol);
		writer.WriteInt(MapCoordRow);
		writer.WriteInt(ActFloor);
		writer.WriteBool(SaveGame);
		writer.WriteInt(ActIndex);
		writer.WriteBool(DoTransition);
	}

	public void Deserialize(PacketReader reader)
	{
		Operation = reader.ReadEnum<ReForgeRoomSyncOperation>();
		RoomType = reader.ReadEnum<RoomType>();
		PointType = reader.ReadEnum<MapPointType>();
		ShowTransition = reader.ReadBool();
		MapCoordCol = reader.ReadInt();
		MapCoordRow = reader.ReadInt();
		ActFloor = reader.ReadInt();
		SaveGame = reader.ReadBool();
		ActIndex = reader.ReadInt();
		DoTransition = reader.ReadBool();
	}
}
