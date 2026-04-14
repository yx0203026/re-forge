#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.addons.mega_text;

public static partial class ReForge
{
    /// <summary>
    /// 挑战等级扩展：允许通过配置增加挑战等级上限，并提供接口注册新的挑战等级定义和效果。
    /// </summary>
	public enum ExtendedAscensionPersistenceMode
	{
		DualSave = 0,
		ClampOnly = 1
	}

    /// <summary>
    /// 挑战等级扩展核心逻辑：提供配置接口、注册接口，以及存储和加载扩展挑战等级进度的实现。
    /// </summary>
	public sealed class ExtendedAscensionConfig
	{
		public int MaxAscension { get; init; } = 10;

		public ExtendedAscensionPersistenceMode PersistenceMode { get; init; } = ExtendedAscensionPersistenceMode.DualSave;
	}

    /// <summary>
    /// 挑战等级定义：包含挑战等级的基本信息，如标题和描述，用于在界面上展示。
    /// </summary>
	public sealed class ExtendedAscensionLevelDefinition
	{
		public int Level { get; init; }

		public string Title { get; init; } = string.Empty;

		public string Description { get; init; } = string.Empty;
	}

    /// <summary>
    /// 挑战等级系统核心：提供注册新的挑战等级和效果的接口，管理扩展挑战等级的存储和加载，以及在游戏中应用这些效果。
    /// </summary>
	public static class Ascension
	{
		private const int VanillaMaxAscension = 10;
		private const string SingleplayerSaveFileName = "current_run.save";
		private const string MultiplayerSaveFileName = "current_run_mp.save";
		private const string SingleplayerSidecarFileName = "current_run.reforge_ext.save";
		private const string MultiplayerSidecarFileName = "current_run_mp.reforge_ext.save";
		private const string ProgressExtFileName = "reforge_ascension_progress.save";
		private const string ProfileSavesRelativeDir = "saves";

		private static readonly object SyncRoot = new();
		private static readonly Dictionary<int, ExtendedAscensionLevelDefinition> LevelDefinitions = new();
		private static readonly List<(int minAscension, Action<RunState, int> effect)> Effects = new();

		private static int _maxAscension = VanillaMaxAscension;
		private static ExtendedAscensionPersistenceMode _mode = ExtendedAscensionPersistenceMode.DualSave;
		private static bool _runStartedHookRequested;
		private static string? _loadedProgressPath;
		private static ExtendedAscensionProgressData _progress = new();

		public static int MaxAscension
		{
			get
			{
				lock (SyncRoot)
				{
					return Math.Max(_maxAscension, VanillaMaxAscension);
				}
			}
		}

		public static ExtendedAscensionPersistenceMode PersistenceMode
		{
			get
			{
				lock (SyncRoot)
				{
					return _mode;
				}
			}
		}

		public static void Configure(ExtendedAscensionConfig config)
		{
			ArgumentNullException.ThrowIfNull(config);
			lock (SyncRoot)
			{
				_maxAscension = Math.Max(config.MaxAscension, VanillaMaxAscension);
				_mode = config.PersistenceMode;
			}
		}

		public static void SetMaxAscension(int maxAscension)
		{
			lock (SyncRoot)
			{
				_maxAscension = Math.Max(maxAscension, VanillaMaxAscension);
			}
		}

		public static void SetPersistenceMode(ExtendedAscensionPersistenceMode mode)
		{
			lock (SyncRoot)
			{
				_mode = mode;
			}
		}

		public static void RegisterLevel(int level, string title, string description)
		{
			if (level <= VanillaMaxAscension)
			{
				throw new ArgumentOutOfRangeException(nameof(level), "Extended ascension level must be > 10.");
			}

			ArgumentNullException.ThrowIfNull(title);
			ArgumentNullException.ThrowIfNull(description);

			lock (SyncRoot)
			{
				LevelDefinitions[level] = new ExtendedAscensionLevelDefinition
				{
					Level = level,
					Title = title,
					Description = description
				};

				if (_maxAscension < level)
				{
					_maxAscension = level;
				}
			}
		}

