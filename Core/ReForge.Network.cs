#nullable enable

using System;
using Godot;
using ReForgeFramework.Networking;

public static partial class ReForge
{
	/// <summary>
	/// ReForge 网络能力入口。
	/// 架构对齐 STS2 官方：消息契约 + 类型注册 + 消息总线 + 传输抽象。
	/// </summary>
	public static class Network
	{
		private static readonly object SyncRoot = new();
		private static ReForgeNetService? _service;
		private static ReForgeNetworkDiagnosticsRuntime? _diagnostics;
		private static ReForgeModelCatalogHandshakeRuntime? _catalogHandshake;
		private static bool _initialized;
		private static bool _autoPumpAttached;
		private static bool _autoPumpCreated;
		private static Callable _autoPump;

		public static bool IsInitialized => _initialized;

		public static bool IsConnected => Service.IsConnected;

		public static ulong LocalPeerId => Service.LocalPeerId;

		public static ReForgeModelCatalogHashSnapshot LocalModelCatalogHash => _catalogHandshake?.LocalSnapshot ?? new ReForgeModelCatalogHashSnapshot(0, 0, 0);

		public static bool HasModelCatalogMismatch => _catalogHandshake?.HasMismatch ?? false;

		public static string? LastModelCatalogMismatchReason => _catalogHandshake?.LastMismatchReason;

		public static void Initialize(ulong localPeerId = 1)
		{
			lock (SyncRoot)
			{
				if (_initialized)
				{
					return;
				}

				_service = new ReForgeNetService(new ReForgeLoopbackTransport(localPeerId));
				RegisterBuiltInMessages(_service);
				_diagnostics = new ReForgeNetworkDiagnosticsRuntime(_service, GetUtcNowMs);
				_catalogHandshake = new ReForgeModelCatalogHandshakeRuntime(_service, GetUtcNowMs);
				_diagnostics.Initialize();
				_catalogHandshake.Initialize();
				_initialized = true;
			}

			TryAttachAutoPump();
			GD.Print($"[ReForge.Network] initialized with loopback transport. localPeerId={localPeerId}.");
		}

		public static void UseTransport(IReForgeNetTransport transport)
		{
			ArgumentNullException.ThrowIfNull(transport);
			EnsureInitialized();

			lock (SyncRoot)
			{
				Service.SetTransport(transport);
			}

			TryAttachAutoPump();
			GD.Print($"[ReForge.Network] switched transport to '{transport.GetType().FullName}'.");
		}

		public static void UseENetHostSkeleton(ushort port, int maxClients = 4, ulong localPeerId = 1)
		{
			UseTransport(ReForgeENetTransport.CreateHost(port, maxClients, localPeerId));
		}

		public static void UseENetClientSkeleton(string host, ushort port, ulong localPeerId)
		{
			UseTransport(ReForgeENetTransport.CreateClient(host, port, localPeerId));
		}

		public static void UseENetClientSkeleton(
			string host,
			ushort port,
			ulong localPeerId,
			bool autoReconnect,
			int maxReconnectAttempts,
			int reconnectInitialDelayMs = 500,
			int reconnectMaxDelayMs = 8000)
		{
			UseTransport(ReForgeENetTransport.CreateClient(
				host,
				port,
				localPeerId,
				autoReconnect,
				maxReconnectAttempts,
				reconnectInitialDelayMs,
				reconnectMaxDelayMs));
		}

		public static void RegisterMessage<T>(byte id) where T : IReForgeNetMessage, new()
		{
			EnsureInitialized();
			Service.RegisterMessage<T>(id);
		}

		public static void RegisterHandler<T>(ReForgeMessageHandlerDelegate<T> handler) where T : IReForgeNetMessage
		{
			EnsureInitialized();
			Service.RegisterMessageHandler(handler);
		}

		public static void UnregisterHandler<T>(ReForgeMessageHandlerDelegate<T> handler) where T : IReForgeNetMessage
		{
			EnsureInitialized();
			Service.UnregisterMessageHandler(handler);
		}

		public static void Send<T>(T message) where T : IReForgeNetMessage
		{
			EnsureInitialized();
			Service.Send(message);
		}

		public static void SendTo<T>(ulong peerId, T message) where T : IReForgeNetMessage
		{
			EnsureInitialized();
			Service.SendTo(peerId, message);
		}

		public static void Update()
		{
			EnsureInitialized();
			Service.Update();
			_diagnostics?.Update();
			_catalogHandshake?.Update();
		}

