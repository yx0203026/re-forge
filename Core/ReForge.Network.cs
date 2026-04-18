#nullable enable

using System;
using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Runs;
using ReForgeFramework.Api.Ui;
using ReForgeFramework.Networking;

public static partial class ReForge
{
	/// <summary>
	/// ReForge 网络能力入口。
	/// 该层只做 STS2 官方 NetService 的二次便捷封装，不再维护独立传输协议栈。
	/// </summary>
	public static class Network
	{
		private sealed class HandlerRegistration
		{
			public required Action<INetGameService> RegisterAction { get; init; }

			public required Action<INetGameService> UnregisterAction { get; init; }
		}

		private static readonly object SyncRoot = new();
		private static bool _initialized;
		private static bool _processHookAttached;
		private static bool _processHookCreated;
		private static Callable _processHook;
		private static INetGameService? _cachedService;
		private static readonly Dictionary<(Type MessageType, Delegate Handler), HandlerRegistration> HandlerRegistrations = new();
		private static INetGameService? _registeredService;
		private static readonly HashSet<(Type MessageType, Delegate Handler)> AppliedRegistrationKeys = new();

		public static bool IsInitialized => _initialized;

		public static bool IsConnected
		{
			get
			{
				INetGameService? service = ResolveService();
				return service != null && service.IsConnected && service.Type.IsMultiplayer();
			}
		}

		public static ulong LocalPeerId => ResolveService()?.NetId ?? 0;

		public static bool IsHostAuthority
		{
			get
			{
				INetGameService? service = ResolveService();
				if (service == null)
				{
					return true;
				}

				return service.Type != NetGameType.Client;
			}
		}

		public static ulong HostPeerId
		{
			get
			{
				INetGameService? service = ResolveService();
				if (service == null)
				{
					return 0;
				}

				if (service.Type == NetGameType.Client && service is NetClientGameService client)
				{
					return client.HostNetId;
				}

				return service.NetId;
			}
		}

		public static void Initialize()
		{
			lock (SyncRoot)
			{
				if (_initialized)
				{
					return;
				}

				_initialized = true;
			}

			TryAttachProcessHook();
			GD.Print("[ReForge.Network] initialized over STS2 official NetService wrapper.");
		}

		public static void RegisterMessage<T>(byte id) where T : INetMessage, new()
		{
			Initialize();
			GD.Print($"[ReForge.Network] RegisterMessage<{typeof(T).Name}> ignored. official NetService uses global message type ids.");
		}

		public static void RegisterHandler<T>(MessageHandlerDelegate<T> handler) where T : INetMessage
		{
			ArgumentNullException.ThrowIfNull(handler);

			(Type MessageType, Delegate Handler) key = (typeof(T), handler);
			lock (SyncRoot)
			{
				if (HandlerRegistrations.ContainsKey(key))
				{
					return;
				}

				HandlerRegistrations[key] = new HandlerRegistration
				{
					RegisterAction = service => service.RegisterMessageHandler(handler),
					UnregisterAction = service => service.UnregisterMessageHandler(handler)
				};
			}

			TryApplyRegistrationsNow();
		}

		public static void RegisterHandler<T>(ReForgeMessageHandlerDelegate<T> handler) where T : IReForgeNetMessage
		{
			ArgumentNullException.ThrowIfNull(handler);
			MessageHandlerDelegate<T> officialHandler = (message, senderId) => handler(message, senderId);

			(Type MessageType, Delegate Handler) key = (typeof(T), handler);
			lock (SyncRoot)
			{
				if (HandlerRegistrations.ContainsKey(key))
				{
					return;
				}

				HandlerRegistrations[key] = new HandlerRegistration
				{
					RegisterAction = service => service.RegisterMessageHandler(officialHandler),
					UnregisterAction = service => service.UnregisterMessageHandler(officialHandler)
				};
			}

			TryApplyRegistrationsNow();
		}

		public static void UnregisterHandler<T>(MessageHandlerDelegate<T> handler) where T : INetMessage
		{
			ArgumentNullException.ThrowIfNull(handler);

			(Type MessageType, Delegate Handler) key = (typeof(T), handler);
			HandlerRegistration? registration = null;
			lock (SyncRoot)
			{
				if (HandlerRegistrations.TryGetValue(key, out HandlerRegistration? found))
				{
					registration = found;
					HandlerRegistrations.Remove(key);
				}
			}

			if (registration != null && _registeredService != null)
			{
				registration.UnregisterAction(_registeredService);
			}

			lock (SyncRoot)
			{
				AppliedRegistrationKeys.Remove(key);
			}
		}

