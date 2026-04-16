#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Unlocks;

public static partial class ReForge
{
	/// <summary>
	/// ReForge 遭遇战过滤 API：支持按怪物 ID 全局屏蔽地图刷新。
	/// </summary>
	public static class Encounters
	{
		private const string LogOwner = "ReForge.Encounters";
		private static readonly object SyncRoot = new();
		private static readonly HashSet<string> RemovedMonsterEntries = new(StringComparer.Ordinal);
		private static readonly FieldInfo? ActRoomsField = AccessTools.Field(typeof(ActModel), "_rooms");

		/// <summary>
		/// 删除怪物（按怪物 ID）。删除后，后续地图不会刷新包含该怪物的遭遇战。
		/// </summary>
		public static bool RemoveMonster(string monsterId)
		{
			if (!TryNormalizeMonsterEntry(monsterId, out string entry))
			{
				return false;
			}

			lock (SyncRoot)
			{
				return RemovedMonsterEntries.Add(entry);
			}
		}

		/// <summary>
		/// 恢复怪物（从删除名单中移除）。
		/// </summary>
		public static bool RestoreMonster(string monsterId)
		{
			if (!TryNormalizeMonsterEntry(monsterId, out string entry))
			{
				return false;
			}

			lock (SyncRoot)
			{
				return RemovedMonsterEntries.Remove(entry);
			}
		}

		/// <summary>
		/// 清空删除名单。
		/// </summary>
		public static void ClearRemovedMonsters()
		{
			lock (SyncRoot)
			{
				RemovedMonsterEntries.Clear();
			}
		}

		/// <summary>
		/// 判断怪物是否已被删除（支持 entry 或 category.entry 格式）。
		/// </summary>
		public static bool IsMonsterRemoved(string monsterId)
		{
			if (!TryNormalizeMonsterEntry(monsterId, out string entry))
			{
				return false;
			}

			return IsMonsterEntryRemoved(entry);
		}

		/// <summary>
		/// 获取当前删除名单快照。
		/// </summary>
		public static IReadOnlyCollection<string> GetRemovedMonstersSnapshot()
		{
			lock (SyncRoot)
			{
				return RemovedMonsterEntries.ToArray();
			}
		}

		internal static bool TryGetAllowedEncounter(ActModel act, RoomType roomType, out EncounterModel encounter)
		{
			encounter = null!;
			if (!HasAnyRemoval())
			{
				return false;
			}

			RoomSet? rooms = TryGetRooms(act);
			if (rooms == null)
			{
				return false;
			}

			switch (roomType)
			{
				case RoomType.Monster:
					if (TryPickAllowedByRotation(rooms.normalEncounters, rooms.normalEncountersVisited, out encounter))
					{
						return true;
					}

					EncounterModel? fallbackMonster = act.AllRegularEncounters.FirstOrDefault(static e => !IsEncounterBlocked(e));
					if (fallbackMonster == null)
					{
						return false;
					}

					encounter = fallbackMonster;
					return true;
				case RoomType.Elite:
					if (TryPickAllowedByRotation(rooms.eliteEncounters, rooms.eliteEncountersVisited, out encounter))
					{
						return true;
					}

					EncounterModel? fallbackElite = act.AllEliteEncounters.FirstOrDefault(static e => !IsEncounterBlocked(e));
					if (fallbackElite == null)
					{
						return false;
					}

					encounter = fallbackElite;
					return true;
				case RoomType.Boss:
					if (!TryPickAllowedBoss(act, rooms, out encounter))
					{
						return false;
					}

					return true;
				default:
					return false;
			}
		}

		internal static void SanitizeActRooms(ActModel act, int? desiredNormalCount = null)
		{
			if (!HasAnyRemoval())
			{
				return;
			}

			RoomSet? rooms = TryGetRooms(act);
			if (rooms == null)
			{
				return;
			}

			int normalTargetCount = Math.Max(1, desiredNormalCount ?? rooms.normalEncounters.Count);

			SanitizeEncounterList(
				rooms.normalEncounters,
				rooms.normalEncountersVisited,
				act.AllRegularEncounters,
				targetCount: normalTargetCount
			);
			SanitizeEncounterList(
				rooms.eliteEncounters,
				rooms.eliteEncountersVisited,
				act.AllEliteEncounters,
				targetCount: Math.Max(1, rooms.eliteEncounters.Count)
			);

			if (IsEncounterBlocked(rooms.Boss))
			{
				EncounterModel? fallbackBoss = act.AllBossEncounters.FirstOrDefault(static e => !IsEncounterBlocked(e));
				if (fallbackBoss != null)
				{
					rooms.Boss = fallbackBoss;
				}
				else
				{
					GD.PrintErr($"[{LogOwner}] all boss encounters are blocked; keeping current boss '{rooms.Boss.Id}'.");
				}
			}

			if (rooms.SecondBoss != null && IsEncounterBlocked(rooms.SecondBoss))
			{
				EncounterModel? fallbackSecondBoss = act.AllBossEncounters.FirstOrDefault(e => !IsEncounterBlocked(e) && e.Id != rooms.Boss.Id);
				rooms.SecondBoss = fallbackSecondBoss;
			}
		}

