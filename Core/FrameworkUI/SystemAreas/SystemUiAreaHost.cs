using System;
using System.Collections.Generic;
using Godot;
using ReForgeFramework.UI.Abstractions;
using ReForgeFramework.UI.Runtime;

namespace ReForgeFramework.UI.SystemAreas;

/// <summary>
/// 系统 UI 区域宿主，负责缓存元素并执行区域挂载。
/// </summary>
public class SystemUiAreaHost
{
	private readonly SystemUiArea _area;
	private readonly List<IUiElement> _elements = new();

	internal SystemUiAreaHost(SystemUiArea area)
	{
		_area = area;
	}

	/// <summary>
	/// 向当前系统区域添加元素并立即尝试挂载。
	/// </summary>
	/// <param name="element">待添加元素。</param>
	public void AddChild(IUiElement element)
	{
		ArgumentNullException.ThrowIfNull(element);

		if (!_elements.Contains(element))
		{
			_elements.Add(element);
		}

		MountElement(element);
	}

	internal void RemountAll()
	{
		foreach (IUiElement element in _elements)
		{
			MountElement(element);
		}
	}

	private void MountElement(IUiElement element)
	{
		Control built = element.Build();
		if (!GodotObject.IsInstanceValid(built))
		{
			return;
		}

		UiRuntimeNode.Ensure().MountToArea(_area, built);
	}
}
