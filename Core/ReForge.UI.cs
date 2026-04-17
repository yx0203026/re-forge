#nullable enable

using System.Threading.Tasks;
using Godot;
using ReForgeFramework.Api.Ui;

public static partial class ReForge
{
	/// <summary>
	/// ReForge UI 工具能力入口。
	/// </summary>
	public static partial class UI
	{
		/// <summary>
		/// 先尝试官方 Ancient CanvasGroup 遮罩，失败后自动回退到本地圆角裁切。
		/// 返回值表示是否成功应用官方遮罩。
		/// </summary>
		public static bool ApplyAncientPortraitMask(TextureRect ancientPortrait, CanvasGroup? portraitCanvasGroup)
		{
			return AncientPortraitMasking.ApplyOfficialOrFallback(ancientPortrait, portraitCanvasGroup);
		}

		/// <summary>
		/// 创建通用“选牌面板”构建器：可通过 AddCard/AddPool 组装候选卡，再 BuildShow 显示。
		/// </summary>
		public static CardSelectionPanelBuilder CreateCardSelectionPanel()
		{
			return new CardSelectionPanelBuilder();
		}

		/// <summary>
		/// 创建“删牌面板”构建器：默认使用牌库删牌模式。
		/// </summary>
		public static CardSelectionPanelBuilder CreateDeckRemovalPanel()
		{
			return new CardSelectionPanelBuilder().UseDeckRemovalMode();
		}

		/// <summary>
		/// 统一的“选卡并授予到牌组”流程入口。
		/// </summary>
		public static Task<CardGrantSelectionResult> SelectAndGrantToDeckAsync(CardGrantSelectionRequest request)
		{
			return CardGrantService.SelectAndGrantToDeckAsync(request);
		}

		/// <summary>
		/// 创建“选择负面 Buff（Debuff）”面板构建器。
		/// 用法：AddDebuff(..., amount) 后调用 ShowAsync / ShowAndApplyAsync。
		/// </summary>
		public static DebuffSelectionPanelBuilder CreateDebuffSelectionPanel()
		{
			return new DebuffSelectionPanelBuilder();
		}
	}
}