		public static bool TryGetLevelDefinition(int level, out ExtendedAscensionLevelDefinition definition)
		{
			lock (SyncRoot)
			{
				if (LevelDefinitions.TryGetValue(level, out ExtendedAscensionLevelDefinition? existing))
				{
					definition = existing;
					return true;
				}
			}

			definition = new ExtendedAscensionLevelDefinition
			{
				Level = level,
				Title = $"Ascension {level}",
				Description = "Custom ascension level registered by mod."
			};
			return false;
		}

		public static void RegisterEffect(int minimumAscension, Action<RunState, int> effect)
		{
			if (minimumAscension <= VanillaMaxAscension)
			{
				throw new ArgumentOutOfRangeException(nameof(minimumAscension), "Extended ascension effect threshold must be > 10.");
			}

			ArgumentNullException.ThrowIfNull(effect);

			lock (SyncRoot)
			{
				Effects.Add((minimumAscension, effect));
				if (_maxAscension < minimumAscension)
				{
					_maxAscension = minimumAscension;
				}
			}

			EnsureRunStartedHook();
		}

		public static int GetMaxAscensionByCharacter(ModelId characterId)
		{
			if (ReForge.IsForceShowAllExtendedAscensionLevelsEnabled())
			{
				return MaxAscension;
			}

			int vanillaMax = SaveManager.Instance.Progress.GetOrCreateCharacterStats(characterId).MaxAscension;
			int extMax = GetStoredCharacterMax(characterId);
			return Math.Max(vanillaMax, extMax);
		}

		public static int GetPreferredAscensionByCharacter(ModelId characterId)
		{
			int vanillaPreferred = SaveManager.Instance.Progress.GetOrCreateCharacterStats(characterId).PreferredAscension;
			int extPreferred = GetStoredCharacterPreferred(characterId);
			int max = GetMaxAscensionByCharacter(characterId);
			int candidate = Math.Max(vanillaPreferred, extPreferred);
			return Math.Clamp(candidate, 0, max);
		}

		public static void SetMaxAscensionByCharacter(ModelId characterId, int maxAscension)
		{
			if (maxAscension <= VanillaMaxAscension)
			{
				return;
			}

			lock (SyncRoot)
			{
				EnsureProgressLoadedLocked();
				CharacterExtendedAscensionProgress character = GetOrCreateCharacterProgressLocked(characterId);
				int clamped = Math.Clamp(maxAscension, VanillaMaxAscension + 1, MaxAscension);
				if (clamped <= character.MaxAscension)
				{
					return;
				}

				character.MaxAscension = clamped;
				if (character.PreferredAscension <= 0)
				{
					character.PreferredAscension = clamped;
				}
				else if (character.PreferredAscension > clamped)
				{
					character.PreferredAscension = clamped;
				}

				SaveProgressLocked();
			}
		}

		public static void SetPreferredAscensionByCharacter(ModelId characterId, int preferredAscension)
		{
			if (preferredAscension <= VanillaMaxAscension)
			{
				return;
			}

			lock (SyncRoot)
			{
				EnsureProgressLoadedLocked();
				CharacterExtendedAscensionProgress character = GetOrCreateCharacterProgressLocked(characterId);
				int max = ReForge.IsForceShowAllExtendedAscensionLevelsEnabled()
					? MaxAscension
					: Math.Max(character.MaxAscension, VanillaMaxAscension);
				if (max < VanillaMaxAscension + 1)
				{
					return;
				}

				int clamped = Math.Clamp(preferredAscension, VanillaMaxAscension + 1, max);
				if (character.PreferredAscension == clamped)
				{
					return;
				}

				character.PreferredAscension = clamped;
				SaveProgressLocked();
			}
		}

		internal static Task WrapSaveTaskWithMirror(Task sourceTask)
		{
			return WrapSaveTaskWithMirrorCore(sourceTask);
		}

