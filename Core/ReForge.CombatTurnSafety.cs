#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Runs;
using ReForgeFramework.EventBus;

public static partial class ReForge
{
	public static partial class Combat
	{
		private const string TurnSafeMutationTurnStartBusId = "reforge.combat.turn-safe-mutation.turn-start-before";
		private const string TurnSafeMutationCombatEndBusId = "reforge.combat.turn-safe-mutation.combat-end-after";
		private const string DefaultTurnSafeMutationOwner = "ReForge.Combat";

		private static readonly object TurnSafeMutationSync = new();
		private static readonly ConditionalWeakTable<IRunState, PendingRunMutations> PendingTurnSafeMutations = new();

		private static bool _turnSafeMutationListenersRegistered;

		private sealed class PendingRunMutations
		{
			public List<PendingTurnSafeMutation> Items { get; } = new();
		}

		private sealed record PendingTurnSafeMutation(
			object CombatStateRef,
			Func<CombatState, Task> MutationAsync,
			string LogOwner,
			bool PlayerTurnOnly);

		/// <summary>
		/// 将“会影响战斗状态”的操作入队，并在下一次回合开始时执行。
		/// 默认仅在玩家回合开始时触发，以降低联机时序差异。
		/// </summary>
		public static bool TryScheduleStateMutationForNextTurn(
			IRunState runState,
			CombatState? combatState,
			Func<CombatState, Task> mutationAsync,
			string? logOwner = null,
			bool playerTurnOnly = true)
		{
			ArgumentNullException.ThrowIfNull(runState);
			ArgumentNullException.ThrowIfNull(mutationAsync);

			if (combatState == null)
			{
				GD.PrintErr("[ReForge.Combat] TryScheduleStateMutationForNextTurn ignored: combatState is null.");
				return false;
			}

			string owner = string.IsNullOrWhiteSpace(logOwner) ? DefaultTurnSafeMutationOwner : logOwner;
			EnsureTurnSafeMutationListenersRegistered();

			int pendingCount;
			lock (TurnSafeMutationSync)
			{
				PendingRunMutations bucket = PendingTurnSafeMutations.GetValue(runState, static _ => new PendingRunMutations());
				bucket.Items.Add(new PendingTurnSafeMutation(combatState, mutationAsync, owner, playerTurnOnly));
				pendingCount = bucket.Items.Count;
			}

			GD.Print($"[{owner}] queued combat-state mutation for next turn. playerTurnOnly={playerTurnOnly}, pendingCount={pendingCount}.");
			return true;
		}

		/// <summary>
		/// 同步版本重载：自动包装为异步任务执行。
		/// </summary>
		public static bool TryScheduleStateMutationForNextTurn(
			IRunState runState,
			CombatState? combatState,
			Action<CombatState> mutation,
			string? logOwner = null,
			bool playerTurnOnly = true)
		{
			ArgumentNullException.ThrowIfNull(mutation);
			return TryScheduleStateMutationForNextTurn(
				runState,
				combatState,
				state =>
				{
					mutation(state);
					return Task.CompletedTask;
				},
				logOwner,
				playerTurnOnly);
		}

		private static void EnsureTurnSafeMutationListenersRegistered()
		{
			bool shouldRegister;
			lock (TurnSafeMutationSync)
			{
				shouldRegister = !_turnSafeMutationListenersRegistered;
				if (shouldRegister)
				{
					_turnSafeMutationListenersRegistered = true;
				}
			}

			if (!shouldRegister)
			{
				return;
			}

			ReForge.EventBus.RegisterListener<TurnPhaseEvent>(
				CombatLifecycleEventIds.TurnStartBefore,
				TurnSafeMutationTurnStartBusId,
				OnTurnStartBefore);

			ReForge.EventBus.RegisterListener<CombatEndAfterEvent>(
				CombatLifecycleEventIds.CombatEndAfter,
				TurnSafeMutationCombatEndBusId,
				OnCombatEndAfter);

			GD.Print("[ReForge.Combat] turn-safe mutation listeners registered.");
		}

		private static void OnTurnStartBefore(TurnPhaseEvent evt)
		{
			IRunState runState = evt.CombatState.RunState;
			List<PendingTurnSafeMutation>? toExecute = null;

			lock (TurnSafeMutationSync)
			{
				if (!PendingTurnSafeMutations.TryGetValue(runState, out PendingRunMutations? bucket) || bucket.Items.Count == 0)
				{
					return;
				}

				for (int i = bucket.Items.Count - 1; i >= 0; i--)
				{
					PendingTurnSafeMutation pending = bucket.Items[i];
					if (!ReferenceEquals(pending.CombatStateRef, evt.CombatState))
					{
						continue;
					}

					if (pending.PlayerTurnOnly && evt.Side != CombatSide.Player)
					{
						continue;
					}

					toExecute ??= new List<PendingTurnSafeMutation>();
					toExecute.Add(pending);
					bucket.Items.RemoveAt(i);
				}
			}

			if (toExecute == null)
			{
				return;
			}

			for (int i = 0; i < toExecute.Count; i++)
			{
				PendingTurnSafeMutation pending = toExecute[i];
				TaskHelper.RunSafely(ExecutePendingMutationAsync(pending, evt.CombatState));
			}
		}

		private static async Task ExecutePendingMutationAsync(PendingTurnSafeMutation pending, CombatState combatState)
		{
			try
			{
				await pending.MutationAsync(combatState);
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[{pending.LogOwner}] turn-safe mutation execution failed. {ex}");
			}
		}

		private static void OnCombatEndAfter(CombatEndAfterEvent evt)
		{
			if (evt.CombatState == null)
			{
				return;
			}

			int removed = 0;
			lock (TurnSafeMutationSync)
			{
				if (!PendingTurnSafeMutations.TryGetValue(evt.RunState, out PendingRunMutations? bucket) || bucket.Items.Count == 0)
				{
					return;
				}

				for (int i = bucket.Items.Count - 1; i >= 0; i--)
				{
					if (!ReferenceEquals(bucket.Items[i].CombatStateRef, evt.CombatState))
					{
						continue;
					}

					bucket.Items.RemoveAt(i);
					removed++;
				}
			}

			if (removed > 0)
			{
				GD.Print($"[ReForge.Combat] dropped stale turn-safe mutations after combat end. removed={removed}.");
			}
		}
	}
}
