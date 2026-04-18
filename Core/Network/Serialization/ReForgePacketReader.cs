#nullable enable

using MegaCrit.Sts2.Core.Multiplayer.Serialization;

namespace ReForgeFramework.Networking;

/// <summary>
/// 兼容层：历史 ReForge 包读取器，内部转发到官方 PacketReader。
/// </summary>
public sealed class ReForgePacketReader
{
	private readonly PacketReader _inner;

	public ReForgePacketReader(PacketReader inner)
	{
		_inner = inner;
	}

	public bool ReadBool() => _inner.ReadBool();

	public byte ReadByte(int bits = 8) => _inner.ReadByte(bits);

	public void ReadBytes(byte[] destinationBuffer, int byteCount) => _inner.ReadBytes(destinationBuffer, byteCount);

	public short ReadShort(int bits = 16) => _inner.ReadShort(bits);

	public ushort ReadUShort(int bits = 16) => _inner.ReadUShort(bits);

	public T ReadEnum<T>() where T : struct, System.Enum => _inner.ReadEnum<T>();

	public int ReadInt(int bits = 32) => _inner.ReadInt(bits);

	public uint ReadUInt(int bits = 32) => _inner.ReadUInt(bits);

	public float ReadFloat(QuantizeParams? quantizeParams = null) => _inner.ReadFloat(quantizeParams);

	public long ReadLong(int bits = 64) => _inner.ReadLong(bits);

	public ulong ReadULong(int bits = 64) => _inner.ReadULong(bits);

	public double ReadDouble() => _inner.ReadDouble();

	public string ReadString() => _inner.ReadString();

	public T Read<T>() where T : IPacketSerializable, new() => _inner.Read<T>();
}
