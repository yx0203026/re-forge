#nullable enable

using Godot;
using ReForgeFramework.Api.Ui;

public static partial class ReForge
{
	/// <summary>
	/// ReForge UI 工具能力入口。
	/// </summary>
	public static class UI
	{
		/// <summary>
		/// 先尝试官方 Ancient CanvasGroup 遮罩，失败后自动回退到本地圆角裁切。
		/// 返回值表示是否成功应用官方遮罩。
		/// </summary>
		public static bool ApplyAncientPortraitMask(TextureRect ancientPortrait, CanvasGroup? portraitCanvasGroup)
		{
			return AncientPortraitMasking.ApplyOfficialOrFallback(ancientPortrait, portraitCanvasGroup);
		}
	}
}
