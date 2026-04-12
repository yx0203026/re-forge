#nullable enable

using System;
using System.Collections.Generic;

namespace ReForgeFramework.Networking;

internal sealed class ReForgeNetMessageRegistry
{
	private readonly Dictionary<byte, Func<IReForgeNetMessage>> _idToFactory = new();
	private readonly Dictionary<Type, byte> _typeToId = new();

	public void RegisterMessage<T>(byte id) where T : IReForgeNetMessage, new()
	{
		Type messageType = typeof(T);

		if (_idToFactory.ContainsKey(id))
		{
			throw new InvalidOperationException($"Message id '{id}' is already registered.");
		}

		if (_typeToId.ContainsKey(messageType))
		{
			throw new InvalidOperationException($"Message type '{messageType.FullName}' is already registered.");
		}

		_idToFactory[id] = static () => new T();
		_typeToId[messageType] = id;
	}

	public byte GetMessageId(IReForgeNetMessage message)
	{
		ArgumentNullException.ThrowIfNull(message);

		Type type = message.GetType();
		if (!_typeToId.TryGetValue(type, out byte id))
		{
			throw new InvalidOperationException(
				$"Message type '{type.FullName}' is not registered. Call RegisterMessage<T>(id) first."
			);
		}

		return id;
	}

	public bool TryCreateMessage(byte id, out IReForgeNetMessage? message)
	{
		if (_idToFactory.TryGetValue(id, out Func<IReForgeNetMessage>? factory))
		{
			message = factory();
			return true;
		}

		message = null;
		return false;
	}
}
