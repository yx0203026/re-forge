#nullable enable

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Runs;

public static partial class ReForge
{
    /// <summary>
    /// 地图布局调整：允许通过配置改变每幕房间数量，并动态调整
    /// 地图屏幕布局以适应新的房间数量。
    /// </summary>
	public static class MapLayout
	{
		private const int DefaultRoomsPerAct = 15;
		private const int MinRoomsPerAct = 5;
		private const float DefaultScrollTop = 1800f;
		private const float DefaultScrollBottom = -600f;
		private const float PreferredMinVerticalSpacing = 145f;
		private const float MapContentVerticalPadding = 12f;
		private const ulong CursorSyncMinIntervalMs = 33;
		private const float CursorSyncMinDistance = 1.25f;

		private static readonly object SyncRoot = new();
		private static readonly Dictionary<ulong, DynamicMapLayoutState> RuntimeLayoutStates = new();

		// 缓存反射字段，避免每次布局都创建 Traverse/Field 访问器。
		private static readonly FieldInfo? NMapScreenMapField = AccessTools.Field(typeof(NMapScreen), "_map");
		private static readonly FieldInfo? NMapScreenDistYField = AccessTools.Field(typeof(NMapScreen), "_distY");
		private static readonly FieldInfo? NMapScreenPointsField = AccessTools.Field(typeof(NMapScreen), "_points");
		private static readonly FieldInfo? NMapScreenPathsContainerField = AccessTools.Field(typeof(NMapScreen), "_pathsContainer");
		private static readonly FieldInfo? NMapScreenMapBgContainerField = AccessTools.Field(typeof(NMapScreen), "_mapBgContainer");
		private static readonly FieldInfo? NMapScreenMapContainerField = AccessTools.Field(typeof(NMapScreen), "_mapContainer");
		private static readonly FieldInfo? NMapScreenTargetDragPosField = AccessTools.Field(typeof(NMapScreen), "_targetDragPos");
		private static readonly FieldInfo? NMapScreenIsDraggingField = AccessTools.Field(typeof(NMapScreen), "_isDragging");

		private static int _targetRoomsPerAct = DefaultRoomsPerAct;
		private static bool _enabled;

		public static bool Enabled
		{
			get
			{
				lock (SyncRoot)
				{
					return _enabled;
				}
			}
		}

		public static int TargetRoomsPerAct
		{
			get
			{
				lock (SyncRoot)
				{
					return _targetRoomsPerAct;
				}
			}
		}

		public static void ConfigureRoomsPerAct(int roomsPerAct)
		{
			if (roomsPerAct < MinRoomsPerAct)
			{
				throw new ArgumentOutOfRangeException(nameof(roomsPerAct), $"roomsPerAct must be >= {MinRoomsPerAct}.");
			}

			lock (SyncRoot)
			{
				_targetRoomsPerAct = roomsPerAct;
				_enabled = roomsPerAct != DefaultRoomsPerAct;
			}
		}

		public static void ConfigureFloors(int totalFloors)
		{
			if (totalFloors < MinRoomsPerAct + 2)
			{
				throw new ArgumentOutOfRangeException(nameof(totalFloors), $"totalFloors must be >= {MinRoomsPerAct + 2}.");
			}

			ConfigureRoomsPerAct(totalFloors - 2);
		}

		public static void Reset()
		{
			lock (SyncRoot)
			{
				_targetRoomsPerAct = DefaultRoomsPerAct;
				_enabled = false;
			}
		}

		internal static bool TryOverrideRoomCount(bool isMultiplayer, out int roomCount)
		{
			lock (SyncRoot)
			{
				if (!_enabled)
				{
					roomCount = 0;
					return false;
				}

				roomCount = isMultiplayer ? Math.Max(MinRoomsPerAct - 1, _targetRoomsPerAct - 1) : _targetRoomsPerAct;
				return true;
			}
		}