		internal static ReadSaveResult<SerializableRun> ResolveSingleplayerLoad(ReadSaveResult<SerializableRun> vanillaResult)
		{
			return ResolveLoadInternal(isMultiplayer: false, vanillaResult, null);
		}

		internal static ReadSaveResult<SerializableRun> ResolveMultiplayerLoad(ReadSaveResult<SerializableRun> vanillaResult, ulong localPlayerId)
		{
			return ResolveLoadInternal(isMultiplayer: true, vanillaResult, localPlayerId);
		}

		internal static void OnRunFinishedAndProgressUpdated(SerializableRun serializableRun, bool victory)
		{
			if (!victory || serializableRun == null)
			{
				return;
			}

			if (serializableRun.GameMode == GameMode.Daily)
			{
				return;
			}

			// A10 胜利后需要解锁到 A11，因此仅在低于 A10 时跳过。
			if (serializableRun.Ascension < VanillaMaxAscension)
			{
				return;
			}

			if (serializableRun.Players == null || serializableRun.Players.Count != 1)
			{
				return;
			}

			ModelId? characterId = serializableRun.Players[0].CharacterId;
			if (characterId == null || characterId == ModelId.none)
			{
				return;
			}

			AdvanceCharacterProgressAfterWin(characterId, serializableRun.Ascension);
		}

		internal static void ApplySingleplayerLobbyAscension(StartRunLobby lobby, ModelId characterId)
		{
			if (lobby.NetService.Type.IsMultiplayer())
			{
				return;
			}

			if (characterId == ModelDb.GetId<RandomCharacter>())
			{
				int randomMax = GetRandomCharacterMaxAscensionIncludingExtended();
				if (randomMax > lobby.MaxAscension)
				{
					SetLobbyMaxAscension(lobby, randomMax);
					lobby.SyncAscensionChange(Math.Min(lobby.Ascension, randomMax));
					lobby.LobbyListener.MaxAscensionChanged();
				}
				return;
			}

			if (lobby.MaxAscension <= 0)
			{
				return;
			}

			int extendedMax = GetMaxAscensionByCharacter(characterId);
			if (extendedMax <= lobby.MaxAscension)
			{
				return;
			}

			SetLobbyMaxAscension(lobby, extendedMax);
			int preferred = GetPreferredAscensionByCharacter(characterId);
			int target = Math.Clamp(preferred, 0, extendedMax);
			lobby.SyncAscensionChange(target);
			lobby.LobbyListener.MaxAscensionChanged();
		}

		internal static void OnLobbyPreferredAscensionUpdated(StartRunLobby lobby)
		{
			if (lobby.GameMode == GameMode.Daily)
			{
				return;
			}

			if (lobby.NetService.Type != NetGameType.Singleplayer)
			{
				return;
			}

			if (lobby.Players.Count == 0)
			{
				return;
			}

			ModelId characterId = lobby.LocalPlayer.character.Id;
			if (lobby.Ascension > VanillaMaxAscension)
			{
				SetPreferredAscensionByCharacter(characterId, lobby.Ascension);
			}
		}

		internal static int GetMultiplayerUnlockedMaxAscension()
		{
			int configured = MaxAscension;
			try
			{
				ProgressState progress = SaveManager.Instance.Progress;
				int mergedMax = Math.Max(progress.MaxMultiplayerAscension, configured);
				if (progress.MaxMultiplayerAscension != mergedMax)
				{
					progress.MaxMultiplayerAscension = mergedMax;
				}

				if (progress.PreferredMultiplayerAscension > mergedMax)
				{
					progress.PreferredMultiplayerAscension = mergedMax;
				}

				return mergedMax;
			}
			catch
			{
				return configured;
			}
		}

		internal static int NormalizeMultiplayerUnlockedMaxAscension(int unlocked)
		{
			int floor = Math.Max(VanillaMaxAscension, unlocked);
			return Math.Max(floor, GetMultiplayerUnlockedMaxAscension());
		}