		public static void UnregisterHandler<T>(ReForgeMessageHandlerDelegate<T> handler) where T : IReForgeNetMessage
		{
			ArgumentNullException.ThrowIfNull(handler);

			(Type MessageType, Delegate Handler) key = (typeof(T), handler);
			HandlerRegistration? registration = null;
			lock (SyncRoot)
			{
				if (HandlerRegistrations.TryGetValue(key, out HandlerRegistration? found))
				{
					registration = found;
					HandlerRegistrations.Remove(key);
				}
			}

			if (registration != null && _registeredService != null)
			{
				registration.UnregisterAction(_registeredService);
			}

			lock (SyncRoot)
			{
				AppliedRegistrationKeys.Remove(key);
			}
		}

		public static void Send<T>(T message) where T : INetMessage
		{
			ArgumentNullException.ThrowIfNull(message);
			if (!TryResolveConnectedMultiplayerService(out INetGameService service))
			{
				GD.PrintErr($"[ReForge.Network] Send ignored. service unavailable or not connected. message={typeof(T).Name}");
				return;
			}

			service.SendMessage(message);
		}

		public static void SendTo<T>(ulong peerId, T message) where T : INetMessage
		{
			ArgumentNullException.ThrowIfNull(message);
			if (!TryResolveConnectedMultiplayerService(out INetGameService service))
			{
				GD.PrintErr($"[ReForge.Network] SendTo ignored. service unavailable or not connected. message={typeof(T).Name}");
				return;
			}

			if (service.Type == NetGameType.Client)
			{
				if (service is not NetClientGameService client)
				{
					GD.PrintErr($"[ReForge.Network] SendTo ignored. unexpected client service type '{service.GetType().FullName}'.");
					return;
				}

				if (peerId != client.HostNetId)
				{
					GD.PrintErr($"[ReForge.Network] SendTo ignored. client can only send to host id={client.HostNetId}, requested={peerId}.");
					return;
				}

				service.SendMessage(message);
				return;
			}

			service.SendMessage(message, peerId);
		}

		public static void Update()
		{
			Initialize();
			ResolveService();
		}

		private static INetGameService? ResolveService()
		{
			RunManager? runManager = RunManager.Instance;
			if (runManager?.NetService == null)
			{
				_cachedService = null;
				return null;
			}

			if (!ReferenceEquals(_cachedService, runManager.NetService))
			{
				_cachedService = runManager.NetService;
				GD.Print($"[ReForge.Network] Bound to official service '{_cachedService.GetType().Name}', type={_cachedService.Type}, netId={_cachedService.NetId}.");
				TryApplyRegistrationsNow();
			}

			return _cachedService;
		}

		private static bool TryResolveConnectedMultiplayerService(out INetGameService service)
		{
			service = ResolveService()!;
			if (service == null)
			{
				return false;
			}

			if (!service.IsConnected)
			{
				return false;
			}

			if (!service.Type.IsMultiplayer())
			{
				return false;
			}

			return true;
		}

		private static void TryApplyRegistrationsNow()
		{
			INetGameService? service = ResolveService();
			if (service == null)
			{
				return;
			}

			lock (SyncRoot)
			{
				if (!ReferenceEquals(_registeredService, service))
				{
					_registeredService = service;
					AppliedRegistrationKeys.Clear();
				}

				foreach (KeyValuePair<(Type MessageType, Delegate Handler), HandlerRegistration> registration in HandlerRegistrations)
				{
					if (AppliedRegistrationKeys.Contains(registration.Key))
					{
						continue;
					}

					registration.Value.RegisterAction(service);
					AppliedRegistrationKeys.Add(registration.Key);
				}
			}
		}

		private static void TryAttachProcessHook()
		{
			if (_processHookAttached)
			{
				return;
			}

			if (Engine.GetMainLoop() is not SceneTree tree)
			{
				return;
			}

			if (!_processHookCreated)
			{
				_processHook = Callable.From(OnProcessFrame);
				_processHookCreated = true;
			}

			tree.Connect(SceneTree.SignalName.ProcessFrame, _processHook);
			_processHookAttached = true;
		}

		private static void OnProcessFrame()
		{
			if (!_initialized)
			{
				return;
			}

			ResolveService();
			DebuffSelectionPanelBuilder.InitializeNetworkSyncRuntime();
		}
	}
}
