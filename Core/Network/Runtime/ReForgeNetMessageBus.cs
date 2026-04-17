#nullable enable

using System;
using System.Collections.Generic;
using Godot;

namespace ReForgeFramework.Networking;

internal sealed class ReForgeNetMessageBus
{
	private delegate void AnonymousMessageHandler(IReForgeNetMessage message, ulong senderId);

	private readonly struct HandlerPair
	{
		public HandlerPair(AnonymousMessageHandler handler, object originalHandler)
		{
			Handler = handler;
			OriginalHandler = originalHandler;
		}

		public AnonymousMessageHandler Handler { get; }

		public object OriginalHandler { get; }
	}

	private readonly ReForgeNetMessageRegistry _registry;
	private readonly ReForgePacketWriter _writer = new();
	private readonly ReForgePacketReader _reader = new();
	private readonly Dictionary<Type, List<HandlerPair>> _handlers = new();
	private readonly object _syncRoot = new();

	public ReForgeNetMessageBus(ReForgeNetMessageRegistry registry)
	{
		_registry = registry;
	}

	public byte[] SerializeMessage(ulong senderId, IReForgeNetMessage message, out int length)
	{
		ArgumentNullException.ThrowIfNull(message);

		_writer.Reset();
		_writer.WriteByte(_registry.GetMessageId(message));
		_writer.WriteULong(senderId);
		message.Serialize(_writer);
		_writer.ZeroByteRemainder();

		byte[] buffer = _writer.ToArray();
		length = buffer.Length;
		return buffer;
	}

	public bool TryDeserializeMessage(byte[] packetBytes, out IReForgeNetMessage? message, out ulong senderId)
	{
		ArgumentNullException.ThrowIfNull(packetBytes);

		message = null;
		senderId = 0;

		try
		{
			_reader.Reset(packetBytes);
			byte messageId = _reader.ReadByte();
			if (!_registry.TryCreateMessage(messageId, out IReForgeNetMessage? created))
			{
				GD.PrintErr($"[ReForge.Network] Unknown message id: {messageId}.");
				return false;
			}

			if (created == null)
			{
				GD.PrintErr($"[ReForge.Network] Message factory returned null. id={messageId}.");
				return false;
			}

			senderId = _reader.ReadULong();
			created.Deserialize(_reader);
			message = created;
			return true;
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[ReForge.Network] Failed to deserialize packet. {ex}");
			return false;
		}
	}

	public void DispatchMessage(IReForgeNetMessage message, ulong senderId)
	{
		ArgumentNullException.ThrowIfNull(message);

		List<HandlerPair> callbacks;
		lock (_syncRoot)
		{
			if (!_handlers.TryGetValue(message.GetType(), out List<HandlerPair>? registered) || registered.Count == 0)
			{
				GD.Print($"[ReForge.Network] No handler for message type '{message.GetType().FullName}'.");
				return;
			}

			callbacks = new List<HandlerPair>(registered);
		}

		for (int i = 0; i < callbacks.Count; i++)
		{
			HandlerPair pair = callbacks[i];
			try
			{
				pair.Handler(message, senderId);
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[ReForge.Network] Message handler failed. type='{message.GetType().FullName}'. {ex}");
			}
		}
	}

	public void RegisterMessageHandler<T>(ReForgeMessageHandlerDelegate<T> handler) where T : IReForgeNetMessage
	{
		ArgumentNullException.ThrowIfNull(handler);

		if (typeof(T) == typeof(IReForgeNetMessage))
		{
			throw new InvalidOperationException("Handler must target a concrete message type.");
		}

		HandlerPair pair = new(
			(message, senderId) => handler((T)message, senderId),
			handler
		);

		lock (_syncRoot)
		{
			if (!_handlers.TryGetValue(typeof(T), out List<HandlerPair>? list))
			{
				list = new List<HandlerPair>();
				_handlers[typeof(T)] = list;
			}

			list.Add(pair);
		}
	}

	public void UnregisterMessageHandler<T>(ReForgeMessageHandlerDelegate<T> handler) where T : IReForgeNetMessage
	{
		ArgumentNullException.ThrowIfNull(handler);

		if (typeof(T) == typeof(IReForgeNetMessage))
		{
			throw new InvalidOperationException("Handler must target a concrete message type.");
		}

		lock (_syncRoot)
		{
			if (!_handlers.TryGetValue(typeof(T), out List<HandlerPair>? list))
			{
				return;
			}

			list.RemoveAll(pair =>
			{
				if (pair.OriginalHandler is not Delegate original)
				{
					return false;
				}

				return original == (Delegate)handler;
			});
		}
	}
}