		internal static void NormalizeLobbyJoinRequest(ref ClientLobbyJoinRequestMessage message)
		{
			message.maxAscensionUnlocked = NormalizeMultiplayerUnlockedMaxAscension(message.maxAscensionUnlocked);
		}

		private static void EnsureRunStartedHook()
		{
			lock (SyncRoot)
			{
				if (_runStartedHookRequested)
				{
					return;
				}
				_runStartedHookRequested = true;
			}

			_ = ReForge.Mods.TryHookRunStartedWithRetry(OnRunStarted, "ReForge.Ascension");
		}

		private static void OnRunStarted(RunState runState)
		{
			List<(int minAscension, Action<RunState, int> effect)> snapshot;
			lock (SyncRoot)
			{
				snapshot = new List<(int minAscension, Action<RunState, int> effect)>(Effects);
			}

			int currentAscension = runState.AscensionLevel;
			for (int i = 0; i < snapshot.Count; i++)
			{
				(int minAscension, Action<RunState, int> effect) item = snapshot[i];
				if (currentAscension < item.minAscension)
				{
					continue;
				}

				try
				{
					item.effect(runState, currentAscension);
				}
				catch (Exception ex)
				{
					GD.PrintErr($"[ReForge.Ascension] effect execution failed. min={item.minAscension}, current={currentAscension}. {ex}");
				}
			}
		}

		private static async Task WrapSaveTaskWithMirrorCore(Task sourceTask)
		{
			await sourceTask;

			try
			{
				if (PersistenceMode != ExtendedAscensionPersistenceMode.DualSave)
				{
					return;
				}

				bool isMultiplayer = IsCurrentRunMultiplayer();
				string vanillaPath = GetRunSavePath(isMultiplayer, sidecar: false);
				string sidecarPath = GetRunSavePath(isMultiplayer, sidecar: true);
				if (!File.Exists(vanillaPath))
				{
					return;
				}

				string json = File.ReadAllText(vanillaPath);
				ReadSaveResult<SerializableRun> parsed = SaveManager.FromJson<SerializableRun>(json);
				if (!parsed.Success || parsed.SaveData == null)
				{
					GD.PrintErr($"[ReForge.Ascension] failed to parse run save while mirroring. path='{vanillaPath}', status={parsed.Status}, err={parsed.ErrorMessage}");
					return;
				}

				if (parsed.SaveData.Ascension <= VanillaMaxAscension)
				{
					DeleteIfExists(sidecarPath);
					return;
				}

				WriteAllText(sidecarPath, json);

				SerializableRun clamped = parsed.SaveData;
				clamped.Ascension = VanillaMaxAscension;
				string clampedJson = SaveManager.ToJson(clamped);
				WriteAllText(vanillaPath, clampedJson);
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[ReForge.Ascension] dual-save mirror failed. {ex}");
			}
		}

		private static ReadSaveResult<SerializableRun> ResolveLoadInternal(bool isMultiplayer, ReadSaveResult<SerializableRun> vanillaResult, ulong? localPlayerId)
		{
			if (PersistenceMode == ExtendedAscensionPersistenceMode.DualSave)
			{
				try
				{
					string sidecarPath = GetRunSavePath(isMultiplayer, sidecar: true);
					if (File.Exists(sidecarPath))
					{
						string sidecarJson = File.ReadAllText(sidecarPath);
						ReadSaveResult<SerializableRun> sidecarResult = SaveManager.FromJson<SerializableRun>(sidecarJson);
						if (sidecarResult.Success && sidecarResult.SaveData != null)
						{
							if (isMultiplayer && localPlayerId.HasValue)
							{
								SerializableRun canonicalized = RunManager.CanonicalizeSave(sidecarResult.SaveData, localPlayerId.Value);
								return new ReadSaveResult<SerializableRun>(canonicalized, sidecarResult.Status, sidecarResult.ErrorMessage);
							}

							return sidecarResult;
						}

						GD.PrintErr($"[ReForge.Ascension] sidecar parse failed, fallback to vanilla. path='{sidecarPath}', status={sidecarResult.Status}, err={sidecarResult.ErrorMessage}");
					}
				}
				catch (Exception ex)
				{
					GD.PrintErr($"[ReForge.Ascension] sidecar load exception, fallback to vanilla. {ex}");
				}
			}

			return ClampResultIfNeeded(vanillaResult);
		}

