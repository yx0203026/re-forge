#nullable enable

using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using ReForgeFramework.ModResources.Models;

namespace ReForgeFramework.ModResources.Patches;

/// <summary>
/// 官方模型纹理 getter 兜底补丁：
/// 在原 getter 执行后，若 ReForge 注册了自定义纹理则覆盖返回值。
/// </summary>
[HarmonyPatch]
public static class OfficialModelTextureFallbackPatches
{
	#region 卡牌

	[HarmonyPatch(typeof(CardModel), "get_Portrait")]
	[HarmonyPostfix]
	private static void CardPortraitPostfix(CardModel __instance, ref Texture2D __result)
	{
		if (TryResolveCardPortrait(__instance, out Texture2D texture))
		{
			__result = texture;
		}
	}

	#endregion

	#region 遗物

	[HarmonyPatch(typeof(RelicModel), "get_Icon")]
	[HarmonyPostfix]
	private static void RelicIconPostfix(RelicModel __instance, ref Texture2D __result)
	{
		if (TryResolveRelicIcon(__instance, out Texture2D texture))
		{
			__result = texture;
		}
	}

	[HarmonyPatch(typeof(RelicModel), "get_IconOutline")]
	[HarmonyPostfix]
	private static void RelicIconOutlinePostfix(RelicModel __instance, ref Texture2D __result)
	{
		if (TryResolveRelicIconOutline(__instance, out Texture2D texture))
		{
			__result = texture;
		}
	}

	[HarmonyPatch(typeof(RelicModel), "get_BigIcon")]
	[HarmonyPostfix]
	private static void RelicBigIconPostfix(RelicModel __instance, ref Texture2D __result)
	{
		if (TryResolveRelicBigIcon(__instance, out Texture2D texture))
		{
			__result = texture;
		}
	}

	#endregion

	private static bool TryResolveCardPortrait(CardModel card, out Texture2D texture)
	{
		texture = null!;

		if (card is IReForgeCardTextureProvider provider
			&& provider.TryGetPortraitTexture(out texture))
		{
			return true;
		}

		if (card is IReForgeTextureModelIdProvider idProvider
			&& ModelTextureRegistry.TryResolveCardPortrait(idProvider.TextureModelId, card, out texture))
		{
			return true;
		}

		return ModelTextureRegistry.TryResolveCardPortrait(card, out texture);
	}

	private static bool TryResolveRelicIcon(RelicModel relic, out Texture2D texture)
	{
		texture = null!;

		if (relic is IReForgeRelicTextureProvider provider
			&& provider.TryGetIconTexture(out texture))
		{
			return true;
		}

		if (relic is IReForgeTextureModelIdProvider idProvider
			&& ModelTextureRegistry.TryResolveRelicIcon(idProvider.TextureModelId, relic, out texture))
		{
			return true;
		}

		return ModelTextureRegistry.TryResolveRelicIcon(relic, out texture);
	}

	private static bool TryResolveRelicIconOutline(RelicModel relic, out Texture2D texture)
	{
		texture = null!;

		if (relic is IReForgeRelicTextureProvider provider
			&& provider.TryGetIconOutlineTexture(out texture))
		{
			return true;
		}

		if (relic is IReForgeTextureModelIdProvider idProvider
			&& ModelTextureRegistry.TryResolveRelicIconOutline(idProvider.TextureModelId, relic, out texture))
		{
			return true;
		}

		return ModelTextureRegistry.TryResolveRelicIconOutline(relic, out texture);
	}

	private static bool TryResolveRelicBigIcon(RelicModel relic, out Texture2D texture)
	{
		texture = null!;

		if (relic is IReForgeRelicTextureProvider provider
			&& provider.TryGetBigIconTexture(out texture))
		{
			return true;
		}

		if (relic is IReForgeTextureModelIdProvider idProvider
			&& ModelTextureRegistry.TryResolveRelicBigIcon(idProvider.TextureModelId, relic, out texture))
		{
			return true;
		}

		return ModelTextureRegistry.TryResolveRelicBigIcon(relic, out texture);
	}
}
