#nullable enable

using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace ReForgeFramework.Networking;

/// <summary>
/// 玩家快照同步消息：携带官方 SerializablePlayer，收包后直接落地到对应玩家实例。
/// </summary>
internal sealed class ReForgePlayerSyncMessage : INetMessage
{
	public SerializablePlayer Snapshot { get; set; } = new();

	public bool ShouldBroadcast => false;

	public NetTransferMode Mode => NetTransferMode.Reliable;

	public LogLevel LogLevel => LogLevel.Debug;

	public void Serialize(PacketWriter writer)
	{
		writer.Write(Snapshot);
	}

	public void Deserialize(PacketReader reader)
	{
		Snapshot = reader.Read<SerializablePlayer>();
	}
}