		private static ReadSaveResult<SerializableRun> ClampResultIfNeeded(ReadSaveResult<SerializableRun> result)
		{
			if (!result.Success || result.SaveData == null)
			{
				return result;
			}

			if (result.SaveData.Ascension <= VanillaMaxAscension)
			{
				return result;
			}

			SerializableRun clamped = result.SaveData;
			clamped.Ascension = VanillaMaxAscension;
			return new ReadSaveResult<SerializableRun>(
				clamped,
				result.Status,
				AppendMessage(result.ErrorMessage, "Extended ascension save was clamped to vanilla A10.")
			);
		}

		private static int GetStoredCharacterMax(ModelId characterId)
		{
			lock (SyncRoot)
			{
				EnsureProgressLoadedLocked();
				return TryGetCharacterProgressLocked(characterId, out CharacterExtendedAscensionProgress? progress) ? progress!.MaxAscension : 0;
			}
		}

		private static int GetStoredCharacterPreferred(ModelId characterId)
		{
			lock (SyncRoot)
			{
				EnsureProgressLoadedLocked();
				return TryGetCharacterProgressLocked(characterId, out CharacterExtendedAscensionProgress? progress) ? progress!.PreferredAscension : 0;
			}
		}

		private static void AdvanceCharacterProgressAfterWin(ModelId characterId, int runAscension)
		{
			// A10 胜利需要继续推进到 A11，因此仅在低于 A10 时跳过。
			if (runAscension < VanillaMaxAscension)
			{
				return;
			}

			lock (SyncRoot)
			{
				EnsureProgressLoadedLocked();
				CharacterExtendedAscensionProgress progress = GetOrCreateCharacterProgressLocked(characterId);
				int currentMax = Math.Max(progress.MaxAscension, VanillaMaxAscension);
				if (runAscension > currentMax)
				{
					currentMax = Math.Min(runAscension, MaxAscension);
				}

				if (runAscension == currentMax && currentMax < MaxAscension)
				{
					currentMax++;
				}

				if (currentMax <= progress.MaxAscension && progress.PreferredAscension == currentMax)
				{
					return;
				}

				progress.MaxAscension = currentMax;
				progress.PreferredAscension = currentMax;
				SaveProgressLocked();
			}
		}

		private static int GetRandomCharacterMaxAscensionIncludingExtended()
		{
			int max = 0;
			foreach (CharacterModel character in ModelDb.AllCharacters)
			{
				max = Math.Max(max, GetMaxAscensionByCharacter(character.Id));
			}

			lock (SyncRoot)
			{
				EnsureProgressLoadedLocked();
				foreach (CharacterExtendedAscensionProgress value in _progress.ByCharacter.Values)
				{
					max = Math.Max(max, value.MaxAscension);
				}
			}

			return max;
		}

