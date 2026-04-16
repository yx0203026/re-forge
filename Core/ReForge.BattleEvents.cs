#nullable enable

using System;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Runs;
using ReForgeFramework.Api.Events;
using ReForgeFramework.BattleEvents;
using ReForgeFramework.Networking;

public static partial class ReForge
{
	/// <summary>
	/// 战斗事件（及时事件）API。
	/// 
	/// 特性：
	/// 1. Enter / Trigger / Leave 三类生命周期。
	/// 2. 可注册多个事件并按优先级执行。
	/// 3. 权威端显式触发并广播，客户端本地执行。
	/// </summary>
	public static class BattleEvents
	{
		private static readonly object SyncRoot = new();
		private static readonly CombatTimelyEventRegistry Registry = new();
		private static readonly CombatTimelyEventRuntime Runtime = new(Registry);

		private static bool _initialized;
		private static bool _handlerRegistered;

		private static readonly ReForgeMessageHandlerDelegate<ReForgeCombatTimelyEventSyncMessage> SyncHandler = OnSyncMessage;
		private static Func<bool>? _authorityResolver;

		public static bool IsInitialized => _initialized;

		public static int RegisteredEventCount => Registry.Count;

		public static void Initialize(Func<bool>? authorityResolver = null)
		{
			lock (SyncRoot)
			{
				if (_initialized)
				{
					if (authorityResolver != null)
					{
						_authorityResolver = authorityResolver;
					}
					return;
				}

				_authorityResolver = authorityResolver;
				_initialized = true;
			}

			TryRegisterNetworkHooks();
			GD.Print("[ReForge.BattleEvents] initialized.");
		}

		public static CombatTimelyEventRegistrationResult Register(CombatTimelyEventBase @event)
		{
			EnsureInitialized();
			CombatTimelyEventRegistrationResult result = Registry.Register(@event);
			GD.Print($"[ReForge.BattleEvents] event {result.Message}. id='{result.EventId}', replaced={result.Replaced}.");
			return result;
		}

		public static bool Unregister(string eventId)
		{
			EnsureInitialized();
			bool removed = Registry.Unregister(eventId);
			if (removed)
			{
				GD.Print($"[ReForge.BattleEvents] event unregistered. id='{eventId}'.");
			}

			return removed;
		}

		public static bool Trigger(string eventId, IRunState? runState = null, CombatState? combatState = null)
		{
			EnsureInitialized();

			CombatState? resolvedCombat = combatState ?? CombatManager.Instance.DebugOnlyGetState();
			IRunState? resolvedRun = runState ?? resolvedCombat?.RunState;
			return Runtime.TryTrigger(eventId, resolvedRun, resolvedCombat, broadcastIfAuthority: true);
		}

		public static bool IsAuthority()
		{
			if (_authorityResolver != null)
			{
				try
				{
					return _authorityResolver();
				}
				catch (Exception ex)
				{
					GD.PrintErr($"[ReForge.BattleEvents] authority resolver failed. {ex.GetType().Name}: {ex.Message}");
				}
			}

			// 默认 peerId=1 为权威端；若没有连接则本地即权威。
			return !Network.IsConnected || Network.LocalPeerId == 1;
		}

		internal static void OnCombatEnter(IRunState? runState, CombatState? combatState)
		{
			if (!_initialized)
			{
				return;
			}

			Runtime.EnterCombat(runState, combatState);
		}

		internal static void OnCombatLeave(IRunState? runState, CombatState? combatState)
		{
			if (!_initialized)
			{
				return;
			}

			Runtime.LeaveCombat(runState, combatState);
		}

		private static void EnsureInitialized()
		{
			if (_initialized)
			{
				return;
			}

			Initialize();
		}

		private static void TryRegisterNetworkHooks()
		{
			if (_handlerRegistered)
			{
				return;
			}

			try
			{
				Network.RegisterMessage<ReForgeCombatTimelyEventSyncMessage>(ReForgeBuiltinMessageIds.CombatTimelyEventSync);
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[ReForge.BattleEvents] message registration skipped. {ex.GetType().Name}: {ex.Message}");
			}

			Network.RegisterHandler(SyncHandler);
			_handlerRegistered = true;
		}

		private static void OnSyncMessage(ReForgeCombatTimelyEventSyncMessage message, ulong senderId)
		{
			Runtime.OnNetworkSyncReceived(message, senderId);
		}
	}
}
