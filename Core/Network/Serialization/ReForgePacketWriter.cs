#nullable enable

using MegaCrit.Sts2.Core.Multiplayer.Serialization;

namespace ReForgeFramework.Networking;

/// <summary>
/// 兼容层：历史 ReForge 包写入器，内部转发到官方 PacketWriter。
/// </summary>
public sealed class ReForgePacketWriter
{
	private readonly PacketWriter _inner;

	public ReForgePacketWriter(PacketWriter inner)
	{
		_inner = inner;
	}

	public void WriteBool(bool value) => _inner.WriteBool(value);

	public void WriteByte(byte value, int bits = 8) => _inner.WriteByte(value, bits);

	public void WriteBytes(byte[] bytes, int byteCount) => _inner.WriteBytes(bytes, byteCount);

	public void WriteShort(short value, int bits = 16) => _inner.WriteShort(value, bits);

	public void WriteUShort(ushort value, int bits = 16) => _inner.WriteUShort(value, bits);

	public void WriteEnum<T>(T value) where T : struct, System.Enum => _inner.WriteEnum(value);

	public void WriteInt(int value, int bits = 32) => _inner.WriteInt(value, bits);

	public void WriteUInt(uint value, int bits = 32) => _inner.WriteUInt(value, bits);

	public void WriteFloat(float value, QuantizeParams? quantizeParams = null) => _inner.WriteFloat(value, quantizeParams);

	public void WriteLong(long value, int bits = 64) => _inner.WriteLong(value, bits);

	public void WriteULong(ulong value, int bits = 64) => _inner.WriteULong(value, bits);

	public void WriteDouble(double value) => _inner.WriteDouble(value);

	public void WriteString(string value) => _inner.WriteString(value);

	public void Write<T>(T value) where T : IPacketSerializable => _inner.Write(value);
}
