#nullable enable

namespace ReForgeFramework.Networking;

/// <summary>
/// 兼容层：保留历史接口定义，实际由 ReForge.Network 静态入口承载。
/// </summary>
public interface IReForgeNetService
{
	ulong LocalPeerId { get; }

	bool IsConnected { get; }

	void RegisterMessage<T>(byte id) where T : IReForgeNetMessage, new();

	void RegisterMessageHandler<T>(ReForgeMessageHandlerDelegate<T> handler) where T : IReForgeNetMessage;

	void UnregisterMessageHandler<T>(ReForgeMessageHandlerDelegate<T> handler) where T : IReForgeNetMessage;

	void Send<T>(T message) where T : IReForgeNetMessage;

	void SendTo<T>(ulong peerId, T message) where T : IReForgeNetMessage;

	void Update();
}
