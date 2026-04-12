#nullable enable

using System;
using System.Collections.Generic;

namespace ReForgeFramework.Networking;

/// <summary>
/// 默认内存回环传输实现。
/// 该实现用于本地开发与框架自测，后续可替换为 ENet/Steam 实现。
/// </summary>
internal sealed class ReForgeLoopbackTransport : IReForgeNetTransport
{
	private readonly object _syncRoot = new();
	private readonly Queue<QueuedPacket> _queue = new();
	private bool _disposed;

	private readonly struct QueuedPacket
	{
		public QueuedPacket(ulong senderId, byte[] bytes, ReForgeNetTransferMode mode, int channel)
		{
			SenderId = senderId;
			Bytes = bytes;
			Mode = mode;
			Channel = channel;
		}

		public ulong SenderId { get; }

		public byte[] Bytes { get; }

		public ReForgeNetTransferMode Mode { get; }

		public int Channel { get; }
	}

	public ReForgeLoopbackTransport(ulong localPeerId)
	{
		if (localPeerId == 0)
		{
			throw new ArgumentOutOfRangeException(nameof(localPeerId), "Local peer id must not be 0.");
		}

		LocalPeerId = localPeerId;
	}

	public ulong LocalPeerId { get; }

	public bool IsConnected => !_disposed;

	public event Action<ulong, byte[], ReForgeNetTransferMode, int>? PacketReceived;

	public void SendToPeer(ulong peerId, byte[] packetBytes, int length, ReForgeNetTransferMode mode, int channel)
	{
		ThrowIfDisposed();
		if (peerId != LocalPeerId)
		{
			return;
		}

		Enqueue(LocalPeerId, packetBytes, length, mode, channel);
	}

	public void SendToAll(byte[] packetBytes, int length, ReForgeNetTransferMode mode, int channel, ulong? excludePeerId = null)
	{
		ThrowIfDisposed();
		if (excludePeerId == LocalPeerId)
		{
			return;
		}

		Enqueue(LocalPeerId, packetBytes, length, mode, channel);
	}

	public void InjectRemotePacket(ulong senderId, byte[] packetBytes, int length, ReForgeNetTransferMode mode, int channel)
	{
		ThrowIfDisposed();
		Enqueue(senderId, packetBytes, length, mode, channel);
	}

	public void Update()
	{
		ThrowIfDisposed();

		while (true)
		{
			QueuedPacket packet;
			lock (_syncRoot)
			{
				if (_queue.Count == 0)
				{
					break;
				}

				packet = _queue.Dequeue();
			}

			PacketReceived?.Invoke(packet.SenderId, packet.Bytes, packet.Mode, packet.Channel);
		}
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		lock (_syncRoot)
		{
			_queue.Clear();
		}

		_disposed = true;
	}

	private void Enqueue(ulong senderId, byte[] packetBytes, int length, ReForgeNetTransferMode mode, int channel)
	{
		ArgumentNullException.ThrowIfNull(packetBytes);
		if (length < 0 || length > packetBytes.Length)
		{
			throw new ArgumentOutOfRangeException(nameof(length));
		}

		byte[] copy = new byte[length];
		Array.Copy(packetBytes, copy, length);

		lock (_syncRoot)
		{
			_queue.Enqueue(new QueuedPacket(senderId, copy, mode, channel));
		}
	}

	private void ThrowIfDisposed()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
	}
}
