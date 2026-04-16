#nullable enable

using System;
using System.IO;
using System.Text;

namespace ReForgeFramework.Networking;

/// <summary>
/// ReForge 基础包写入器。
/// 当前实现优先稳定可读，后续可替换为位级压缩版本。
/// </summary>
public sealed class ReForgePacketWriter : IDisposable
{
	private readonly MemoryStream _stream;
	private readonly BinaryWriter _writer;

	public ReForgePacketWriter(int initialCapacity = 256)
	{
		_stream = new MemoryStream(initialCapacity);
		_writer = new BinaryWriter(_stream, Encoding.UTF8, leaveOpen: true);
	}

	public int Length => (int)_stream.Length;

	public void Reset()
	{
		_stream.SetLength(0);
		_stream.Position = 0;
	}

	public byte[] ToArray()
	{
		return _stream.ToArray();
	}

	public void WriteBool(bool value) => _writer.Write(value);

	public void WriteByte(byte value) => _writer.Write(value);

	public void WriteInt(int value) => _writer.Write(value);

	public void WriteUInt(uint value) => _writer.Write(value);

	public void WriteLong(long value) => _writer.Write(value);

	public void WriteULong(ulong value) => _writer.Write(value);

	public void WriteFloat(float value) => _writer.Write(value);

	public void WriteDouble(double value) => _writer.Write(value);

	public void WriteString(string value)
	{
		ArgumentNullException.ThrowIfNull(value);
		_writer.Write(value);
	}

	public void WriteBytes(byte[] value, int length)
	{
		ArgumentNullException.ThrowIfNull(value);
		if (length < 0 || length > value.Length)
		{
			throw new ArgumentOutOfRangeException(nameof(length));
		}

		_writer.Write(length);
		_writer.Write(value, 0, length);
	}

	public void Dispose()
	{
		_writer.Dispose();
		_stream.Dispose();
	}
}