		internal static void ReflowMapScreen(NMapScreen mapScreen)
		{
			if (mapScreen == null)
			{
				return;
			}

			if (!Enabled)
			{
				ClearDynamicState(mapScreen);
				return;
			}

			try
			{
				if (!TryGetMapScreenFieldValue(mapScreen, NMapScreenMapField, out ActMap map))
				{
					ClearDynamicState(mapScreen);
					return;
				}

				if (map == null || map is NullActMap)
				{
					ClearDynamicState(mapScreen);
					return;
				}

				int rowCount = map.GetRowCount();
				if (rowCount <= 1)
				{
					ClearDynamicState(mapScreen);
					return;
				}

				if (!TryGetMapScreenFieldValue(mapScreen, NMapScreenDistYField, out float distY))
				{
					ClearDynamicState(mapScreen);
					return;
				}

				if (distY <= 0.01f)
				{
					ClearDynamicState(mapScreen);
					return;
				}

				TryGetMapScreenFieldValue(mapScreen, NMapScreenPointsField, out Control points);
				TryGetMapScreenFieldValue(mapScreen, NMapScreenPathsContainerField, out Control paths);
				TryGetMapScreenFieldValue(mapScreen, NMapScreenMapBgContainerField, out Control mapBg);
				TryGetMapScreenFieldValue(mapScreen, NMapScreenMapContainerField, out Control mapContainer);
				if (points == null || paths == null || mapContainer == null)
				{
					ClearDynamicState(mapScreen);
					return;
				}

				ulong layoutSignature = BuildLayoutSignature(map, rowCount, distY, points, paths);
				if (TryGetCachedState(mapScreen, out DynamicMapLayoutState? cachedState) &&
					cachedState.LayoutSignature == layoutSignature)
				{
					return;
				}

				float scaleY = Math.Max(1f, PreferredMinVerticalSpacing / distY);
				scaleY = ClampScaleYToBackground(scaleY, points, mapBg);

				float contentBottomAnchorY = GetChildrenBottomY(points);
				RespaceChildrenY(points, scaleY, contentBottomAnchorY);
				RespaceChildrenY(paths, scaleY, contentBottomAnchorY);
				ApplyBackgroundScaleY(mapBg, Math.Max(1f, scaleY));
				AlignAndClampForegroundToBackground(mapContainer, mapBg);
				StoreDynamicState(mapScreen, scaleY, layoutSignature);
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[ReForge.MapLayout] ReflowMapScreen failed: {ex}");
				ClearDynamicState(mapScreen);
			}
		}

		internal static bool TryGetDynamicScrollRange(NMapScreen mapScreen, out float topLimit, out float bottomLimit)
		{
			if (TryGetCachedState(mapScreen, out DynamicMapLayoutState? state))
			{
				topLimit = state.TopLimit;
				bottomLimit = state.BottomLimit;
				return true;
			}

			topLimit = DefaultScrollTop;
			bottomLimit = DefaultScrollBottom;
			return false;
		}

		internal static bool ShouldForceRemoteCursorSync(NMapScreen mapScreen, Vector2 mapPosition)
		{
			if (!TryGetCachedState(mapScreen, out DynamicMapLayoutState? state))
			{
				return true;
			}

			ulong nowTicks = Time.GetTicksMsec();
			float distance = state.LastCursorSyncPos.DistanceTo(mapPosition);
			if (distance < CursorSyncMinDistance && nowTicks - state.LastCursorSyncTicks < CursorSyncMinIntervalMs)
			{
				return false;
			}

			lock (SyncRoot)
			{
				state.LastCursorSyncPos = mapPosition;
				state.LastCursorSyncTicks = nowTicks;
			}

			return true;
		}

		private static float GetChildrenBottomY(Control container)
		{
			float maxY = float.MinValue;
			bool foundAnyControl = false;

			foreach (Node child in container.GetChildren())
			{
				if (child is not Control control)
				{
					continue;
				}

				foundAnyControl = true;
				if (control.Position.Y > maxY)
				{
					maxY = control.Position.Y;
				}
			}

			if (!foundAnyControl)
			{
				return 0f;
			}

			return maxY;
		}

		private static bool TryGetChildrenVerticalBounds(Control container, out float minY, out float maxY)
		{
			minY = float.MaxValue;
			maxY = float.MinValue;
			bool foundAnyControl = false;

			foreach (Node child in container.GetChildren())
			{
				if (child is not Control control)
				{
					continue;
				}

				foundAnyControl = true;
				if (control.Position.Y < minY)
				{
					minY = control.Position.Y;
				}

				if (control.Position.Y > maxY)
				{
					maxY = control.Position.Y;
				}
			}

			if (!foundAnyControl)
			{
				minY = 0f;
				maxY = 0f;
				return false;
			}

			return true;
		}

