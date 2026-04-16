#nullable enable

using System.Threading.Tasks;
using ReForgeFramework.Api.Ui;

public static partial class ReForge
{
	public static partial class UI
	{
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
	}
}