		private static bool TryNormalizeMonsterEntry(string monsterId, out string normalized)
		{
			normalized = string.Empty;
			if (string.IsNullOrWhiteSpace(monsterId))
			{
				return false;
			}

			string value = monsterId.Trim().ToLowerInvariant();
			int splitIndex = value.LastIndexOf('.');
			if (splitIndex >= 0 && splitIndex < value.Length - 1)
			{
				value = value[(splitIndex + 1)..];
			}

			normalized = value;
			return normalized.Length > 0;
		}

		private static bool HasAnyRemoval()
		{
			lock (SyncRoot)
			{
				return RemovedMonsterEntries.Count > 0;
			}
		}

		private static bool IsMonsterEntryRemoved(string entry)
		{
			lock (SyncRoot)
			{
				return RemovedMonsterEntries.Contains(entry);
			}
		}

		private static bool IsEncounterBlocked(EncounterModel? encounter)
		{
			if (encounter == null)
			{
				return false;
			}

			foreach (MonsterModel monster in encounter.AllPossibleMonsters)
			{
				if (monster == null)
				{
					continue;
				}

				string entry = monster.Id.Entry.ToLowerInvariant();
				if (IsMonsterEntryRemoved(entry))
				{
					return true;
				}
			}

			return false;
		}

		private static RoomSet? TryGetRooms(ActModel act)
		{
			if (ActRoomsField == null)
			{
				GD.PrintErr($"[{LogOwner}] ActModel._rooms field not found; encounter filtering is degraded.");
				return null;
			}

			try
			{
				return ActRoomsField.GetValue(act) as RoomSet;
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[{LogOwner}] failed to access ActModel room set. {ex}");
				return null;
			}
		}

		private static bool TryPickAllowedByRotation(IReadOnlyList<EncounterModel> encounters, int visited, out EncounterModel encounter)
		{
			encounter = null!;
			if (encounters.Count == 0)
			{
				return false;
			}

			for (int offset = 0; offset < encounters.Count; offset++)
			{
				int index = (visited + offset) % encounters.Count;
				EncounterModel candidate = encounters[index];
				if (!IsEncounterBlocked(candidate))
				{
					encounter = candidate;
					return true;
				}
			}

			return false;
		}

		private static bool TryPickAllowedBoss(ActModel act, RoomSet rooms, out EncounterModel encounter)
		{
			encounter = null!;
			EncounterModel current = rooms.NextBossEncounter;
			if (!IsEncounterBlocked(current))
			{
				encounter = current;
				return true;
			}

			EncounterModel? fallback = act.AllBossEncounters.FirstOrDefault(static e => !IsEncounterBlocked(e));
			if (fallback == null)
			{
				return false;
			}

			encounter = fallback;
			return true;
		}

		private static void SanitizeEncounterList(
			List<EncounterModel> targets,
			int visited,
			IEnumerable<EncounterModel> pool,
			int targetCount)
		{
			targets.RemoveAll(static e => IsEncounterBlocked(e));
			if (targets.Count > 0)
			{
				return;
			}

			List<EncounterModel> candidates = pool.Where(static e => !IsEncounterBlocked(e)).Distinct().ToList();
			if (candidates.Count == 0)
			{
				GD.PrintErr($"[{LogOwner}] all encounter candidates are blocked; list remains empty.");
				return;
			}

			int startIndex = Math.Clamp(visited, 0, int.MaxValue);
			for (int i = 0; i < targetCount; i++)
			{
				EncounterModel candidate = candidates[(startIndex + i) % candidates.Count];
				targets.Add(candidate);
			}
		}
	}
}

[HarmonyPatch(typeof(ActModel), nameof(ActModel.GenerateRooms))]
internal static class ReForgeEncounterFilterGenerateRoomsPatch
{
	[HarmonyPostfix]
	private static void Postfix(ActModel __instance, Rng rng, UnlockState unlockState, bool isMultiplayer)
	{
		ReForge.Encounters.SanitizeActRooms(__instance, desiredNormalCount: __instance.GetNumberOfRooms(isMultiplayer));
	}
}

[HarmonyPatch(typeof(ActModel), nameof(ActModel.ValidateRoomsAfterLoad))]
internal static class ReForgeEncounterFilterValidateRoomsPatch
{
	[HarmonyPostfix]
	private static void Postfix(ActModel __instance, Rng rng)
	{
		ReForge.Encounters.SanitizeActRooms(__instance);
	}
}

[HarmonyPatch(typeof(ActModel), nameof(ActModel.PullNextEncounter))]
internal static class ReForgeEncounterFilterPullNextPatch
{
	[HarmonyPrefix]
	private static bool Prefix(ActModel __instance, RoomType roomType, ref EncounterModel __result)
	{
		if (!ReForge.Encounters.TryGetAllowedEncounter(__instance, roomType, out EncounterModel encounter))
		{
			return true;
		}

		__result = encounter;
		return false;
	}
}
