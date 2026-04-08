namespace ReForgeFramework.UI.Abstractions;

/// <summary>
/// UI 可见性作用域：用于声明控件在不同官方界面状态下的显隐规则。
/// </summary>
public enum UiVisibilityScope
{
	/// <summary>
	/// 始终可见（默认值）。
	/// </summary>
	Always = 0,

	/// <summary>
	/// 主菜单可见期间显示（包含子菜单，如单人/设置入口）。
	/// </summary>
	MainMenuOnly = 1,

	/// <summary>
	/// 仅主菜单首页显示（当任意子菜单可见时隐藏）。
	/// </summary>
	MainMenuHomeOnly = 2,

	/// <summary>
	/// 仅设置页面显示。
	/// </summary>
	SettingsOnly = 3,

	/// <summary>
	/// 仅主菜单子菜单显示（例如单人、设置、时间线等子页面）。
	/// </summary>
	MainMenuSubmenuOnly = 4,

	/// <summary>
	/// 仅在 Run 主场景显示。
	/// </summary>
	RunOnly = 10,

	/// <summary>
	/// 仅地图界面打开时显示。
	/// </summary>
	RunMapOnly = 11,

	/// <summary>
	/// 仅 Overlay 栈有打开界面时显示。
	/// </summary>
	RunOverlayOnly = 12,

	/// <summary>
	/// 仅 Capstone 界面打开时显示。
	/// </summary>
	RunCapstoneOnly = 13,

	/// <summary>
	/// 仅模态弹窗打开时显示。
	/// </summary>
	ModalOnly = 14,

	/// <summary>
	/// 仅事件房间显示。
	/// </summary>
	RunEventRoomOnly = 20,

	/// <summary>
	/// 仅战斗房间显示。
	/// </summary>
	RunCombatRoomOnly = 21,

	/// <summary>
	/// 仅商店房间显示。
	/// </summary>
	RunMerchantRoomOnly = 22,

	/// <summary>
	/// 仅休息房间显示。
	/// </summary>
	RunRestSiteRoomOnly = 23,

	/// <summary>
	/// 仅宝箱房间显示。
	/// </summary>
	RunTreasureRoomOnly = 24,

	/// <summary>
	/// 仅地图房间（进入节点前后）显示。
	/// </summary>
	RunMapRoomOnly = 25
}