namespace ReForgeFramework.Settings.Abstractions;

/// <summary>
/// UI 鍙鎬т綔鐢ㄥ煙锛氱敤浜庡０鏄庢帶浠跺湪涓嶅悓瀹樻柟鐣岄潰鐘舵€佷笅鐨勬樉闅愯鍒欍€?
/// </summary>
public enum UiVisibilityScope
{
	/// <summary>
	/// 濮嬬粓鍙锛堥粯璁ゅ€硷級銆?
	/// </summary>
	Always = 0,

	/// <summary>
	/// 涓昏彍鍗曞彲瑙佹湡闂存樉绀猴紙鍖呭惈瀛愯彍鍗曪紝濡傚崟浜?璁剧疆鍏ュ彛锛夈€?
	/// </summary>
	MainMenuOnly = 1,

	/// <summary>
	/// 浠呬富鑿滃崟棣栭〉鏄剧ず锛堝綋浠绘剰瀛愯彍鍗曞彲瑙佹椂闅愯棌锛夈€?
	/// </summary>
	MainMenuHomeOnly = 2,

	/// <summary>
	/// 浠呰缃〉闈㈡樉绀恒€?
	/// </summary>
	SettingsOnly = 3,

	/// <summary>
	/// 浠呬富鑿滃崟瀛愯彍鍗曟樉绀猴紙渚嬪鍗曚汉銆佽缃€佹椂闂寸嚎绛夊瓙椤甸潰锛夈€?
	/// </summary>
	MainMenuSubmenuOnly = 4,

	/// <summary>
	/// 浠呭湪 Run 涓诲満鏅樉绀恒€?
	/// </summary>
	RunOnly = 10,

	/// <summary>
	/// 浠呭湴鍥剧晫闈㈡墦寮€鏃舵樉绀恒€?
	/// </summary>
	RunMapOnly = 11,

	/// <summary>
	/// 浠?Overlay 鏍堟湁鎵撳紑鐣岄潰鏃舵樉绀恒€?
	/// </summary>
	RunOverlayOnly = 12,

	/// <summary>
	/// 浠?Capstone 鐣岄潰鎵撳紑鏃舵樉绀恒€?
	/// </summary>
	RunCapstoneOnly = 13,

	/// <summary>
	/// 浠呮ā鎬佸脊绐楁墦寮€鏃舵樉绀恒€?
	/// </summary>
	ModalOnly = 14,

	/// <summary>
	/// 浠呬簨浠舵埧闂存樉绀恒€?
	/// </summary>
	RunEventRoomOnly = 20,

	/// <summary>
	/// 浠呮垬鏂楁埧闂存樉绀恒€?
	/// </summary>
	RunCombatRoomOnly = 21,

	/// <summary>
	/// 浠呭晢搴楁埧闂存樉绀恒€?
	/// </summary>
	RunMerchantRoomOnly = 22,

	/// <summary>
	/// 浠呬紤鎭埧闂存樉绀恒€?
	/// </summary>
	RunRestSiteRoomOnly = 23,

	/// <summary>
	/// 浠呭疂绠辨埧闂存樉绀恒€?
	/// </summary>
	RunTreasureRoomOnly = 24,

	/// <summary>
	/// 浠呭湴鍥炬埧闂达紙杩涘叆鑺傜偣鍓嶅悗锛夋樉绀恒€?
	/// </summary>
	RunMapRoomOnly = 25
}
