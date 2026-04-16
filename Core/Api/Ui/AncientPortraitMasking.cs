#nullable enable

using Godot;

namespace ReForgeFramework.Api.Ui;

/// <summary>
/// Ancient 立绘遮罩工具：优先复用官方 CanvasGroup 遮罩链路，失败时回退本地圆角裁切。
/// </summary>
public static class AncientPortraitMasking
{
	private const string AncientPortraitMaskMetaKey = "__reforge_ancient_portrait_mask";

	// 官方资源路径与参数（来自 STS2 NCard Ancient 分支）
	private const string OfficialCanvasGroupMaskShaderPath = "res://shaders/blur/canvas_group_mask_blur.gdshader";
	private const string OfficialAncientMaskTexturePath = "res://images/atlases/compressed.sprites/card_template/ancient_portrait_mask_large.tres";
	private static readonly Vector4 OfficialMaskRegion = new(615f, 1151f, 600f, 847f);
	private static readonly Vector2 OfficialMaskOffset = new(10f, 8f);
	private static readonly Vector2 OfficialMaskStep = new(0f, 0f);
	private const float OfficialMaskRadius = 32f;

	private const float FallbackCornerRadius = 0.085f;
	private const float FallbackFeather = 0.008f;
	private static readonly Shader FallbackAncientPortraitMaskShader = CreateFallbackAncientPortraitMaskShader();

	public static void ConfigureAncientPortraitNode(TextureRect ancientPortrait)
	{
		ancientPortrait.StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered;
		ancientPortrait.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		ancientPortrait.ClipContents = true;
		ancientPortrait.UseParentMaterial = false;
	}

	public static bool TryApplyOfficialCanvasGroupMask(CanvasGroup? portraitCanvasGroup)
	{
		if (portraitCanvasGroup == null)
		{
			return false;
		}

		Shader? shader = GD.Load<Shader>(OfficialCanvasGroupMaskShaderPath);
		Texture2D? maskTexture = GD.Load<Texture2D>(OfficialAncientMaskTexturePath);
		if (shader == null || maskTexture == null)
		{
			return false;
		}

		ShaderMaterial material = new()
		{
			Shader = shader
		};

		material.SetShaderParameter("mask_texture", maskTexture);
		material.SetShaderParameter("mask_region", OfficialMaskRegion);
		material.SetShaderParameter("mask_offset", OfficialMaskOffset);
		material.SetShaderParameter("step", OfficialMaskStep);
		material.SetShaderParameter("radius", OfficialMaskRadius);
		material.SetShaderParameter("mask", true);
		material.SetShaderParameter("blur", false);

		portraitCanvasGroup.Material = material;
		return true;
	}

	public static void ClearPortraitFallbackMaterial(TextureRect ancientPortrait)
	{
		ancientPortrait.Material = null;
		ancientPortrait.SetMeta(AncientPortraitMaskMetaKey, false);
	}

	public static void ApplyPortraitClipFallback(TextureRect ancientPortrait)
	{
		ancientPortrait.UseParentMaterial = false;

		if (ancientPortrait.GetMeta(AncientPortraitMaskMetaKey, false).AsBool())
		{
			return;
		}

		ShaderMaterial material = new()
		{
			Shader = FallbackAncientPortraitMaskShader
		};
		material.SetShaderParameter("corner_radius", FallbackCornerRadius);
		material.SetShaderParameter("feather", FallbackFeather);

		ancientPortrait.Material = material;
		ancientPortrait.SetMeta(AncientPortraitMaskMetaKey, true);
	}

	public static bool ApplyOfficialOrFallback(TextureRect ancientPortrait, CanvasGroup? portraitCanvasGroup)
	{
		ConfigureAncientPortraitNode(ancientPortrait);
		if (TryApplyOfficialCanvasGroupMask(portraitCanvasGroup))
		{
			ClearPortraitFallbackMaterial(ancientPortrait);
			return true;
		}

		ApplyPortraitClipFallback(ancientPortrait);
		return false;
	}

	private static Shader CreateFallbackAncientPortraitMaskShader()
	{
		Shader shader = new();
		shader.Code = @"shader_type canvas_item;

uniform float corner_radius = 0.085;
uniform float feather = 0.008;

void fragment() {
	vec2 p = UV - vec2(0.5);
	vec2 half_size = vec2(0.5);
	vec2 inner = half_size - vec2(corner_radius);
	vec2 q = abs(p) - inner;
	float dist = length(max(q, vec2(0.0))) - corner_radius;
	float alpha = 1.0 - smoothstep(0.0, feather, dist);
	vec4 color = texture(TEXTURE, UV);
	color.a *= alpha;
	COLOR = color;
}";
		return shader;
	}
}
