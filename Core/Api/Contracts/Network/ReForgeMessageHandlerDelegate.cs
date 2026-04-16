#nullable enable

namespace ReForgeFramework.Networking;

public delegate void ReForgeMessageHandlerDelegate<in T>(T message, ulong senderId) where T : IReForgeNetMessage;