		private static void SetLobbyMaxAscension(StartRunLobby lobby, int maxAscension)
		{
			Traverse.Create(lobby).Property(nameof(StartRunLobby.MaxAscension)).SetValue(maxAscension);
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

		private static string GetRunSavePath(bool isMultiplayer, bool sidecar)
		{
			string fileName;
			if (!sidecar)
			{
				fileName = isMultiplayer ? MultiplayerSaveFileName : SingleplayerSaveFileName;
			}
			else
			{
				fileName = isMultiplayer ? MultiplayerSidecarFileName : SingleplayerSidecarFileName;
			}

			// GetProfileScopedPath 需要 profile 相对路径，不能传 user:// 绝对样式路径。
			string relativePath = Path.Combine(ProfileSavesRelativeDir, fileName);
			return NormalizeFsPath(SaveManager.Instance.GetProfileScopedPath(relativePath));
		}

		private static string GetProgressSavePath()
		{
			string relativePath = Path.Combine(ProfileSavesRelativeDir, ProgressExtFileName);
			return NormalizeFsPath(SaveManager.Instance.GetProfileScopedPath(relativePath));
		}

		private static void EnsureProgressLoadedLocked()
		{
			string path = GetProgressSavePath();
			if (string.Equals(_loadedProgressPath, path, StringComparison.OrdinalIgnoreCase))
			{
				return;
			}

			_loadedProgressPath = path;
			_progress = new ExtendedAscensionProgressData();

			try
			{
				if (!File.Exists(path))
				{
					return;
				}

				string json = File.ReadAllText(path);
				ExtendedAscensionProgressData? parsed = JsonSerializer.Deserialize<ExtendedAscensionProgressData>(json);
				if (parsed != null)
				{
					_progress = parsed;
				}
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[ReForge.Ascension] failed to load extended progress save. path='{path}'. {ex}");
				_progress = new ExtendedAscensionProgressData();
			}
		}

		private static void SaveProgressLocked()
		{
			if (string.IsNullOrWhiteSpace(_loadedProgressPath))
			{
				_loadedProgressPath = GetProgressSavePath();
			}

			try
			{
				string path = NormalizeFsPath(_loadedProgressPath);
				string? dir = Path.GetDirectoryName(path);
				if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
				{
					Directory.CreateDirectory(dir);
				}

				string json = JsonSerializer.Serialize(_progress);
				File.WriteAllText(path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[ReForge.Ascension] failed to save extended progress. {ex}");
			}
		}

		private static bool TryGetCharacterProgressLocked(ModelId characterId, out CharacterExtendedAscensionProgress? progress)
		{
			string key = ToCharacterKey(characterId);
			if (!string.IsNullOrWhiteSpace(key) && _progress.ByCharacter.TryGetValue(key, out CharacterExtendedAscensionProgress? value))
			{
				progress = value;
				return true;
			}

			progress = null;
			return false;
		}

		private static CharacterExtendedAscensionProgress GetOrCreateCharacterProgressLocked(ModelId characterId)
		{
			string key = ToCharacterKey(characterId);
			if (string.IsNullOrWhiteSpace(key))
			{
				throw new ArgumentException("Character id key is empty.", nameof(characterId));
			}

			if (_progress.ByCharacter.TryGetValue(key, out CharacterExtendedAscensionProgress? existing))
			{
				return existing;
			}

			CharacterExtendedAscensionProgress created = new();
			_progress.ByCharacter[key] = created;
			return created;
		}

		private static string ToCharacterKey(ModelId characterId)
		{
			return characterId.Entry?.Trim().ToLowerInvariant() ?? string.Empty;
		}

		private static string AppendMessage(string? existing, string appended)
		{
			if (string.IsNullOrWhiteSpace(existing))
			{
				return appended;
			}

			return existing + " | " + appended;
		}

		private static void DeleteIfExists(string path)
		{
			try
			{
				if (File.Exists(path))
				{
					File.Delete(path);
				}
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[ReForge.Ascension] failed to delete sidecar '{path}'. {ex}");
			}
		}

		private static void WriteAllText(string path, string content)
		{
			path = NormalizeFsPath(path);
			string? dir = Path.GetDirectoryName(path);
			if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
			{
				Directory.CreateDirectory(dir);
			}

			File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		}

		private static string NormalizeFsPath(string path)
		{
			if (string.IsNullOrWhiteSpace(path))
			{
				return path;
			}

			return path.Contains("://", StringComparison.Ordinal) ? ProjectSettings.GlobalizePath(path) : path;
		}
	}
}

public sealed class CharacterExtendedAscensionProgress
{
	public int MaxAscension { get; set; }

