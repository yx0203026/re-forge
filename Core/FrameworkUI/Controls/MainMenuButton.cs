#nullable enable

using System;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using ReForgeFramework.UI.Abstractions;
using ReForgeFramework.UI.Localization;

namespace ReForgeFramework.UI.Controls;

public sealed partial class MainMenuButton : UiElement
{
	private readonly string _text;
	private readonly string? _textKey;
	private readonly string? _locTable;
	private readonly string? _locEntryKey;
	private readonly Action? _onClick;

	public MainMenuButton(
		string text = "ReForge",
		Action? onClick = null,
		string? textKey = null,
		string? locTable = null,
		string? locEntryKey = null)
	{
		_text = text;
		_onClick = onClick;
		_textKey = textKey;
		_locTable = locTable;
		_locEntryKey = locEntryKey;
	}

	protected override Control CreateControl()
	{
		return new MainMenuStyledProxy(_text, _onClick, _textKey, _locTable, _locEntryKey);
	}

	private sealed partial class MainMenuStyledProxy : Control
	{
		private readonly string _text;
		private readonly string? _textKey;
		private readonly string? _locTable;
		private readonly string? _locEntryKey;
		private readonly Action? _onClick;
		private bool _initialized;

		public MainMenuStyledProxy(string text, Action? onClick, string? textKey, string? locTable, string? locEntryKey)
		{
			_text = text;
			_onClick = onClick;
			_textKey = textKey;
			_locTable = locTable;
			_locEntryKey = locEntryKey;
			Name = "ReForgeMainMenuButton";
			SizeFlagsHorizontal = SizeFlags.ExpandFill;
			CustomMinimumSize = new Vector2(200f, 50f);
			FocusMode = FocusModeEnum.All;
		}

		public override void _Ready()
		{
			CallDeferred(nameof(InitializeVisual));
		}

		private void InitializeVisual()
		{
			if (_initialized || !IsInsideTree())
			{
				return;
			}

			_initialized = true;
			if (TryReplaceWithOfficialClone())
			{
				return;
			}

			BuildFallbackButton();
		}

		// 优先复制官方主菜单按钮模板，确保主题、字体、动画与交互一致。
		private bool TryReplaceWithOfficialClone()
		{
			if (GetParent() is not Control parent)
			{
				return false;
			}

			foreach (Node sibling in parent.GetChildren())
			{
				if (sibling == this || sibling is not Control existing)
				{
					continue;
				}

				if (existing.Name != Name)
				{
					continue;
				}

				ApplyText(existing);
				QueueFree();
				return true;
			}

			Control? template = null;
			foreach (Node child in parent.GetChildren())
			{
				if (child == this || child is not Control candidate)
				{
					continue;
				}

				if (candidate.GetNodeOrNull<Godot.Label>("Label") == null)
				{
					continue;
				}

				template = candidate;
				if (candidate.GetNodeOrNull<Node>("ContinueRunInfo") == null)
				{
					break;
				}
			}

			if (template == null)
			{
				return false;
			}

			if (template.Duplicate() is not Control clone)
			{
				return false;
			}

			clone.Name = Name;
			clone.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			clone.CustomMinimumSize = new Vector2(200f, 50f);
			ApplyText(clone);

			if (_onClick != null)
			{
				if (clone.HasSignal("released"))
				{
					clone.Connect("released", Callable.From(_onClick));
				}
				else if (clone is Godot.Button button)
				{
					button.Pressed += _onClick;
				}
			}

			parent.AddChild(clone);
			parent.MoveChild(clone, GetIndex());
			BindLocalizationRefresh(clone);
			HookMainMenuFocusReticle(clone);
			QueueFree();
			return true;
		}

		private void BuildFallbackButton()
		{
			if (GetChildCount() > 0)
			{
				return;
			}

			Godot.Button button = new()
			{
				Name = "FallbackMainMenuButton",
				Text = ResolveText(),
				Flat = true,
				Alignment = HorizontalAlignment.Center,
				FocusMode = FocusModeEnum.All
			};

			button.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
			if (_onClick != null)
			{
				button.Pressed += _onClick;
			}

			BindLocalizationRefresh(button);
			AddChild(button);
		}

