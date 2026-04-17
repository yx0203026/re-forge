#nullable enable

using MegaCrit.Sts2.Core.Saves.Runs;

namespace ReForgeFramework.Networking;

/// <summary>
/// 玩家快照同步消息：携带官方 SerializablePlayer，收包后直接落地到对应玩家实例。
/// </summary>
internal sealed class ReForgePlayerSyncMessage : IReForgeNetMessage
{
	public SerializablePlayer Snapshot { get; set; } = new();

	public bool ShouldBroadcast => false;

	public ReForgeNetTransferMode Mode => ReForgeNetTransferMode.Reliable;

	public ReForgeNetLogLevel LogLevel => ReForgeNetLogLevel.Debug;

	public void Serialize(ReForgePacketWriter writer)
	{
		writer.Write(Snapshot);
	}

	public void Deserialize(ReForgePacketReader reader)
	{
		Snapshot = reader.Read<SerializablePlayer>();
	}
}