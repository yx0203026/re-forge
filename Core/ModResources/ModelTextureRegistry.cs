#nullable enable

using System;
using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Models;

namespace ReForgeFramework.ModResources;

/// <summary>
/// ReForge 纹理注册中心：
/// 允许外部模组按模型 ID 注册卡牌/遗物纹理加载委托，
/// 再由 Harmony 补丁在官方 getter 执行后做结果兜底覆盖。
/// </summary>
public static class ModelTextureRegistry
{
	private static readonly object SyncRoot = new();

	private static readonly Dictionary<string, Func<CardModel, Texture2D?>> CardPortraitLoaders = new(StringComparer.OrdinalIgnoreCase);
	private static readonly Dictionary<string, Func<RelicModel, Texture2D?>> RelicIconLoaders = new(StringComparer.OrdinalIgnoreCase);
	private static readonly Dictionary<string, Func<RelicModel, Texture2D?>> RelicIconOutlineLoaders = new(StringComparer.OrdinalIgnoreCase);
	private static readonly Dictionary<string, Func<RelicModel, Texture2D?>> RelicBigIconLoaders = new(StringComparer.OrdinalIgnoreCase);

	#region 注册 API

	public static void RegisterCardPortrait(ModelId modelId, Func<CardModel, Texture2D?> loader)
	{
		ArgumentNullException.ThrowIfNull(modelId);
		ArgumentNullException.ThrowIfNull(loader);
		RegisterLoader(CardPortraitLoaders, modelId, loader);
	}

