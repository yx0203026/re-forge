#nullable enable

using System;
using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;

namespace ReForgeFramework.ModResources.Models;

/// <summary>
/// ReForge 卡牌基础模型：
/// 1. 构造时可选传入自定义 ModelId；
/// 2. 构造时可选传入纹理加载委托；
/// 3. 与官方 CardModel 保持兼容，纹理返回由补丁统一接管。
/// </summary>
public abstract class ReForgeCard : CardModel, IReForgeTextureModelIdProvider, IReForgeCardTextureProvider
{
	private readonly Func<CardModel, Texture2D?>? _portraitTextureLoader;

	public ModelId TextureModelId { get; }

	protected ReForgeCard(
		int canonicalEnergyCost,
		CardType type,
		CardRarity rarity,
		TargetType targetType,
		ModelId? customModelId = null,
		Func<CardModel, Texture2D?>? portraitTextureLoader = null,
		bool shouldShowInCardLibrary = true)
		: base(canonicalEnergyCost, type, rarity, targetType, shouldShowInCardLibrary)
	{
		_portraitTextureLoader = portraitTextureLoader;
		TextureModelId = customModelId ?? Id;

		if (customModelId != null && customModelId != Id)
		{
			ModelIdOverrideHelper.TryOverride(this, customModelId);
		}
	}

	protected ReForgeCard(
		int canonicalEnergyCost,
		CardType type,
		CardRarity rarity,
		TargetType targetType,
		string customModelEntry,
		Func<CardModel, Texture2D?>? portraitTextureLoader = null,
		bool shouldShowInCardLibrary = true)
		: this(
			canonicalEnergyCost,
			type,
			rarity,
			targetType,
			BuildCustomId(customModelEntry),
			portraitTextureLoader,
			shouldShowInCardLibrary)
	{
	}

	public bool TryGetPortraitTexture(out Texture2D texture)
	{
		texture = null!;
		if (_portraitTextureLoader == null)
		{
			return false;
		}

		Texture2D? loaded;
		try
		{
			loaded = _portraitTextureLoader.Invoke(this);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[ReForge.ReForgeCard] Portrait loader failed for '{TextureModelId}'. {ex}");
			return false;
		}

		if (loaded == null)
		{
			return false;
		}

		texture = loaded;
		return true;
	}

	private static ModelId BuildCustomId(string customModelEntry)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(customModelEntry);
		return new ModelId("CARDS", customModelEntry.Trim());
	}
}
