#nullable enable

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Migrations;
using ReForgeFramework.EventBus;

namespace ReForgeFramework.EventBus.Patches;

/// <summary>
/// 保存/读取生命周期补丁：在官方入口发布开始/结束事件。
/// </summary>
internal static class SaveManagerLifecyclePatches
{
	private sealed class SaveOperationState
	{
		private int _completedPublished;

		public SaveOperationState(DateTimeOffset startedAt, bool saveProgress, bool isMultiplayer, bool hasPreFinishedRoom)
		{
			StartedAt = startedAt;
			SaveProgress = saveProgress;
			IsMultiplayer = isMultiplayer;
			HasPreFinishedRoom = hasPreFinishedRoom;
		}

		public DateTimeOffset StartedAt { get; }

		public bool SaveProgress { get; }

		public bool IsMultiplayer { get; }

		public bool HasPreFinishedRoom { get; }

		public bool TryMarkCompletedPublished()
		{
			return Interlocked.Exchange(ref _completedPublished, 1) == 0;
		}
	}

	private sealed class LoadOperationState
	{
		private int _completedPublished;

		public LoadOperationState(DateTimeOffset startedAt, bool isMultiplayer)
		{
			StartedAt = startedAt;
			IsMultiplayer = isMultiplayer;
		}

		public DateTimeOffset StartedAt { get; }

		public bool IsMultiplayer { get; }

		public bool TryMarkCompletedPublished()
		{
			return Interlocked.Exchange(ref _completedPublished, 1) == 0;
		}
	}

	[HarmonyPatch(typeof(SaveManager), nameof(SaveManager.SaveRun))]
	private static class SaveRunPatch
	{
		[HarmonyPrefix]
		private static void Prefix(AbstractRoom? preFinishedRoom, bool saveProgress, out SaveOperationState __state)
		{
			bool isMultiplayer = IsCurrentRunMultiplayer();
			bool hasPreFinishedRoom = preFinishedRoom != null;
			DateTimeOffset startedAt = DateTimeOffset.UtcNow;

			__state = new SaveOperationState(startedAt, saveProgress, isMultiplayer, hasPreFinishedRoom);

			SafePublish(
				SaveLoadLifecycleEventIds.SaveStarted,
				new SaveLifecycleStartedEvent(startedAt, saveProgress, isMultiplayer, hasPreFinishedRoom)
			);
		}

		[HarmonyPostfix]
		private static void Postfix(ref Task __result, SaveOperationState __state)
		{
			if (__result == null)
			{
				if (!__state.TryMarkCompletedPublished())
				{
					return;
				}

				DateTimeOffset finishedAt = DateTimeOffset.UtcNow;
				double durationMs = (finishedAt - __state.StartedAt).TotalMilliseconds;
				SafePublish(
					SaveLoadLifecycleEventIds.SaveCompleted,
					new SaveLifecycleCompletedEvent(
						finishedAt,
						__state.SaveProgress,
						__state.IsMultiplayer,
						__state.HasPreFinishedRoom,
						Success: false,
						ErrorMessage: "SaveRun returned null Task.",
						durationMs
					)
				);

				return;
			}

			__result = WrapSaveTask(__result, __state);
		}

		[HarmonyFinalizer]
		private static Exception? Finalizer(Exception? __exception, SaveOperationState __state)
		{
			if (__exception == null || !__state.TryMarkCompletedPublished())
			{
				return __exception;
			}

			DateTimeOffset finishedAt = DateTimeOffset.UtcNow;
			double durationMs = (finishedAt - __state.StartedAt).TotalMilliseconds;

			SafePublish(
				SaveLoadLifecycleEventIds.SaveCompleted,
				new SaveLifecycleCompletedEvent(
					finishedAt,
					__state.SaveProgress,
					__state.IsMultiplayer,
					__state.HasPreFinishedRoom,
					Success: false,
					ErrorMessage: __exception.ToString(),
					durationMs
				)
			);

			return __exception;
		}
	}

	[HarmonyPatch(typeof(SaveManager), nameof(SaveManager.LoadRunSave))]
	private static class LoadRunSavePatch
	{
		[HarmonyPrefix]
		private static void Prefix(out LoadOperationState __state)
		{
			DateTimeOffset startedAt = DateTimeOffset.UtcNow;
			__state = new LoadOperationState(startedAt, isMultiplayer: false);

			SafePublish(
				SaveLoadLifecycleEventIds.LoadStarted,
				new LoadLifecycleStartedEvent(startedAt, IsMultiplayer: false)
			);
		}

		[HarmonyPostfix]
		private static void Postfix(ReadSaveResult<SerializableRun> __result, LoadOperationState __state)
		{
			if (!__state.TryMarkCompletedPublished())
			{
				return;
			}

			DateTimeOffset finishedAt = DateTimeOffset.UtcNow;
			double durationMs = (finishedAt - __state.StartedAt).TotalMilliseconds;

			SafePublish(
				SaveLoadLifecycleEventIds.LoadCompleted,
				new LoadLifecycleCompletedEvent(
					finishedAt,
					__state.IsMultiplayer,
					__result.Success,
					__result.Status.ToString(),
					__result.SaveData != null,
					__result.ErrorMessage,
					durationMs
				)
			);
		}

