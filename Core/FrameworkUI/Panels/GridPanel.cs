#nullable enable

using System;
using System.Collections.Generic;
using Godot;
using ReForgeFramework.UI.Abstractions;

namespace ReForgeFramework.UI.Panels;

public class GridPanel : UiPanel
{
	private sealed record GridEntry(IUiElement Element, int? SlotIndex);

	private const string SpacerPrefix = "ReForgeGridSpacer";

	private readonly int _columns;
	private readonly int _hSeparation;
	private readonly int _vSeparation;
	private readonly List<GridEntry> _entries = new();

	public GridPanel(int columns, int hSeparation = 8, int vSeparation = 8)
	{
		if (columns <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(columns), "columns must be greater than 0.");
		}

		_columns = columns;
		_hSeparation = hSeparation;
		_vSeparation = vSeparation;
	}

	public override UiPanel AddChild(IUiElement child)
	{
		_entries.Add(new GridEntry(child, null));
		base.AddChild(child);
		RebuildIfLive();
		return this;
	}

	public GridPanel AddChildAt(IUiElement child, int row, int column)
	{
		if (row < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(row), "row cannot be less than 0.");
		}

		if (column < 0 || column >= _columns)
		{
			throw new ArgumentOutOfRangeException(nameof(column), "column is out of range.");
		}

		_entries.Add(new GridEntry(child, row * _columns + column));
		base.AddChild(child);
		RebuildIfLive();
		return this;
	}

	protected override Control CreatePanelControl()
	{
		GridContainer container = new GridContainer
		{
			Name = "ReForgeGridPanel",
			Columns = _columns
		};

		container.AddThemeConstantOverride("h_separation", _hSeparation);
		container.AddThemeConstantOverride("v_separation", _vSeparation);
		ApplyEntries(container);
		return container;
	}

	protected override void AddChildToContainer(Control container, Control childControl)
	{
		// GridPanel order is managed centrally by ApplyEntries.
	}

	private void RebuildIfLive()
	{
		if (BuiltControl is GridContainer container)
		{
			ApplyEntries(container);
		}
	}

	private void ApplyEntries(GridContainer container)
	{
		DetachExistingChildren(container);

		foreach (Control control in BuildOrderedControls())
		{
			AttachControl(container, control);
		}
	}

	private static void DetachExistingChildren(GridContainer container)
	{
		foreach (Node child in container.GetChildren())
		{
			container.RemoveChild(child);
			if (child is Control control && control.Name.ToString().StartsWith(SpacerPrefix, StringComparison.Ordinal))
			{
				control.QueueFree();
			}
		}
	}

	private IEnumerable<Control> BuildOrderedControls()
	{
		Queue<IUiElement> dynamicQueue = new();
		Dictionary<int, Queue<IUiElement>> fixedEntries = new();

		foreach (GridEntry entry in _entries)
		{
			if (!entry.SlotIndex.HasValue)
			{
				dynamicQueue.Enqueue(entry.Element);
				continue;
			}

			if (!fixedEntries.TryGetValue(entry.SlotIndex.Value, out Queue<IUiElement>? list))
			{
				list = new Queue<IUiElement>();
				fixedEntries[entry.SlotIndex.Value] = list;
			}

			list.Enqueue(entry.Element);
		}

		int maxFixedSlot = -1;
		foreach (int slot in fixedEntries.Keys)
		{
			if (slot > maxFixedSlot)
			{
				maxFixedSlot = slot;
			}
		}

		int slotIndex = 0;
		while (dynamicQueue.Count > 0 || fixedEntries.Count > 0 || slotIndex <= maxFixedSlot)
		{
			if (fixedEntries.TryGetValue(slotIndex, out Queue<IUiElement>? fixedAtSlot))
			{
				while (fixedAtSlot.Count > 0)
				{
					yield return fixedAtSlot.Dequeue().Build();
				}
				fixedEntries.Remove(slotIndex);
				slotIndex++;
				continue;
			}

			if (dynamicQueue.Count > 0)
			{
				yield return dynamicQueue.Dequeue().Build();
			}
			else
			{
				yield return new Control { Name = $"{SpacerPrefix}{slotIndex}" };
			}

			slotIndex++;
		}
	}
}
