#nullable enable

using System;
using Godot;
using MegaCrit.Sts2.Core.Models;

namespace ReForgeFramework.ModResources.Models;

/// <summary>
/// ReForge 遗物基础模型：
/// 1. 构造时可选传入自定义 ModelId；
/// 2. 构造时可选传入图标/描边/大图纹理加载委托；
/// 3. 由 Harmony 纹理补丁接管 getter 返回。
/// </summary>
public abstract class ReForgeRelic : RelicModel, IReForgeTextureModelIdProvider, IReForgeRelicTextureProvider
{
	private readonly Func<RelicModel, Texture2D?>? _iconTextureLoader;
	private readonly Func<RelicModel, Texture2D?>? _iconOutlineTextureLoader;
	private readonly Func<RelicModel, Texture2D?>? _bigIconTextureLoader;

	public ModelId TextureModelId { get; }

	protected ReForgeRelic(
		ModelId? customModelId = null,
		Func<RelicModel, Texture2D?>? iconTextureLoader = null,
		Func<RelicModel, Texture2D?>? iconOutlineTextureLoader = null,
		Func<RelicModel, Texture2D?>? bigIconTextureLoader = null)
	{
		_iconTextureLoader = iconTextureLoader;
		_iconOutlineTextureLoader = iconOutlineTextureLoader;
		_bigIconTextureLoader = bigIconTextureLoader;
		TextureModelId = customModelId ?? Id;

		if (customModelId != null && customModelId != Id)
		{
			ModelIdOverrideHelper.TryOverride(this, customModelId);
		}
	}

	protected ReForgeRelic(
		string customModelEntry,
		Func<RelicModel, Texture2D?>? iconTextureLoader = null,
		Func<RelicModel, Texture2D?>? iconOutlineTextureLoader = null,
		Func<RelicModel, Texture2D?>? bigIconTextureLoader = null)
		: this(
			BuildCustomId(customModelEntry),
			iconTextureLoader,
			iconOutlineTextureLoader,
			bigIconTextureLoader)
	{
	}

	public bool TryGetIconTexture(out Texture2D texture)
	{
		return TryInvokeLoader(_iconTextureLoader, out texture, "Icon");
	}

	public bool TryGetIconOutlineTexture(out Texture2D texture)
	{
		return TryInvokeLoader(_iconOutlineTextureLoader, out texture, "IconOutline");
	}

	public bool TryGetBigIconTexture(out Texture2D texture)
	{
		return TryInvokeLoader(_bigIconTextureLoader, out texture, "BigIcon");
	}

	private bool TryInvokeLoader(
		Func<RelicModel, Texture2D?>? loader,
		out Texture2D texture,
		string textureKind)
	{
		texture = null!;
		if (loader == null)
		{
			return false;
		}

		Texture2D? loaded;
		try
		{
			loaded = loader.Invoke(this);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[ReForge.ReForgeRelic] {textureKind} loader failed for '{TextureModelId}'. {ex}");
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
		return new ModelId("RELICS", customModelEntry.Trim());
	}
}
