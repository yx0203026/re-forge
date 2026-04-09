using Godot;

namespace ReForgeFramework.UI.Abstractions;

/// <summary>
/// UI 元素抽象接口，约定可构建为 Godot 控件实例。
/// </summary>
public interface IUiElement
{
	/// <summary>
	/// 构建并返回对应的 Godot 控件。
	/// </summary>
	/// <returns>构建后的控件实例。</returns>
	Control Build();
}
