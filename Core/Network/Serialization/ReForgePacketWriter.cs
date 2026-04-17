#nullable enable

using System;

namespace ReForgeFramework.Networking;

/// <summary>
/// ReForge 基础包写入器。
/// 直接继承 STS2 官方 PacketWriter，以便复用官方 IPacketSerializable 协议。
/// </summary>
public sealed class ReForgePacketWriter : MegaCrit.Sts2.Core.Multiplayer.Serialization.PacketWriter
{
	public byte[] ToArray()
	{
		byte[] buffer = new byte[BytePosition];
		Array.Copy(Buffer, buffer, BytePosition);
		return buffer;
	}
}
