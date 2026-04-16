#nullable enable

using System;
using Godot;
using MegaCrit.Sts2.Core.Models;

namespace ReForgeFramework.ModResources.Models;

/// <summary>
/// ReForge Power 基础模型：
/// 1. 构造时可选传入自定义 ModelId；
/// 2. 构造时可选传入小图/大图纹理加载委托；
/// 3. 与官方 PowerModel 保持兼容，纹理返回由补丁统一接管。
/// </summary>
public abstract class ReForgePower : PowerModel, IReForgeTextureModelIdProvider, IReForgePowerTextureProvider
{
	private readonly Func<PowerModel, Texture2D?>? _iconTextureLoader;
	private readonly Func<PowerModel, Texture2D?>? _bigIconTextureLoader;

	public ModelId TextureModelId { get; }

	protected ReForgePower(
		ModelId? customModelId = null,
		Func<PowerModel, Texture2D?>? iconTextureLoader = null,
		Func<PowerModel, Texture2D?>? bigIconTextureLoader = null)
	{
		_iconTextureLoader = iconTextureLoader;
		_bigIconTextureLoader = bigIconTextureLoader;
		TextureModelId = customModelId ?? Id;

		if (customModelId != null && customModelId != Id)
		{
			ModelIdOverrideHelper.TryOverride(this, customModelId);
		}
	}

	protected ReForgePower(
		string customModelEntry,
		Func<PowerModel, Texture2D?>? iconTextureLoader = null,
		Func<PowerModel, Texture2D?>? bigIconTextureLoader = null)
		: this(
			BuildCustomId(customModelEntry),
			iconTextureLoader,
			bigIconTextureLoader)
	{
	}

	public bool TryGetIconTexture(out Texture2D texture)
	{
		return TryInvokeLoader(_iconTextureLoader, out texture, "Icon");
	}

	public bool TryGetBigIconTexture(out Texture2D texture)
	{
		return TryInvokeLoader(_bigIconTextureLoader, out texture, "BigIcon");
	}

	private bool TryInvokeLoader(
		Func<PowerModel, Texture2D?>? loader,
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
			GD.PrintErr($"[ReForge.ReForgePower] {textureKind} loader failed for '{TextureModelId}'. {ex}");
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
		return new ModelId("POWERS", customModelEntry.Trim());
	}
}
