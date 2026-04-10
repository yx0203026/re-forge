using Godot;

namespace ReForgeFramework.Settings.Abstractions;

/// <summary>
/// UI 鍏冪礌鎶借薄鎺ュ彛锛岀害瀹氬彲鏋勫缓涓?Godot 鎺т欢瀹炰緥銆?
/// </summary>
public interface IUiElement
{
	/// <summary>
	/// 鏋勫缓骞惰繑鍥炲搴旂殑 Godot 鎺т欢銆?
	/// </summary>
	/// <returns>鏋勫缓鍚庣殑鎺т欢瀹炰緥銆?/returns>
	Control Build();
}

