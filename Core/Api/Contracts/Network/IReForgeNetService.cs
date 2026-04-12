#nullable enable

namespace ReForgeFramework.Networking;

/// <summary>
/// 网络服务抽象（消息注册/发送/处理）。
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
