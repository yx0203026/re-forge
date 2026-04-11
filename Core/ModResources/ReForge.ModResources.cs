#nullable enable

using System;
using Godot;
using MegaCrit.Sts2.Core.Models;
using ReForgeFramework.ModLoading;

public static partial class ReForge
{
	/// <summary>
	/// ReForge 模型纹理 API 入口。
	/// </summary>
	public static class ModelTextures
	{
		public static void RegisterCardPortrait(ModelId modelId, Func<CardModel, Texture2D?> loader)
		{
			ReForgeFramework.ModResources.ModelTextureRegistry.RegisterCardPortrait(modelId, loader);
		}

		public static void RegisterCardPortrait(string modelEntry, Func<CardModel, Texture2D?> loader)
		{
			ReForgeFramework.ModResources.ModelTextureRegistry.RegisterCardPortrait(modelEntry, loader);
		}

		public static void RegisterRelicIcon(ModelId modelId, Func<RelicModel, Texture2D?> loader)
		{
			ReForgeFramework.ModResources.ModelTextureRegistry.RegisterRelicIcon(modelId, loader);
		}

		public static void RegisterRelicIcon(string modelEntry, Func<RelicModel, Texture2D?> loader)
		{
			ReForgeFramework.ModResources.ModelTextureRegistry.RegisterRelicIcon(modelEntry, loader);
		}

		public static void RegisterRelicIconOutline(ModelId modelId, Func<RelicModel, Texture2D?> loader)
		{
			ReForgeFramework.ModResources.ModelTextureRegistry.RegisterRelicIconOutline(modelId, loader);
		}

		public static void RegisterRelicIconOutline(string modelEntry, Func<RelicModel, Texture2D?> loader)
		{
			ReForgeFramework.ModResources.ModelTextureRegistry.RegisterRelicIconOutline(modelEntry, loader);
		}

		public static void RegisterRelicBigIcon(ModelId modelId, Func<RelicModel, Texture2D?> loader)
		{
			ReForgeFramework.ModResources.ModelTextureRegistry.RegisterRelicBigIcon(modelId, loader);
		}

		public static void RegisterRelicBigIcon(string modelEntry, Func<RelicModel, Texture2D?> loader)
		{
			ReForgeFramework.ModResources.ModelTextureRegistry.RegisterRelicBigIcon(modelEntry, loader);
		}

		/// <summary>
		/// 便捷方法：从 ReForge 模组资源系统按路径读取纹理。
		/// 适用于注册委托中的一行式加载。
		/// </summary>
		public static Texture2D? LoadFromModResource(string resourcePath)
		{
			if (ReForgeModManager.TryLoadTexture(resourcePath, out Texture2D texture))
			{
				return texture;
			}

			return null;
		}
	}
}
