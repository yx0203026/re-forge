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
		private static readonly object _cardPortraitLoaderCacheSync = new();
		private static readonly System.Collections.Generic.Dictionary<string, Func<CardModel, Texture2D?>> _cardPortraitLoaderCache = new(StringComparer.OrdinalIgnoreCase);

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

		/// <summary>
		/// 官方封装：从模组资源路径注册卡牌立绘，内置异常保护与懒加载缓存。
		/// 返回值表示注册调用是否成功。
		/// </summary>
		public static bool TryRegisterCardPortraitFromModResource(string modelEntry, string resourcePath, string? logOwner = null)
		{
			if (string.IsNullOrWhiteSpace(modelEntry))
			{
				GD.PrintErr("[ReForge.ModelTextures] failed to register card portrait: modelEntry is empty.");
				return false;
			}

			if (string.IsNullOrWhiteSpace(resourcePath))
			{
				GD.PrintErr($"[ReForge.ModelTextures] failed to register card portrait for '{modelEntry}': resourcePath is empty.");
				return false;
			}

			string owner = string.IsNullOrWhiteSpace(logOwner) ? "ReForge.ModelTextures" : logOwner;

			try
			{
				RegisterCardPortrait(modelEntry, GetOrCreateCachedCardPortraitLoader(modelEntry, resourcePath, owner));
				return true;
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[{owner}] failed to register custom portrait for '{modelEntry}'. {ex}");
				return false;
			}
		}

		private static Func<CardModel, Texture2D?> GetOrCreateCachedCardPortraitLoader(string modelEntry, string resourcePath, string owner)
		{
			string cacheKey = $"{modelEntry}|{resourcePath}";
			lock (_cardPortraitLoaderCacheSync)
			{
				if (_cardPortraitLoaderCache.TryGetValue(cacheKey, out Func<CardModel, Texture2D?>? existing))
				{
					return existing;
				}

				object textureSync = new();
				Texture2D? cachedTexture = null;
				Func<CardModel, Texture2D?> loader = _ =>
				{
					Texture2D? snapshot = cachedTexture;
					if (snapshot != null)
					{
						return snapshot;
					}

					lock (textureSync)
					{
						if (cachedTexture != null)
						{
							return cachedTexture;
						}

						Texture2D? loaded = LoadFromModResource(resourcePath);
						if (loaded == null)
						{
							GD.PrintErr($"[{owner}] failed to load portrait resource '{resourcePath}' for '{modelEntry}'.");
							return null;
						}

						cachedTexture = loaded;
						return loaded;
					}
				};

				_cardPortraitLoaderCache[cacheKey] = loader;
				return loader;
			}
		}
	}
}
