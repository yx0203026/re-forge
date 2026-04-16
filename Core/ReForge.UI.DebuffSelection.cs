#nullable enable

using ReForgeFramework.Api.Ui;

public static partial class ReForge
{
	public static partial class UI
	{
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