		public static bool TryGetPeerNetworkStats(ulong peerId, out ReForgePeerNetworkStats stats)
		{
			EnsureInitialized();
			if (_diagnostics == null)
			{
				stats = new ReForgePeerNetworkStats(peerId, 0, 0, 0, false);
				return false;
			}

			return _diagnostics.TryGetPeerStats(peerId, out stats);
		}

		public static void BroadcastModelCatalogHandshake()
		{
			EnsureInitialized();
			_catalogHandshake?.BroadcastHello();
		}

		public static bool TryGetENetClientReconnectState(
			out bool isReconnecting,
			out int reconnectAttempt,
			out long nextReconnectAtMs,
			out string lastDisconnectReason)
		{
			EnsureInitialized();
			if (Service.Transport is ReForgeENetTransport enet && enet.Config.Role == ReForgeENetTransport.ReForgeENetRole.Client)
			{
				isReconnecting = enet.IsReconnecting;
				reconnectAttempt = enet.ReconnectAttempt;
				nextReconnectAtMs = enet.NextReconnectAtMs;
				lastDisconnectReason = enet.LastDisconnectReason;
				return true;
			}

			isReconnecting = false;
			reconnectAttempt = 0;
			nextReconnectAtMs = 0;
			lastDisconnectReason = string.Empty;
			return false;
		}

		public static bool TryGetENetHostState(
			out ulong[] connectedPeerIds,
			out bool hasLastDisconnect,
			out ulong lastDisconnectedPeerId,
			out string lastDisconnectReason,
			out long lastDisconnectAtMs)
		{
			EnsureInitialized();
			if (Service.Transport is ReForgeENetTransport enet && enet.Config.Role == ReForgeENetTransport.ReForgeENetRole.Host)
			{
				connectedPeerIds = enet.GetHostConnectedPeerIdsSnapshot();
				hasLastDisconnect = enet.TryGetHostLastDisconnect(out lastDisconnectedPeerId, out lastDisconnectReason, out lastDisconnectAtMs);
				return true;
			}

			connectedPeerIds = Array.Empty<ulong>();
			hasLastDisconnect = false;
			lastDisconnectedPeerId = 0;
			lastDisconnectReason = string.Empty;
			lastDisconnectAtMs = 0;
			return false;
		}

		private static ReForgeNetService Service
		{
			get
			{
				EnsureInitialized();
				return _service!;
			}
		}

		private static void EnsureInitialized()
		{
			if (_initialized)
			{
				return;
			}

			Initialize();
		}

		private static void TryAttachAutoPump()
		{
			if (_autoPumpAttached)
			{
				return;
			}

			if (Engine.GetMainLoop() is not SceneTree tree)
			{
				return;
			}

			if (!_autoPumpCreated)
			{
				_autoPump = Callable.From(OnProcessFrame);
				_autoPumpCreated = true;
			}

			tree.Connect(SceneTree.SignalName.ProcessFrame, _autoPump);
			_autoPumpAttached = true;
		}

		private static void OnProcessFrame()
		{
			if (_initialized)
			{
				_service?.Update();
				_diagnostics?.Update();
				_catalogHandshake?.Update();
			}
		}

		private static long GetUtcNowMs()
		{
			return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		}

		private static void RegisterBuiltInMessages(ReForgeNetService service)
		{
			TryRegisterBuiltInMessage<ReForgeNetPingMessage>(service, ReForgeBuiltinMessageIds.Ping);
			TryRegisterBuiltInMessage<ReForgeNetPongMessage>(service, ReForgeBuiltinMessageIds.Pong);
			TryRegisterBuiltInMessage<ReForgeNetHeartbeatMessage>(service, ReForgeBuiltinMessageIds.Heartbeat);
			TryRegisterBuiltInMessage<ReForgeModelCatalogHelloMessage>(service, ReForgeBuiltinMessageIds.ModelCatalogHello);
			TryRegisterBuiltInMessage<ReForgeModelCatalogResultMessage>(service, ReForgeBuiltinMessageIds.ModelCatalogResult);
			TryRegisterBuiltInMessage<ReForgeCombatTimelyEventSyncMessage>(service, ReForgeBuiltinMessageIds.CombatTimelyEventSync);
			TryRegisterBuiltInMessage<ReForgePlayerSyncMessage>(service, ReForgeBuiltinMessageIds.PlayerSync);
			TryRegisterBuiltInMessage<ReForgeRoomSyncMessage>(service, ReForgeBuiltinMessageIds.RoomSync);
		}

		private static void TryRegisterBuiltInMessage<T>(ReForgeNetService service, byte id) where T : IReForgeNetMessage, new()
		{
			try
			{
				service.RegisterMessage<T>(id);
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[ReForge.Network] Failed to register built-in message '{typeof(T).Name}' with id={id}. {ex}");
			}
		}
	}
}