		private static void RespaceChildrenY(Control container, float scaleY, float anchorY)
		{
			foreach (Node child in container.GetChildren())
			{
				if (child is not Control control)
				{
					continue;
				}

				Vector2 pos = control.Position;
				float anchoredY = anchorY + (pos.Y - anchorY) * scaleY;
				control.Position = new Vector2(pos.X, anchoredY);
			}
		}

		private static float ClampScaleYToBackground(float desiredScaleY, Control? points, Control? mapBg)
		{
			if (desiredScaleY <= 0f || points == null || mapBg == null)
			{
				return desiredScaleY;
			}

			if (!TryGetChildrenVerticalBounds(points, out float contentTop, out float contentBottom))
			{
				return desiredScaleY;
			}

			float contentHeight = contentBottom - contentTop;
			if (contentHeight <= 0.01f)
			{
				return desiredScaleY;
			}

			if (!TryGetMapBackgroundVerticalRange(mapBg, out float bgTop, out float bgBottom))
			{
				return desiredScaleY;
			}

			float bgHeight = bgBottom - bgTop;
			float usableHeight = bgHeight - MapContentVerticalPadding * 2f;
			if (usableHeight <= 0.01f)
			{
				return desiredScaleY;
			}

			float maxScaleY = usableHeight / contentHeight;
			if (maxScaleY <= 0f)
			{
				return desiredScaleY;
			}

			return Math.Min(desiredScaleY, maxScaleY);
		}

		private static void AlignAndClampForegroundToBackground(Control mapContainer, Control? mapBg)
		{
			if (mapBg == null)
			{
				return;
			}

			if (!TryGetForegroundVerticalBounds(mapContainer, mapBg, out float contentTop, out float contentBottom))
			{
				return;
			}

			if (!TryGetMapBackgroundVerticalRange(mapBg, out float bgTop, out float bgBottom))
			{
				return;
			}

			float bgTopWithPadding = bgTop + MapContentVerticalPadding;
			float bgBottomWithPadding = bgBottom - MapContentVerticalPadding;
			if (bgBottomWithPadding <= bgTopWithPadding)
			{
				return;
			}

			// 只做一次范围采样：先中心对齐，再用边界规则做二次修正。
			float contentCenterY = (contentTop + contentBottom) * 0.5f;
			float bgCenterY = (bgTopWithPadding + bgBottomWithPadding) * 0.5f;
			float offsetY = bgCenterY - contentCenterY;

			float shiftedTop = contentTop + offsetY;
			float shiftedBottom = contentBottom + offsetY;
			if (shiftedBottom > bgBottomWithPadding)
			{
				float pullUpOffset = bgBottomWithPadding - shiftedBottom;
				offsetY += pullUpOffset;
				shiftedTop += pullUpOffset;
			}

			if (shiftedTop < bgTopWithPadding)
			{
				offsetY += bgTopWithPadding - shiftedTop;
			}

			if (Math.Abs(offsetY) <= 0.01f)
			{
				return;
			}

			ShiftForegroundChildrenY(mapContainer, mapBg, offsetY);
		}

		private static bool TryGetMapScreenFieldValue<T>(NMapScreen mapScreen, FieldInfo? field, out T value)
		{
			value = default!;
			if (field == null)
			{
				return false;
			}

			object? rawValue = field.GetValue(mapScreen);
			if (rawValue is not T typedValue)
			{
				return false;
			}

			value = typedValue;
			return true;
		}

		private static bool TrySetMapScreenFieldValue<T>(NMapScreen mapScreen, FieldInfo? field, T value)
		{
			if (field == null)
			{
				return false;
			}

			field.SetValue(mapScreen, value);
			return true;
		}

		internal static bool TryGetMapContainer(NMapScreen mapScreen, out Control mapContainer)
		{
			return TryGetMapScreenFieldValue(mapScreen, NMapScreenMapContainerField, out mapContainer);
		}

		internal static bool TryGetTargetDragPos(NMapScreen mapScreen, out Vector2 targetDragPos)
		{
			return TryGetMapScreenFieldValue(mapScreen, NMapScreenTargetDragPosField, out targetDragPos);
		}

		internal static bool TrySetTargetDragPos(NMapScreen mapScreen, Vector2 targetDragPos)
		{
			return TrySetMapScreenFieldValue(mapScreen, NMapScreenTargetDragPosField, targetDragPos);
		}

