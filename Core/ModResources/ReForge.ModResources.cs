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
		private static readonly object _relicIconLoaderCacheSync = new();
		private static readonly System.Collections.Generic.Dictionary<string, Func<RelicModel, Texture2D?>> _relicIconLoaderCache = new(StringComparer.OrdinalIgnoreCase);
		private static readonly object _relicIconOutlineLoaderCacheSync = new();
		private static readonly System.Collections.Generic.Dictionary<string, Func<RelicModel, Texture2D?>> _relicIconOutlineLoaderCache = new(StringComparer.OrdinalIgnoreCase);
		private static readonly object _relicBigIconLoaderCacheSync = new();
		private static readonly System.Collections.Generic.Dictionary<string, Func<RelicModel, Texture2D?>> _relicBigIconLoaderCache = new(StringComparer.OrdinalIgnoreCase);
		private static readonly object _powerIconLoaderCacheSync = new();
		private static readonly System.Collections.Generic.Dictionary<string, Func<PowerModel, Texture2D?>> _powerIconLoaderCache = new(StringComparer.OrdinalIgnoreCase);
		private static readonly object _powerBigIconLoaderCacheSync = new();
		private static readonly System.Collections.Generic.Dictionary<string, Func<PowerModel, Texture2D?>> _powerBigIconLoaderCache = new(StringComparer.OrdinalIgnoreCase);
		private static readonly object _rawTextureLoaderCacheSync = new();
		private static readonly System.Collections.Generic.Dictionary<string, Func<Texture2D?>> _rawTextureLoaderCache = new(StringComparer.OrdinalIgnoreCase);
		private static readonly object _namedTextureLoaderSync = new();
		private static readonly System.Collections.Generic.Dictionary<string, Func<Texture2D?>> _namedTextureLoaders = new(StringComparer.OrdinalIgnoreCase);

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

		public static void RegisterPowerIcon(ModelId modelId, Func<PowerModel, Texture2D?> loader)
		{
			ReForgeFramework.ModResources.ModelTextureRegistry.RegisterPowerIcon(modelId, loader);
		}

		public static void RegisterPowerIcon(string modelEntry, Func<PowerModel, Texture2D?> loader)
		{
			ReForgeFramework.ModResources.ModelTextureRegistry.RegisterPowerIcon(modelEntry, loader);
		}

		public static void RegisterPowerBigIcon(ModelId modelId, Func<PowerModel, Texture2D?> loader)
		{
			ReForgeFramework.ModResources.ModelTextureRegistry.RegisterPowerBigIcon(modelId, loader);
		}

		public static void RegisterPowerBigIcon(string modelEntry, Func<PowerModel, Texture2D?> loader)
		{
			ReForgeFramework.ModResources.ModelTextureRegistry.RegisterPowerBigIcon(modelEntry, loader);
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

		public static bool TryRegisterRelicIconFromModResource(string modelEntry, string resourcePath, string? logOwner = null)
		{
			if (string.IsNullOrWhiteSpace(modelEntry))
			{
				GD.PrintErr("[ReForge.ModelTextures] failed to register relic icon: modelEntry is empty.");
				return false;
			}

			if (string.IsNullOrWhiteSpace(resourcePath))
			{
				GD.PrintErr($"[ReForge.ModelTextures] failed to register relic icon for '{modelEntry}': resourcePath is empty.");
				return false;
			}

			string owner = string.IsNullOrWhiteSpace(logOwner) ? "ReForge.ModelTextures" : logOwner;

			try
			{
				RegisterRelicIcon(modelEntry, GetOrCreateCachedRelicIconLoader(modelEntry, resourcePath, owner));
				return true;
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[{owner}] failed to register relic icon for '{modelEntry}'. {ex}");
				return false;
			}
		}

		public static bool TryRegisterRelicIconOutlineFromModResource(string modelEntry, string resourcePath, string? logOwner = null)
		{
			if (string.IsNullOrWhiteSpace(modelEntry))
			{
				GD.PrintErr("[ReForge.ModelTextures] failed to register relic icon outline: modelEntry is empty.");
				return false;
			}

			if (string.IsNullOrWhiteSpace(resourcePath))
			{
				GD.PrintErr($"[ReForge.ModelTextures] failed to register relic icon outline for '{modelEntry}': resourcePath is empty.");
				return false;
			}

			string owner = string.IsNullOrWhiteSpace(logOwner) ? "ReForge.ModelTextures" : logOwner;

			try
			{
				RegisterRelicIconOutline(modelEntry, GetOrCreateCachedRelicIconOutlineLoader(modelEntry, resourcePath, owner));
				return true;
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[{owner}] failed to register relic icon outline for '{modelEntry}'. {ex}");
				return false;
			}
		}

		public static bool TryRegisterRelicBigIconFromModResource(string modelEntry, string resourcePath, string? logOwner = null)
		{
			if (string.IsNullOrWhiteSpace(modelEntry))
			{
				GD.PrintErr("[ReForge.ModelTextures] failed to register relic big icon: modelEntry is empty.");
				return false;
			}

			if (string.IsNullOrWhiteSpace(resourcePath))
			{
				GD.PrintErr($"[ReForge.ModelTextures] failed to register relic big icon for '{modelEntry}': resourcePath is empty.");
				return false;
			}

			string owner = string.IsNullOrWhiteSpace(logOwner) ? "ReForge.ModelTextures" : logOwner;

			try
			{
				RegisterRelicBigIcon(modelEntry, GetOrCreateCachedRelicBigIconLoader(modelEntry, resourcePath, owner));
				return true;
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[{owner}] failed to register relic big icon for '{modelEntry}'. {ex}");
				return false;
			}
		}

		/// <summary>
		/// 注册任意命名纹理（用于 UI、背景、药水、怪物、Boss 等非模型纹理入口）。
		/// </summary>
		public static bool TryRegisterNamedTextureFromModResource(string textureKey, string resourcePath, string? logOwner = null)
		{
			if (string.IsNullOrWhiteSpace(textureKey))
			{
				GD.PrintErr("[ReForge.ModelTextures] failed to register named texture: textureKey is empty.");
				return false;
			}

			if (string.IsNullOrWhiteSpace(resourcePath))
			{
				GD.PrintErr($"[ReForge.ModelTextures] failed to register named texture '{textureKey}': resourcePath is empty.");
				return false;
			}

			string owner = string.IsNullOrWhiteSpace(logOwner) ? "ReForge.ModelTextures" : logOwner;
			string normalizedKey = textureKey.Trim();

			try
			{
				Func<Texture2D?> loader = GetOrCreateCachedRawTextureLoader(normalizedKey, resourcePath, owner);
				lock (_namedTextureLoaderSync)
				{
					_namedTextureLoaders[normalizedKey] = loader;
				}

				return true;
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[{owner}] failed to register named texture '{normalizedKey}'. {ex}");
				return false;
			}
		}

		public static bool TryGetNamedTexture(string textureKey, out Texture2D texture)
		{
			texture = null!;
			if (string.IsNullOrWhiteSpace(textureKey))
			{
				return false;
			}

			Func<Texture2D?>? loader;
			lock (_namedTextureLoaderSync)
			{
				if (!_namedTextureLoaders.TryGetValue(textureKey.Trim(), out loader))
				{
					return false;
				}
			}

			Texture2D? resolved = loader();
			if (resolved == null)
			{
				return false;
			}

			texture = resolved;
			return true;
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

		public static bool TryRegisterPowerIconFromModResource(string modelEntry, string resourcePath, string? logOwner = null)
		{
			if (string.IsNullOrWhiteSpace(modelEntry))
			{
				GD.PrintErr("[ReForge.ModelTextures] failed to register power icon: modelEntry is empty.");
				return false;
			}

			if (string.IsNullOrWhiteSpace(resourcePath))
			{
				GD.PrintErr($"[ReForge.ModelTextures] failed to register power icon for '{modelEntry}': resourcePath is empty.");
				return false;
			}

			string owner = string.IsNullOrWhiteSpace(logOwner) ? "ReForge.ModelTextures" : logOwner;

			try
			{
				RegisterPowerIcon(modelEntry, GetOrCreateCachedPowerIconLoader(modelEntry, resourcePath, owner));
				return true;
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[{owner}] failed to register power icon for '{modelEntry}'. {ex}");
				return false;
			}
		}

		public static bool TryRegisterPowerBigIconFromModResource(string modelEntry, string resourcePath, string? logOwner = null)
		{
			if (string.IsNullOrWhiteSpace(modelEntry))
			{
				GD.PrintErr("[ReForge.ModelTextures] failed to register power big icon: modelEntry is empty.");
				return false;
			}

			if (string.IsNullOrWhiteSpace(resourcePath))
			{
				GD.PrintErr($"[ReForge.ModelTextures] failed to register power big icon for '{modelEntry}': resourcePath is empty.");
				return false;
			}

			string owner = string.IsNullOrWhiteSpace(logOwner) ? "ReForge.ModelTextures" : logOwner;

			try
			{
				RegisterPowerBigIcon(modelEntry, GetOrCreateCachedPowerBigIconLoader(modelEntry, resourcePath, owner));
				return true;
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[{owner}] failed to register power big icon for '{modelEntry}'. {ex}");
				return false;
			}
		}

		private static Func<PowerModel, Texture2D?> GetOrCreateCachedPowerIconLoader(string modelEntry, string resourcePath, string owner)
		{
			string cacheKey = $"{modelEntry}|{resourcePath}";
			lock (_powerIconLoaderCacheSync)
			{
				if (_powerIconLoaderCache.TryGetValue(cacheKey, out Func<PowerModel, Texture2D?>? existing))
				{
					return existing;
				}

				object textureSync = new();
				Texture2D? cachedTexture = null;
				Func<PowerModel, Texture2D?> loader = _ =>
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
							GD.PrintErr($"[{owner}] failed to load power icon resource '{resourcePath}' for '{modelEntry}'.");
							return null;
						}

						cachedTexture = loaded;
						return loaded;
					}
				};

				_powerIconLoaderCache[cacheKey] = loader;
				return loader;
			}
		}

		private static Func<PowerModel, Texture2D?> GetOrCreateCachedPowerBigIconLoader(string modelEntry, string resourcePath, string owner)
		{
			string cacheKey = $"{modelEntry}|{resourcePath}";
			lock (_powerBigIconLoaderCacheSync)
			{
				if (_powerBigIconLoaderCache.TryGetValue(cacheKey, out Func<PowerModel, Texture2D?>? existing))
				{
					return existing;
				}

				object textureSync = new();
				Texture2D? cachedTexture = null;
				Func<PowerModel, Texture2D?> loader = _ =>
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
							GD.PrintErr($"[{owner}] failed to load power big icon resource '{resourcePath}' for '{modelEntry}'.");
							return null;
						}

						cachedTexture = loaded;
						return loaded;
					}
				};

				_powerBigIconLoaderCache[cacheKey] = loader;
				return loader;
			}
		}

		private static Func<RelicModel, Texture2D?> GetOrCreateCachedRelicIconLoader(string modelEntry, string resourcePath, string owner)
		{
			string cacheKey = $"{modelEntry}|{resourcePath}";
			lock (_relicIconLoaderCacheSync)
			{
				if (_relicIconLoaderCache.TryGetValue(cacheKey, out Func<RelicModel, Texture2D?>? existing))
				{
					return existing;
				}

				Func<Texture2D?> rawLoader = GetOrCreateCachedRawTextureLoader(modelEntry, resourcePath, owner);
				Func<RelicModel, Texture2D?> typedLoader = _ => rawLoader();
				_relicIconLoaderCache[cacheKey] = typedLoader;
				return typedLoader;
			}
		}

		private static Func<RelicModel, Texture2D?> GetOrCreateCachedRelicIconOutlineLoader(string modelEntry, string resourcePath, string owner)
		{
			string cacheKey = $"{modelEntry}|{resourcePath}";
			lock (_relicIconOutlineLoaderCacheSync)
			{
				if (_relicIconOutlineLoaderCache.TryGetValue(cacheKey, out Func<RelicModel, Texture2D?>? existing))
				{
					return existing;
				}

				Func<Texture2D?> rawLoader = GetOrCreateCachedRawTextureLoader(modelEntry, resourcePath, owner);
				Func<RelicModel, Texture2D?> typedLoader = _ => rawLoader();
				_relicIconOutlineLoaderCache[cacheKey] = typedLoader;
				return typedLoader;
			}
		}

		private static Func<RelicModel, Texture2D?> GetOrCreateCachedRelicBigIconLoader(string modelEntry, string resourcePath, string owner)
		{
			string cacheKey = $"{modelEntry}|{resourcePath}";
			lock (_relicBigIconLoaderCacheSync)
			{
				if (_relicBigIconLoaderCache.TryGetValue(cacheKey, out Func<RelicModel, Texture2D?>? existing))
				{
					return existing;
				}

				Func<Texture2D?> rawLoader = GetOrCreateCachedRawTextureLoader(modelEntry, resourcePath, owner);
				Func<RelicModel, Texture2D?> typedLoader = _ => rawLoader();
				_relicBigIconLoaderCache[cacheKey] = typedLoader;
				return typedLoader;
			}
		}

		private static Func<Texture2D?> GetOrCreateCachedRawTextureLoader(string key, string resourcePath, string owner)
		{
			string cacheKey = $"{key}|{resourcePath}";
			lock (_rawTextureLoaderCacheSync)
			{
				if (_rawTextureLoaderCache.TryGetValue(cacheKey, out Func<Texture2D?>? existing))
				{
					return existing;
				}

				object textureSync = new();
				Texture2D? cachedTexture = null;
				Func<Texture2D?> loader = () =>
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
							GD.PrintErr($"[{owner}] failed to load texture resource '{resourcePath}' for '{key}'.");
							return null;
						}

						cachedTexture = loaded;
						return loaded;
					}
				};

				_rawTextureLoaderCache[cacheKey] = loader;
				return loader;
			}
		}
	}
}