		private string ResolveText()
		{
			return UiLocalization.GetText(_textKey, _text, _locTable, _locEntryKey);
		}

		private void ApplyText(Control target)
		{
			if (target.GetNodeOrNull<Godot.Label>("Label") is { } label)
			{
				label.Text = ResolveText();
				HookLabelCenterPivot(label);
				return;
			}

			if (target is Godot.Button button)
			{
				button.Text = ResolveText();
			}
		}

		// 官方主菜单按钮的缩放基于 Label 中心点，需要在布局变化后持续刷新。
		private static void HookLabelCenterPivot(Godot.Label label)
		{
			const string PivotBoundMetaKey = "reforge_main_menu_pivot_bound";

			void UpdatePivot()
			{
				if (!GodotObject.IsInstanceValid(label))
				{
					return;
				}

				label.PivotOffset = label.Size * 0.5f;
			}

			UpdatePivot();
			Callable.From(UpdatePivot).CallDeferred();

			if (label.HasMeta(PivotBoundMetaKey))
			{
				return;
			}

			label.SetMeta(PivotBoundMetaKey, true);
			label.Resized += UpdatePivot;
		}

		// 复用官方 NMainMenu 焦点处理链路，不再自定义指示器动画。
		private static void HookMainMenuFocusReticle(Control clone)
		{
			if (clone is not NClickableControl clickable)
			{
				return;
			}

			if (clone.GetParent()?.GetParent() is not Node mainMenuRoot)
			{
				return;
			}

			MethodInfo? focusedMethod = mainMenuRoot.GetType().GetMethod("MainMenuButtonFocused", BindingFlags.Instance | BindingFlags.NonPublic);
			MethodInfo? unfocusedMethod = mainMenuRoot.GetType().GetMethod("MainMenuButtonUnfocused", BindingFlags.Instance | BindingFlags.NonPublic);

			if (focusedMethod == null || unfocusedMethod == null)
			{
				return;
			}

			Type? focusedParamType = focusedMethod.GetParameters().Length > 0
				? focusedMethod.GetParameters()[0].ParameterType
				: null;

			Type? unfocusedParamType = unfocusedMethod.GetParameters().Length > 0
				? unfocusedMethod.GetParameters()[0].ParameterType
				: null;

			if (focusedParamType == null || unfocusedParamType == null)
			{
				return;
			}

			if (!focusedParamType.IsInstanceOfType(clone) || !unfocusedParamType.IsInstanceOfType(clone))
			{
				return;
			}

			clickable.Connect(NClickableControl.SignalName.Focused, Callable.From<NClickableControl>(_ =>
			{
				if (!GodotObject.IsInstanceValid(mainMenuRoot) || !GodotObject.IsInstanceValid(clone))
				{
					return;
				}

				Callable.From(() =>
				{
					object? ignoredResult = focusedMethod.Invoke(mainMenuRoot, new object?[] { clone });
				}).CallDeferred();
			}));

			clickable.Connect(NClickableControl.SignalName.Unfocused, Callable.From<NClickableControl>(_ =>
			{
				if (!GodotObject.IsInstanceValid(mainMenuRoot) || !GodotObject.IsInstanceValid(clone))
				{
					return;
				}

				unfocusedMethod.Invoke(mainMenuRoot, new object?[] { clone });
			}));
		}

		private void BindLocalizationRefresh(Control target)
		{
			if (string.IsNullOrWhiteSpace(_textKey)
				&& (string.IsNullOrWhiteSpace(_locTable) || string.IsNullOrWhiteSpace(_locEntryKey)))
			{
				return;
			}

			Action? handler = null;
			handler = () =>
			{
				if (!GodotObject.IsInstanceValid(target))
				{
					if (handler != null)
					{
						UiLocalization.LocaleChanged -= handler;
					}
					return;
				}

				ApplyText(target);
			};

			UiLocalization.LocaleChanged += handler;
			target.TreeExiting += () =>
			{
				if (handler != null)
				{
					UiLocalization.LocaleChanged -= handler;
				}
			};
		}
	}
}
