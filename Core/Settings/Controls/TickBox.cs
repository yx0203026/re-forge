#nullable enable

using System;
using Godot;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;
using ReForgeFramework.Settings.Abstractions;

namespace ReForgeFramework.Settings.Controls;

/// <summary>
/// 鍕鹃€夋鐙珛鎺т欢锛氫紭鍏堝厠闅嗗畼鏂?NTickbox锛屽鐢ㄥ畼鏂瑰嬀閫変笌鍔ㄧ敾閫昏緫銆?
/// </summary>
public sealed partial class TickBox : UiElement
{
	private const string SettingsTickboxScenePath = "res://scenes/screens/settings_tickbox.tscn";
	private const string MetaReadyHookBound = "__reforge_tickbox_ready_hook_bound";

	private bool _checked;
	private readonly Action<bool>? _onToggled;

	/// <summary>
	/// 鍒濆鍖栧嬀閫夋鎺т欢銆?
	/// </summary>
	/// <param name="initialChecked">鍒濆鍕鹃€夌姸鎬併€?/param>
	/// <param name="onToggled">鐘舵€佸彉鍖栧洖璋冦€?/param>
	public TickBox(bool initialChecked = false, Action<bool>? onToggled = null)
	{
		_checked = initialChecked;
		_onToggled = onToggled;
	}

	/// <summary>
	/// 璁剧疆鍕鹃€夋鐘舵€併€?
	/// </summary>
	/// <param name="isChecked">鐩爣鐘舵€併€?/param>
	/// <returns>褰撳墠鍕鹃€夋瀹炰緥銆?/returns>
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
		/// <summary>
		/// 鑺傜偣灏辩华鍚庤繛鎺ュ畼鏂逛氦浜掍俊鍙枫€?
		/// </summary>
		public override void _Ready()
		{
			ConnectSignals();
		}
	}
}

