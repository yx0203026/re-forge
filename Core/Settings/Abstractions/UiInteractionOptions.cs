#nullable enable

using System;
using System.Collections.Generic;
using Godot;

namespace ReForgeFramework.Settings.Abstractions;

/// <summary>
/// 缁熶竴鏀跺彛 UI 浜や簰浜嬩欢锛屼究浜庡湪 UiElement 鍩虹被涓€娆℃€х粦瀹氥€?
/// </summary>
public sealed class UiInteractionOptions
{
	/// <summary>
	/// 榧犳爣杩涘叆鎺т欢鍖哄煙鏃惰Е鍙戠殑澶勭悊鍣ㄩ泦鍚堛€?
	/// </summary>
	public List<Action<Control>> HoverEnterHandlers { get; } = new();

	/// <summary>
	/// 榧犳爣绂诲紑鎺т欢鍖哄煙鏃惰Е鍙戠殑澶勭悊鍣ㄩ泦鍚堛€?
	/// </summary>
	public List<Action<Control>> HoverExitHandlers { get; } = new();

	/// <summary>
	/// 榧犳爣宸﹂敭鎸変笅鏃惰Е鍙戠殑澶勭悊鍣ㄩ泦鍚堛€?
	/// </summary>
	public List<Action<Control, InputEventMouseButton>> LeftMouseDownHandlers { get; } = new();

	/// <summary>
	/// 榧犳爣鍙抽敭鎸変笅鏃惰Е鍙戠殑澶勭悊鍣ㄩ泦鍚堛€?
	/// </summary>
	public List<Action<Control, InputEventMouseButton>> RightMouseDownHandlers { get; } = new();

	/// <summary>
	/// 榧犳爣鎷栨嫿鏃惰Е鍙戠殑澶勭悊鍣ㄩ泦鍚堛€?
	/// </summary>
	public List<Action<Control, InputEventMouseMotion>> DragHandlers { get; } = new();

	/// <summary>
	/// 鏄惁瀛樺湪浠绘剰浜や簰澶勭悊鍣ㄣ€?
	/// </summary>
	public bool HasAnyHandlers =>
		HoverEnterHandlers.Count > 0
		|| HoverExitHandlers.Count > 0
		|| LeftMouseDownHandlers.Count > 0
		|| RightMouseDownHandlers.Count > 0
		|| DragHandlers.Count > 0;
}