		[HarmonyFinalizer]
		private static Exception? Finalizer(Exception? __exception, LoadOperationState __state)
		{
			if (__exception == null || !__state.TryMarkCompletedPublished())
			{
				return __exception;
			}

			DateTimeOffset finishedAt = DateTimeOffset.UtcNow;
			double durationMs = (finishedAt - __state.StartedAt).TotalMilliseconds;

			SafePublish(
				SaveLoadLifecycleEventIds.LoadCompleted,
				new LoadLifecycleCompletedEvent(
					finishedAt,
					__state.IsMultiplayer,
					Success: false,
					Status: "Exception",
					HasSaveData: false,
					ErrorMessage: __exception.ToString(),
					durationMs
				)
			);

			return __exception;
		}
	}

	[HarmonyPatch(typeof(SaveManager), nameof(SaveManager.LoadAndCanonicalizeMultiplayerRunSave))]
	private static class LoadMultiplayerRunSavePatch
	{
		[HarmonyPrefix]
		private static void Prefix(out LoadOperationState __state)
		{
			DateTimeOffset startedAt = DateTimeOffset.UtcNow;
			__state = new LoadOperationState(startedAt, isMultiplayer: true);

			SafePublish(
				SaveLoadLifecycleEventIds.LoadStarted,
				new LoadLifecycleStartedEvent(startedAt, IsMultiplayer: true)
			);
		}

		[HarmonyPostfix]
		private static void Postfix(ReadSaveResult<SerializableRun> __result, LoadOperationState __state)
		{
			if (!__state.TryMarkCompletedPublished())
			{
				return;
			}

			DateTimeOffset finishedAt = DateTimeOffset.UtcNow;
			double durationMs = (finishedAt - __state.StartedAt).TotalMilliseconds;

			SafePublish(
				SaveLoadLifecycleEventIds.LoadCompleted,
				new LoadLifecycleCompletedEvent(
					finishedAt,
					__state.IsMultiplayer,
					__result.Success,
					__result.Status.ToString(),
					__result.SaveData != null,
					__result.ErrorMessage,
					durationMs
				)
			);
		}

		[HarmonyFinalizer]
		private static Exception? Finalizer(Exception? __exception, LoadOperationState __state)
		{
			if (__exception == null || !__state.TryMarkCompletedPublished())
			{
				return __exception;
			}

			DateTimeOffset finishedAt = DateTimeOffset.UtcNow;
			double durationMs = (finishedAt - __state.StartedAt).TotalMilliseconds;

			SafePublish(
				SaveLoadLifecycleEventIds.LoadCompleted,
				new LoadLifecycleCompletedEvent(
					finishedAt,
					__state.IsMultiplayer,
					Success: false,
					Status: "Exception",
					HasSaveData: false,
					ErrorMessage: __exception.ToString(),
					durationMs
				)
			);

			return __exception;
		}
	}

	// SaveRun 返回 Task，需包裹其完成态才能准确发布结束事件。
	private static async Task WrapSaveTask(Task saveTask, SaveOperationState state)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		try
		{
			await saveTask;

			if (!state.TryMarkCompletedPublished())
			{
				return;
			}

			DateTimeOffset finishedAt = DateTimeOffset.UtcNow;
			SafePublish(
				SaveLoadLifecycleEventIds.SaveCompleted,
				new SaveLifecycleCompletedEvent(
					finishedAt,
					state.SaveProgress,
					state.IsMultiplayer,
					state.HasPreFinishedRoom,
					Success: true,
					ErrorMessage: null,
					stopwatch.Elapsed.TotalMilliseconds
				)
			);
		}
		catch (Exception exception)
		{
			if (state.TryMarkCompletedPublished())
			{
				DateTimeOffset finishedAt = DateTimeOffset.UtcNow;
				SafePublish(
					SaveLoadLifecycleEventIds.SaveCompleted,
					new SaveLifecycleCompletedEvent(
						finishedAt,
						state.SaveProgress,
						state.IsMultiplayer,
						state.HasPreFinishedRoom,
						Success: false,
						ErrorMessage: exception.ToString(),
						stopwatch.Elapsed.TotalMilliseconds
					)
				);
			}

			throw;
		}
	}

	private static bool IsCurrentRunMultiplayer()
	{
		try
		{
			return RunManager.Instance.NetService.Type.IsMultiplayer();
		}
		catch
		{
			return false;
		}
	}

	private static void SafePublish<TEvent>(string eventId, TEvent eventArg) where TEvent : IEventArg
	{
		try
		{
			global::ReForge.EventBus.Publish(eventId, eventArg);
		}
		catch (Exception exception)
		{
			GD.PrintErr($"[ReForge.EventBus] lifecycle publish failed. eventId='{eventId}'. {exception}");
		}
	}
}
