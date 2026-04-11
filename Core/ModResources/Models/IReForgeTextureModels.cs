#nullable enable

using Godot;
using MegaCrit.Sts2.Core.Models;

namespace ReForgeFramework.ModResources.Models;

/// <summary>
/// 提供可选的纹理模型 ID（用于纹理注册中心 key 匹配）。
/// </summary>
public interface IReForgeTextureModelIdProvider
{
	ModelId TextureModelId { get; }
}

/// <summary>
/// 可选卡牌纹理提供接口。
/// </summary>
public interface IReForgeCardTextureProvider
{
	bool TryGetPortraitTexture(out Texture2D texture);
}

/// <summary>
/// 可选遗物纹理提供接口。
/// </summary>
public interface IReForgeRelicTextureProvider
{
	bool TryGetIconTexture(out Texture2D texture);

	bool TryGetIconOutlineTexture(out Texture2D texture);

	bool TryGetBigIconTexture(out Texture2D texture);
}