	public static void RegisterCardPortrait(string modelEntry, Func<CardModel, Texture2D?> loader)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(modelEntry);
		ArgumentNullException.ThrowIfNull(loader);
		RegisterLoader(CardPortraitLoaders, modelEntry, loader);
	}

	public static void RegisterRelicIcon(ModelId modelId, Func<RelicModel, Texture2D?> loader)
	{
		ArgumentNullException.ThrowIfNull(modelId);
		ArgumentNullException.ThrowIfNull(loader);
		RegisterLoader(RelicIconLoaders, modelId, loader);
	}

	public static void RegisterRelicIcon(string modelEntry, Func<RelicModel, Texture2D?> loader)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(modelEntry);
		ArgumentNullException.ThrowIfNull(loader);
		RegisterLoader(RelicIconLoaders, modelEntry, loader);
	}

	public static void RegisterRelicIconOutline(ModelId modelId, Func<RelicModel, Texture2D?> loader)
	{
		ArgumentNullException.ThrowIfNull(modelId);
		ArgumentNullException.ThrowIfNull(loader);
		RegisterLoader(RelicIconOutlineLoaders, modelId, loader);
	}

	public static void RegisterRelicIconOutline(string modelEntry, Func<RelicModel, Texture2D?> loader)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(modelEntry);
		ArgumentNullException.ThrowIfNull(loader);
		RegisterLoader(RelicIconOutlineLoaders, modelEntry, loader);
	}

	public static void RegisterRelicBigIcon(ModelId modelId, Func<RelicModel, Texture2D?> loader)
	{
		ArgumentNullException.ThrowIfNull(modelId);
		ArgumentNullException.ThrowIfNull(loader);
		RegisterLoader(RelicBigIconLoaders, modelId, loader);
	}

	public static void RegisterRelicBigIcon(string modelEntry, Func<RelicModel, Texture2D?> loader)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(modelEntry);
		ArgumentNullException.ThrowIfNull(loader);
		RegisterLoader(RelicBigIconLoaders, modelEntry, loader);
	}

	#endregion

	#region 查询 API

	public static bool TryResolveCardPortrait(CardModel card, out Texture2D texture)
	{
		ArgumentNullException.ThrowIfNull(card);
		return TryResolveTexture(CardPortraitLoaders, card.Id, card, out texture);
	}

	public static bool TryResolveCardPortrait(ModelId modelId, CardModel card, out Texture2D texture)
	{
		ArgumentNullException.ThrowIfNull(modelId);
		ArgumentNullException.ThrowIfNull(card);
		return TryResolveTexture(CardPortraitLoaders, modelId, card, out texture)
			|| TryResolveTexture(CardPortraitLoaders, card.Id, card, out texture);
	}

	public static bool TryResolveRelicIcon(RelicModel relic, out Texture2D texture)
	{
		ArgumentNullException.ThrowIfNull(relic);
		return TryResolveTexture(RelicIconLoaders, relic.Id, relic, out texture);
	}

	public static bool TryResolveRelicIcon(ModelId modelId, RelicModel relic, out Texture2D texture)
	{
		ArgumentNullException.ThrowIfNull(modelId);
		ArgumentNullException.ThrowIfNull(relic);
		return TryResolveTexture(RelicIconLoaders, modelId, relic, out texture)
			|| TryResolveTexture(RelicIconLoaders, relic.Id, relic, out texture);
	}

	public static bool TryResolveRelicIconOutline(RelicModel relic, out Texture2D texture)
	{
		ArgumentNullException.ThrowIfNull(relic);
		return TryResolveTexture(RelicIconOutlineLoaders, relic.Id, relic, out texture);
	}

	public static bool TryResolveRelicIconOutline(ModelId modelId, RelicModel relic, out Texture2D texture)
	{
		ArgumentNullException.ThrowIfNull(modelId);
		ArgumentNullException.ThrowIfNull(relic);
		return TryResolveTexture(RelicIconOutlineLoaders, modelId, relic, out texture)
			|| TryResolveTexture(RelicIconOutlineLoaders, relic.Id, relic, out texture);
	}

	public static bool TryResolveRelicBigIcon(RelicModel relic, out Texture2D texture)
	{
		ArgumentNullException.ThrowIfNull(relic);
		return TryResolveTexture(RelicBigIconLoaders, relic.Id, relic, out texture);
	}

	public static bool TryResolveRelicBigIcon(ModelId modelId, RelicModel relic, out Texture2D texture)
	{
		ArgumentNullException.ThrowIfNull(modelId);
		ArgumentNullException.ThrowIfNull(relic);
		return TryResolveTexture(RelicBigIconLoaders, modelId, relic, out texture)
			|| TryResolveTexture(RelicBigIconLoaders, relic.Id, relic, out texture);
	}

	#endregion

	private static void RegisterLoader<TModel>(
		Dictionary<string, Func<TModel, Texture2D?>> registry,
		ModelId modelId,
		Func<TModel, Texture2D?> loader)
		where TModel : AbstractModel
	{
		lock (SyncRoot)
		{
			registry[BuildModelKey(modelId)] = loader;
			registry[BuildEntryKey(modelId.Entry)] = loader;
		}
	}

	private static void RegisterLoader<TModel>(
		Dictionary<string, Func<TModel, Texture2D?>> registry,
		string modelEntry,
		Func<TModel, Texture2D?> loader)
		where TModel : AbstractModel
	{
		lock (SyncRoot)
		{
			registry[BuildEntryKey(modelEntry)] = loader;
		}
	}

	private static bool TryResolveTexture<TModel>(
		Dictionary<string, Func<TModel, Texture2D?>> registry,
		ModelId modelId,
		TModel model,
		out Texture2D texture)
		where TModel : AbstractModel
	{
		texture = null!;

		Func<TModel, Texture2D?>? loader;
		lock (SyncRoot)
		{
			if (!registry.TryGetValue(BuildModelKey(modelId), out loader)
				&& !registry.TryGetValue(BuildEntryKey(modelId.Entry), out loader))
			{
				return false;
			}
		}

		Texture2D? resolved = null;
		try
		{
			resolved = loader.Invoke(model);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[ReForge.ModelTextureRegistry] Loader threw for model '{modelId}'. {ex}");
			return false;
		}

		if (resolved == null)
		{
			return false;
		}

		texture = resolved;
		return true;
	}

	private static string BuildModelKey(ModelId modelId)
	{
		return string.Concat(modelId.Category, ".", modelId.Entry).ToUpperInvariant();
	}

	private static string BuildEntryKey(string entry)
	{
		return entry.Trim().ToUpperInvariant();
	}
}
