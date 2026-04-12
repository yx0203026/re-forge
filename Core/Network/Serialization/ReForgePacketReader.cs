#nullable enable

using System;
using System.IO;
using System.Text;

namespace ReForgeFramework.Networking;

/// <summary>
/// ReForge 基础包读取器。
/// </summary>
public sealed class ReForgePacketReader : IDisposable
{
	private MemoryStream _stream;
	private BinaryReader _reader;

	public ReForgePacketReader()
	{
		_stream = new MemoryStream(Array.Empty<byte>(), writable: false);
		_reader = new BinaryReader(_stream, Encoding.UTF8, leaveOpen: true);
	}

	public int Position => (int)_stream.Position;

	public int Length => (int)_stream.Length;

	public bool EndOfStream => _stream.Position >= _stream.Length;

	public void Reset(byte[] buffer)
	{
		ArgumentNullException.ThrowIfNull(buffer);

		_reader.Dispose();
		_stream.Dispose();

		_stream = new MemoryStream(buffer, writable: false);
		_reader = new BinaryReader(_stream, Encoding.UTF8, leaveOpen: true);
	}

	public bool ReadBool() => _reader.ReadBoolean();

	public byte ReadByte() => _reader.ReadByte();

	public int ReadInt() => _reader.ReadInt32();

	public uint ReadUInt() => _reader.ReadUInt32();

	public long ReadLong() => _reader.ReadInt64();

	public ulong ReadULong() => _reader.ReadUInt64();

	public float ReadFloat() => _reader.ReadSingle();

	public double ReadDouble() => _reader.ReadDouble();

	public string ReadString() => _reader.ReadString();

	public byte[] ReadBytes()
	{
		int length = _reader.ReadInt32();
		if (length < 0)
		{
			throw new InvalidDataException($"Invalid byte array length: {length}");
		}

		byte[] data = _reader.ReadBytes(length);
		if (data.Length != length)
		{
			throw new EndOfStreamException($"Expected {length} bytes, but only read {data.Length} bytes.");
		}

		return data;
	}

	public void Dispose()
	{
		_reader.Dispose();
		_stream.Dispose();
	}
}