		internal static bool TryGetIsDragging(NMapScreen mapScreen, out bool isDragging)
		{
			return TryGetMapScreenFieldValue(mapScreen, NMapScreenIsDraggingField, out isDragging);
		}

		private static bool TryGetMapBackgroundVerticalRange(Control mapBg, out float topY, out float bottomY)
		{
			topY = mapBg.Position.Y;
			float height = mapBg.Size.Y * mapBg.Scale.Y;
			if (height <= 0.01f)
			{
				bottomY = topY;
				return false;
			}

			bottomY = topY + height;
			return true;
		}

		private static bool TryGetForegroundVerticalBounds(Control mapContainer, Control mapBg, out float minY, out float maxY)
		{
			minY = float.MaxValue;
			maxY = float.MinValue;
			bool foundAny = false;

			foreach (Node child in mapContainer.GetChildren())
			{
				if (child is not Control childControl || ReferenceEquals(childControl, mapBg))
				{
					continue;
				}

				CollectVerticalBounds(childControl, childControl.Position.Y, childControl.Scale.Y, ref minY, ref maxY, ref foundAny);
			}

			if (!foundAny)
			{
				minY = 0f;
				maxY = 0f;
				return false;
			}

			return true;
		}

		private static void CollectVerticalBounds(
			Control control,
			float absoluteTop,
			float absoluteScaleY,
			ref float minY,
			ref float maxY,
			ref bool foundAny)
		{
			float height = control.Size.Y * absoluteScaleY;
			if (height > 0.01f)
			{
				foundAny = true;
				float bottom = absoluteTop + height;
				if (absoluteTop < minY)
				{
					minY = absoluteTop;
				}

				if (bottom > maxY)
				{
					maxY = bottom;
				}
			}

			foreach (Node child in control.GetChildren())
			{
				if (child is not Control childControl)
				{
					continue;
				}

				float childTop = absoluteTop + childControl.Position.Y * absoluteScaleY;
				float childScaleY = absoluteScaleY * childControl.Scale.Y;
				CollectVerticalBounds(childControl, childTop, childScaleY, ref minY, ref maxY, ref foundAny);
			}
		}

		private static void ShiftForegroundChildrenY(Control mapContainer, Control mapBg, float offsetY)
		{
			foreach (Node child in mapContainer.GetChildren())
			{
				if (child is not Control control || ReferenceEquals(control, mapBg))
				{
					continue;
				}

				Vector2 pos = control.Position;
				control.Position = new Vector2(pos.X, pos.Y + offsetY);
			}
		}

		private static ulong BuildLayoutSignature(ActMap map, int rowCount, float distY, Control points, Control paths)
		{
			ulong mapIdentity = unchecked((ulong)RuntimeHelpers.GetHashCode(map));
			ulong distYQuantized = (ulong)Math.Clamp((int)Math.Round(distY * 1000f), 0, int.MaxValue);
			ulong pointsCount = (ulong)points.GetChildCount();
			ulong pathsCount = (ulong)paths.GetChildCount();

			ulong signature = mapIdentity;
			signature = (signature * 397) ^ (ulong)(uint)rowCount;
			signature = (signature * 397) ^ distYQuantized;
			signature = (signature * 397) ^ pointsCount;
			signature = (signature * 397) ^ pathsCount;
			return signature;
		}

		private static bool TryGetCachedState(NMapScreen mapScreen, out DynamicMapLayoutState state)
		{
			lock (SyncRoot)
			{
				return RuntimeLayoutStates.TryGetValue(mapScreen.GetInstanceId(), out state!);
			}
		}

		private static void StoreDynamicState(NMapScreen mapScreen, float scaleY, ulong layoutSignature)
		{
			DynamicMapLayoutState state = new DynamicMapLayoutState
			{
				TopLimit = DefaultScrollTop * scaleY,
				BottomLimit = DefaultScrollBottom,
				LayoutSignature = layoutSignature,
				LastCursorSyncPos = Vector2.Zero,
				LastCursorSyncTicks = 0
			};

			lock (SyncRoot)
			{
				RuntimeLayoutStates[mapScreen.GetInstanceId()] = state;
			}
		}

		internal static void ClearDynamicState(NMapScreen mapScreen)
		{
			TryGetMapScreenFieldValue(mapScreen, NMapScreenMapBgContainerField, out Control mapBg);
			ApplyBackgroundScaleY(mapBg, 1f);

			lock (SyncRoot)
			{
				RuntimeLayoutStates.Remove(mapScreen.GetInstanceId());
			}
		}

