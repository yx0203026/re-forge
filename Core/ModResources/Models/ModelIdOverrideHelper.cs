#nullable enable

using System;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;

namespace ReForgeFramework.ModResources.Models;

internal static class ModelIdOverrideHelper
{
	private static readonly FieldInfo? IdBackingField = AccessTools.Field(typeof(AbstractModel), "<Id>k__BackingField");

	public static bool TryOverride(AbstractModel model, ModelId modelId)
	{
		ArgumentNullException.ThrowIfNull(model);
		ArgumentNullException.ThrowIfNull(modelId);

		if (IdBackingField == null)
		{
			GD.PrintErr("[ReForge.ModelIdOverride] Cannot locate AbstractModel.Id backing field.");
			return false;
		}

		ModelId originalId = model.Id;
		try
		{
			IdBackingField.SetValue(model, modelId);
			model.InitId(modelId);
			return true;
		}
		catch (Exception ex)
		{
			// 失败时必须回滚，否则模型会携带一个未注册的非法 ID，后续序列化/网络映射会直接崩溃。
			try
			{
				IdBackingField.SetValue(model, originalId);
				model.InitId(originalId);
			}
			catch (Exception rollbackEx)
			{
				GD.PrintErr($"[ReForge.ModelIdOverride] Rollback failed for model id '{originalId}'. {rollbackEx}");
			}

			GD.PrintErr($"[ReForge.ModelIdOverride] Failed to override model id to '{modelId}'. {ex}");
			return false;
		}
	}
}
