#nullable enable

using System;
using Godot;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;
using ReForgeFramework.UI.Abstractions;

namespace ReForgeFramework.UI.Controls;

/// <summary>
/// 勾选框独立控件：优先克隆官方 NTickbox，复用官方勾选与动画逻辑。
/// </summary>
public sealed partial class TickBox : UiElement
{
	private const string SettingsTickboxScenePath = "res://scenes/screens/settings_tickbox.tscn";
	private const string MetaReadyHookBound = "__reforge_tickbox_ready_hook_bound";

	private bool _checked;
	private readonly Action<bool>? _onToggled;

	public TickBox(bool initialChecked = false, Action<bool>? onToggled = null)
	{
		_checked = initialChecked;
		_onToggled = onToggled;
	}

	public TickBox WithChecked(bool isChecked = true)
	{
		_checked = isChecked;
		if (BuiltControl is NTickbox tickbox && GodotObject.IsInstanceValid(tickbox))
		{
			ApplyCheckedStateOrSchedule(tickbox);
		}
		return this;
	}

	protected override Control CreateControl()
	{
		NTickbox? tickbox = BuildOfficialTickbox();
		if (tickbox == null)
		{
			return BuildFallbackControl();
		}

		tickbox.Name = "ReForgeTickBox";
		ApplyCheckedStateOrSchedule(tickbox);
		if (_onToggled != null)
		{
			tickbox.Connect(NTickbox.SignalName.Toggled, Callable.From<NTickbox>(source => _onToggled(source.IsTicked)));
		}

		return tickbox;
	}

	private void ApplyCheckedStateOrSchedule(NTickbox tickbox)
	{
		if (!GodotObject.IsInstanceValid(tickbox))
		{
			return;
		}

		if (TryApplyCheckedState(tickbox))
		{
			return;
		}

		if (tickbox.HasMeta(MetaReadyHookBound))
		{
			return;
		}

		tickbox.SetMeta(MetaReadyHookBound, true);
		tickbox.Connect(Node.SignalName.Ready, Callable.From(() =>
		{
			if (!GodotObject.IsInstanceValid(tickbox))
			{
				return;
			}

			TryApplyCheckedState(tickbox);
		}), (uint)GodotObject.ConnectFlags.OneShot);
	}

	private bool TryApplyCheckedState(NTickbox tickbox)
	{
		if (!tickbox.IsNodeReady())
		{
			return false;
		}

		tickbox.IsTicked = _checked;
		return true;
	}

	private static NTickbox? BuildOfficialTickbox()
	{
		if (!ResourceLoader.Exists(SettingsTickboxScenePath))
		{
			GD.Print($"[ReForge.UI] Missing official settings tickbox scene: '{SettingsTickboxScenePath}'.");
			return null;
		}

		PackedScene? scene = ResourceLoader.Load<PackedScene>(SettingsTickboxScenePath);
		if (scene == null)
		{
			return null;
		}

		Control? templateRoot = scene.Instantiate<Control>();
		if (templateRoot == null)
		{
			return null;
		}

		RuntimeSettingsTickbox tickbox = new RuntimeSettingsTickbox
		{
			CustomMinimumSize = templateRoot.CustomMinimumSize,
			SizeFlagsHorizontal = templateRoot.SizeFlagsHorizontal,
			SizeFlagsVertical = templateRoot.SizeFlagsVertical,
			FocusMode = templateRoot.FocusMode,
			MouseFilter = templateRoot.MouseFilter
		};

		foreach (Node child in templateRoot.GetChildren())
		{
			templateRoot.RemoveChild(child);
			tickbox.AddChild(child);
			AdoptOwnerRecursive(child, tickbox);
		}

		templateRoot.Free();
		return tickbox;
	}

	private static void AdoptOwnerRecursive(Node node, Node owner)
	{
		node.Owner = owner;
		foreach (Node child in node.GetChildren())
		{
			AdoptOwnerRecursive(child, owner);
		}
	}

	private Control BuildFallbackControl()
	{
		CheckBox checkBox = new()
		{
			Name = "FallbackTickBox",
			ButtonPressed = _checked,
			FocusMode = Control.FocusModeEnum.All,
			Text = string.Empty
		};

		if (_onToggled != null)
		{
			checkBox.Toggled += toggledOn => _onToggled(toggledOn);
		}

		return checkBox;
	}

	private sealed partial class RuntimeSettingsTickbox : NSettingsTickbox
	{
		public override void _Ready()
		{
			ConnectSignals();
		}
	}
}