		private static void ApplyBackgroundScaleY(Control? mapBg, float scaleY)
		{
			if (mapBg == null)
			{
				return;
			}

			Vector2 scale = mapBg.Scale;
			Vector2 pos = mapBg.Position;
			float currentBottom = pos.Y + mapBg.Size.Y * scale.Y;
			float newPosY = currentBottom - mapBg.Size.Y * scaleY;

			mapBg.Scale = new Vector2(scale.X, scaleY);
			mapBg.Position = new Vector2(pos.X, newPosY);
		}
	}
}

internal sealed class DynamicMapLayoutState
{
	public float TopLimit { get; set; }

	public float BottomLimit { get; set; }

	public ulong LayoutSignature { get; set; }

	public Vector2 LastCursorSyncPos { get; set; }

	public ulong LastCursorSyncTicks { get; set; }
}

[HarmonyPatch(typeof(ActModel), nameof(ActModel.GetNumberOfRooms))]
internal static class ReForgeMapLayoutRoomsPatch
{
	[HarmonyPostfix]
	private static void Postfix(bool isMultiplayer, ref int __result)
	{
		if (ReForge.MapLayout.TryOverrideRoomCount(isMultiplayer, out int roomCount))
		{
			__result = roomCount;
		}
	}
}

[HarmonyPatch(typeof(NMapScreen), nameof(NMapScreen.SetMap))]
internal static class ReForgeMapLayoutSetMapPatch
{
	[HarmonyPrefix]
	private static void Prefix(NMapScreen __instance)
	{
		try
		{
			ReForge.MapLayout.ClearDynamicState(__instance);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[ReForge.MapLayout] SetMap prefix clear-state failed: {ex}");
		}
	}

	[HarmonyPostfix]
	private static void Postfix(NMapScreen __instance)
	{
		try
		{
			ReForge.MapLayout.ReflowMapScreen(__instance);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[ReForge.MapLayout] SetMap postfix failed: {ex}");
		}
	}
}

[HarmonyPatch(typeof(NMapScreen), "UpdateScrollPosition")]
internal static class ReForgeMapLayoutScrollPatch
{
	[HarmonyPrefix]
	private static bool Prefix(NMapScreen __instance, double delta)
	{
		if (!ReForge.MapLayout.TryGetDynamicScrollRange(__instance, out float topLimit, out float bottomLimit))
		{
			return true;
		}

		if (!ReForge.MapLayout.TryGetMapContainer(__instance, out Control mapContainer))
		{
			return true;
		}

		if (mapContainer == null)
		{
			return true;
		}

		if (!ReForge.MapLayout.TryGetTargetDragPos(__instance, out Vector2 target))
		{
			return true;
		}

		Vector2 previousMapPosition = mapContainer.Position;
		Vector2 originalTarget = target;

		if (mapContainer.Position != target)
		{
			mapContainer.Position = mapContainer.Position.Lerp(target, (float)delta * 15f);
			if (mapContainer.Position.DistanceTo(target) < 0.5f)
			{
				mapContainer.Position = target;
			}
		}

		if (!ReForge.MapLayout.TryGetIsDragging(__instance, out bool isDragging))
		{
			return true;
		}

		if (!isDragging)
		{
			// 用动态边界替代原版硬编码的 -600/1800。
			if (target.Y < bottomLimit)
			{
				target = target.Lerp(new Vector2(target.X, bottomLimit), (float)delta * 12f);
			}
			else if (target.Y > topLimit)
			{
				target = target.Lerp(new Vector2(target.X, topLimit), (float)delta * 12f);
			}
		}

		ReForge.MapLayout.TrySetTargetDragPos(__instance, target);
		bool targetAdjusted = target != originalTarget;
		bool mapMoved = mapContainer.Position.DistanceSquaredTo(previousMapPosition) > 0.01f;

		// 联机才需要远端光标同步；并且必须发生实际地图运动或目标修正。
		if (ReForge.Network.IsConnected &&
			(isDragging || mapMoved || targetAdjusted) &&
			ReForge.MapLayout.ShouldForceRemoteCursorSync(__instance, mapContainer.Position))
		{
			NGame.Instance?.RemoteCursorContainer?.ForceUpdateAllCursors();
		}
		return false;
	}
}