	public int PreferredAscension { get; set; }
}

public sealed class ExtendedAscensionProgressData
{
	public Dictionary<string, CharacterExtendedAscensionProgress> ByCharacter { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

[HarmonyPatch(typeof(SaveManager), nameof(SaveManager.SaveRun))]
internal static class ReForgeExtendedAscensionSavePatch
{
	[HarmonyPostfix]
	private static void Postfix(ref Task __result)
	{
		if (__result == null)
		{
			return;
		}

		__result = ReForge.Ascension.WrapSaveTaskWithMirror(__result);
	}
}

[HarmonyPatch(typeof(SaveManager), nameof(SaveManager.LoadRunSave))]
internal static class ReForgeExtendedAscensionLoadPatch
{
	[HarmonyPostfix]
	private static void Postfix(ref ReadSaveResult<SerializableRun> __result)
	{
		__result = ReForge.Ascension.ResolveSingleplayerLoad(__result);
	}
}

[HarmonyPatch(typeof(SaveManager), nameof(SaveManager.LoadAndCanonicalizeMultiplayerRunSave))]
internal static class ReForgeExtendedAscensionMultiplayerLoadPatch
{
	[HarmonyPostfix]
	private static void Postfix(ulong localPlayerId, ref ReadSaveResult<SerializableRun> __result)
	{
		__result = ReForge.Ascension.ResolveMultiplayerLoad(__result, localPlayerId);
	}
}

[HarmonyPatch(typeof(SaveManager), nameof(SaveManager.UpdateProgressWithRunData))]
internal static class ReForgeExtendedAscensionProgressPatch
{
	[HarmonyPostfix]
	private static void Postfix(SerializableRun serializableRun, bool victory)
	{
		ReForge.Ascension.OnRunFinishedAndProgressUpdated(serializableRun, victory);
	}
}

[HarmonyPatch(typeof(StartRunLobby), "SetSingleplayerAscensionAfterCharacterChanged")]
internal static class ReForgeExtendedAscensionLobbyCharacterPatch
{
	[HarmonyPostfix]
	private static void Postfix(StartRunLobby __instance, ModelId characterId)
	{
		ReForge.Ascension.ApplySingleplayerLobbyAscension(__instance, characterId);
	}
}

[HarmonyPatch(typeof(StartRunLobby), "UpdatePreferredAscension")]
internal static class ReForgeExtendedAscensionLobbyPreferredPatch
{
	[HarmonyPostfix]
	private static void Postfix(StartRunLobby __instance)
	{
		ReForge.Ascension.OnLobbyPreferredAscensionUpdated(__instance);
	}
}

[HarmonyPatch(typeof(ClientLobbyJoinRequestMessage), nameof(ClientLobbyJoinRequestMessage.Serialize))]
internal static class ReForgeExtendedAscensionLobbyJoinRequestPatch
{
	[HarmonyPrefix]
	private static void Prefix(ref ClientLobbyJoinRequestMessage __instance)
	{
		ReForge.Ascension.NormalizeLobbyJoinRequest(ref __instance);
	}
}

[HarmonyPatch(typeof(StartRunLobby), "TryAddPlayerInFirstAvailableSlot")]
internal static class ReForgeExtendedAscensionLobbyPlayerAddPatch
{
	[HarmonyPrefix]
	private static void Prefix(ref int maxAscensionUnlocked)
	{
		maxAscensionUnlocked = ReForge.Ascension.NormalizeMultiplayerUnlockedMaxAscension(maxAscensionUnlocked);
	}
}

[HarmonyPatch(typeof(StartRunLobby), "BeginRunLocally")]
internal static class ReForgeExtendedAscensionBeginRunLocallyPatch
{
	private readonly struct RestoreState
	{
		public RestoreState(CharacterStats? stats, int originalMax, int originalPreferred, bool shouldRestore)
		{
			Stats = stats;
			OriginalMax = originalMax;
			OriginalPreferred = originalPreferred;
			ShouldRestore = shouldRestore;
		}

		public CharacterStats? Stats { get; }

		public int OriginalMax { get; }

		public int OriginalPreferred { get; }

		public bool ShouldRestore { get; }
	}

	[HarmonyPrefix]
	private static void Prefix(StartRunLobby __instance, ref RestoreState __state)
	{
		__state = default;
		if (__instance.NetService.Type != NetGameType.Singleplayer)
		{
			return;
		}

		if (__instance.Players.Count == 0 || __instance.Ascension <= 10)
		{
			return;
		}

		ModelId characterId = __instance.Players[0].character.Id;
		CharacterStats stats = SaveManager.Instance.Progress.GetOrCreateCharacterStats(characterId);
		int originalMax = stats.MaxAscension;
		int originalPreferred = stats.PreferredAscension;

		int extendedMax = ReForge.Ascension.GetMaxAscensionByCharacter(characterId);
		int targetMax = Math.Max(Math.Max(originalMax, extendedMax), __instance.Ascension);
		stats.MaxAscension = targetMax;
		stats.PreferredAscension = Math.Max(originalPreferred, __instance.Ascension);

		__state = new RestoreState(stats, originalMax, originalPreferred, shouldRestore: true);
	}

	[HarmonyPostfix]
	private static void Postfix(ref RestoreState __state)
	{
		if (!__state.ShouldRestore || __state.Stats == null)
		{
			return;
		}

		__state.Stats.MaxAscension = __state.OriginalMax;
		__state.Stats.PreferredAscension = __state.OriginalPreferred;
	}
}

[HarmonyPatch(typeof(NAscensionPanel), "RefreshAscensionText")]
internal static class ReForgeAscensionPanelTextPatch
{
	[HarmonyPrefix]
	private static bool Prefix(NAscensionPanel __instance)
	{
		int level = __instance.Ascension;
		if (level <= 10)
		{
			return true;
		}

		_ = ReForge.Ascension.TryGetLevelDefinition(level, out ReForge.ExtendedAscensionLevelDefinition definition);
		MegaLabel? levelLabel = Traverse.Create(__instance).Field<MegaLabel>("_ascensionLevel").Value;
		MegaRichTextLabel? infoLabel = Traverse.Create(__instance).Field<MegaRichTextLabel>("_info").Value;
		if (levelLabel == null || infoLabel == null)
		{
			return false;
		}

		levelLabel.SetTextAutoSize(level.ToString());
		infoLabel.Text = "[b][gold]" + definition.Title + "[/gold][/b]\n" + definition.Description;
		return false;
	}
}

[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Helpers.AscensionHelper), nameof(MegaCrit.Sts2.Core.Helpers.AscensionHelper.GetHoverTip))]
internal static class ReForgeAscensionHoverTipPatch
{
	[HarmonyPrefix]
	private static bool Prefix(CharacterModel character, int level, bool achievementsLocked, ref HoverTip __result)
	{
		if (level <= 10)
		{
			return true;
		}

		List<string> ascensions = new();
		for (int i = 1; i <= level; i++)
		{
			if (i <= 10)
			{
				ascensions.Add(MegaCrit.Sts2.Core.Helpers.AscensionHelper.GetTitle(i).GetFormattedText());
				continue;
			}

			_ = ReForge.Ascension.TryGetLevelDefinition(i, out ReForge.ExtendedAscensionLevelDefinition definition);
			ascensions.Add(definition.Title);
		}

		LocString title = new LocString("ascension", "PORTRAIT_TITLE");
		title.Add("character", character.Title);
		title.Add("ascension", level);
		string description = string.Join("\n", ascensions);
		if (achievementsLocked)
		{
			string locked = new LocString("gameplay_ui", "ACHIEVEMENTS_LOCKED").GetFormattedText();
			description = level == 0 ? description + locked : description + "\n" + locked;
		}

		__result = new HoverTip(title, description);
		return false;
	}
}
