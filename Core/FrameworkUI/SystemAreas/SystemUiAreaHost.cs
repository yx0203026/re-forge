using System;
using System.Collections.Generic;
using Godot;
using ReForgeFramework.UI.Abstractions;
using ReForgeFramework.UI.Runtime;

namespace ReForgeFramework.UI.SystemAreas;

public sealed class SystemUiAreaHost
{
	private readonly SystemUiArea _area;
	private readonly List<IUiElement> _elements = new();

	internal SystemUiAreaHost(SystemUiArea area)
	{
		_area = area;
	}

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
