#nullable enable

using System;
using System.Collections.Generic;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;
using ReForgeFramework.UI.Abstractions;
using ReForgeFramework.UI.Localization;

namespace ReForgeFramework.UI.Controls;

/// <summary>
/// 自定义设置条目（官方风格）：左侧说明文本 + 右侧操作控件。
/// </summary>
public sealed class SettingOptionItem : UiElement
{
	private const string SettingsLineThemePath = "res://themes/settings_screen_line_header.tres";

	private readonly string _title;
	private readonly IUiElement _optionControl;

	/// <summary>
	/// 是否已配置悬浮提示。
	/// </summary>
	public bool HasHoverTip { get; private set; }

	internal string Title => _title;

	/// <summary>
	/// 初始化设置项（左侧标题 + 右侧控件）。
	/// </summary>
	/// <param name="title">标题文本。</param>
	/// <param name="optionControl">右侧选项控件。</param>
	public SettingOptionItem(string title, IUiElement optionControl)
	{
		_title = title;
		_optionControl = optionControl;
	}

	/// <summary>
	/// 创建“标题 + 勾选框”的设置项。
	/// </summary>
	/// <param name="title">标题文本。</param>
	/// <param name="initialValue">初始勾选状态。</param>
	/// <param name="onToggled">状态变化回调。</param>
	/// <returns>设置项实例。</returns>
	public static SettingOptionItem Toggle(string title, bool initialValue, System.Action<bool>? onToggled = null)
	{
		return new SettingOptionItem(title, new TickBox(initialValue, onToggled));
	}

	/// <summary>
	/// 创建“左侧标题 + 右侧官方反馈风格按钮”的设置条目。
	/// </summary>
	public static SettingOptionItem FeedbackButton(
		string title,
		string buttonText,
		Action? onPressed = null,
		string? textKey = null,
		string? locTable = null,
		string? locEntryKey = null)
	{
		return new SettingOptionItem(title, new OfficialFeedbackButton(buttonText, onPressed, textKey, locTable, locEntryKey));
	}

	/// <summary>
	/// 为设置条目绑定鼠标悬浮提示（必须项）。
	/// </summary>
	public SettingOptionItem WithHoverTip(string locTable, string titleLocKey, string descriptionLocKey)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(locTable);
		ArgumentException.ThrowIfNullOrWhiteSpace(titleLocKey);
		ArgumentException.ThrowIfNullOrWhiteSpace(descriptionLocKey);

		if (HasHoverTip)
		{
			return this;
		}

		HasHoverTip = true;

		OnHoverEnter(owner =>
		{
			if (!GodotObject.IsInstanceValid(owner))
			{
				return;
			}

			EnsureHoverTipKeysInLocTable(locTable, titleLocKey, descriptionLocKey);

			NHoverTipSet.Remove(owner);
			HoverTip tip = new(
				new LocString(locTable, titleLocKey),
				new LocString(locTable, descriptionLocKey)
			);

			NHoverTipSet tipSet = NHoverTipSet.CreateAndShow(owner, tip);
			tipSet.GlobalPosition = owner.GlobalPosition + NSettingsScreen.settingTipsOffset;
		});

		OnHoverExit(owner =>
		{
			if (!GodotObject.IsInstanceValid(owner))
			{
				return;
			}

			NHoverTipSet.Remove(owner);
		});

		return this;
	}

	private static void EnsureHoverTipKeysInLocTable(string locTable, string titleLocKey, string descriptionLocKey)
	{
		try
		{
			if (LocManager.Instance == null)
			{
				return;
			}

			string titleText = UiLocalization.GetText(null, titleLocKey, locTable, titleLocKey);
			string descriptionText = UiLocalization.GetText(null, descriptionLocKey, locTable, descriptionLocKey);
			Dictionary<string, string> patch = new(StringComparer.Ordinal)
			{
				[titleLocKey] = titleText,
				[descriptionLocKey] = descriptionText
			};

			LocManager.Instance.GetTable(locTable).MergeWith(patch);
		}
		catch
		{
			// 悬浮提示不应因本地化补丁失败而中断交互流程。
		}
	}

	protected override Control CreateControl()
	{
		VBoxContainer root = new()
		{
			Name = "ReForgeSettingOptionItem",
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
		};

		MarginContainer row = new()
		{
			Name = "Row",
			CustomMinimumSize = new Vector2(0f, 64f),
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
		};
		row.AddThemeConstantOverride("margin_left", 12);
		row.AddThemeConstantOverride("margin_top", 0);
		row.AddThemeConstantOverride("margin_right", 12);
		row.AddThemeConstantOverride("margin_bottom", 0);

		RichTextLabel labelControl = BuildOfficialLabel();
		labelControl.Name = "Label";
		labelControl.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		labelControl.FocusMode = Control.FocusModeEnum.None;
		labelControl.MouseFilter = Control.MouseFilterEnum.Ignore;
		labelControl.BbcodeEnabled = true;
		labelControl.Text = _title;
		labelControl.VerticalAlignment = VerticalAlignment.Center;

		HBoxContainer rowContent = new()
		{
			Name = "RowContent",
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			Alignment = BoxContainer.AlignmentMode.Begin
		};
		rowContent.AddThemeConstantOverride("separation", 16);

		Control option = _optionControl.Build();
		option.Name = "Option";
		option.CustomMinimumSize = new Vector2(320f, 64f);
		option.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;

		rowContent.AddChild(labelControl);
		rowContent.AddChild(option);
		row.AddChild(rowContent);

		ColorRect divider = new()
		{
			Name = "Divider",
			CustomMinimumSize = new Vector2(0f, 2f),
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			Color = new Color(0.909804f, 0.862745f, 0.745098f, 0.25098f)
		};

		root.AddChild(row);
		root.AddChild(divider);
		return root;
	}

	private sealed class OfficialFeedbackButton : UiElement
	{
		private const string SettingsScreenScenePath = "res://scenes/screens/settings_screen.tscn";
		private const string TemplateButtonPath = "ScrollContainer/Mask/Clipper/GeneralSettings/VBoxContainer/SendFeedback/FeedbackButton";
		private const string MetaReadyTextHookBound = "__reforge_feedback_button_ready_text_hook_bound";
		private const string MetaGuiInputHookBound = "__reforge_feedback_button_gui_input_hook_bound";

		private readonly string _text;
		private readonly Action? _onPressed;
		private readonly string? _textKey;
		private readonly string? _locTable;
		private readonly string? _locEntryKey;
		private ulong _lastPressInvokeAtMs;

		/// <summary>
		/// 初始化官方反馈按钮封装。
		/// </summary>
		/// <param name="text">默认显示文本。</param>
		/// <param name="onPressed">点击回调。</param>
		/// <param name="textKey">UI 本地化 key。</param>
		/// <param name="locTable">官方本地化表名。</param>
		/// <param name="locEntryKey">官方本地化词条键。</param>
		public OfficialFeedbackButton(
			string text,
			Action? onPressed = null,
			string? textKey = null,
			string? locTable = null,
			string? locEntryKey = null)
		{
			_text = text;
			_onPressed = onPressed;
			_textKey = textKey;
			_locTable = locTable;
			_locEntryKey = locEntryKey;
		}

		protected override Control CreateControl()
		{
			Control button = TryCloneOfficialFeedbackButton() ?? BuildFallbackButton();
			button.Name = "ReForgeFeedbackOptionButton";
			button.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;
			button.FocusMode = Control.FocusModeEnum.All;

			BindPressed(button);
			ApplyButtonText(button);
			BindReadyTextRefresh(button);
			BindLocalizationRefresh(button);

			return button;
		}

		private Control? TryCloneOfficialFeedbackButton()
		{
			if (!ResourceLoader.Exists(SettingsScreenScenePath))
			{
				return null;
			}

			PackedScene? scene = ResourceLoader.Load<PackedScene>(SettingsScreenScenePath);
			if (scene?.Instantiate() is not Node root)
			{
				return null;
			}

			Control? template = root.GetNodeOrNull<Control>(TemplateButtonPath) ?? FindFeedbackButtonTemplate(root);
			Control? cloned = template?.Duplicate() as Control;
			root.QueueFree();
			return cloned;
		}

		private static Control? FindFeedbackButtonTemplate(Node root)
		{
			if (root is Control control
				&& string.Equals(control.Name, "FeedbackButton", StringComparison.Ordinal)
				&& control.GetNodeOrNull<Node>("Label") != null)
			{
				return control;
			}

			foreach (Node child in root.GetChildren())
			{
				Control? found = FindFeedbackButtonTemplate(child);
				if (found != null)
				{
					return found;
				}
			}

			return null;
		}

		private Control BuildFallbackButton()
		{
			return new Button(
				text: ResolveText(),
				onClick: _onPressed,
				textKey: _textKey,
				locTable: _locTable,
				locEntryKey: _locEntryKey,
				stylePreset: UiButtonStylePreset.OfficialConfirm,
				customStyler: nativeButton =>
				{
					nativeButton.CustomMinimumSize = new Vector2(220f, 54f);
				})
				.Build();
		}

		private void BindPressed(Control button)
		{
			if (_onPressed == null)
			{
				return;
			}

			button.MouseFilter = Control.MouseFilterEnum.Stop;
			EnsureClickableEnabled(button);

			bool bound = false;

			if (button.HasSignal("released"))
			{
				if (TryConnectButtonSignal(button, "released", Callable.From(() => TriggerPressed("released"))))
				{
					bound = true;
				}

				if (TryConnectButtonSignal(button, "released", Callable.From<GodotObject>(_ => TriggerPressed("released(obj)"))))
				{
					bound = true;
				}
			}

			if (button.HasSignal("pressed"))
			{
				if (TryConnectButtonSignal(button, "pressed", Callable.From(() => TriggerPressed("pressed"))))
				{
					bound = true;
				}

				if (TryConnectButtonSignal(button, "pressed", Callable.From<GodotObject>(_ => TriggerPressed("pressed(obj)"))))
				{
					bound = true;
				}
			}

			if (button is Godot.Button nativeButton)
			{
				nativeButton.Pressed += () => TriggerPressed("native-button");
				bound = true;
			}

			if (TryBindGuiInputFallback(button))
			{
				bound = true;
			}

			if (!bound)
			{
				GD.PrintErr("[ReForge.UI] Feedback button has no bindable click signal. Please check runtime control type.");
			}
		}

		private bool TryBindGuiInputFallback(Control button)
		{
			if (button.HasMeta(MetaGuiInputHookBound))
			{
				return true;
			}

			if (!button.HasSignal(Control.SignalName.GuiInput))
			{
				return false;
			}

			Error connectResult = button.Connect(Control.SignalName.GuiInput, Callable.From<InputEvent>(inputEvent =>
			{
				if (inputEvent is not InputEventMouseButton mouseButton)
				{
					return;
				}

				if (mouseButton.ButtonIndex != MouseButton.Left || !mouseButton.Pressed)
				{
					return;
				}

				TriggerPressed("gui_input");
			}));

			if (connectResult != Error.Ok)
			{
				return false;
			}

			button.SetMeta(MetaGuiInputHookBound, true);
			return true;
		}

		private static void EnsureClickableEnabled(Control button)
		{
			if (button is BaseButton baseButton)
			{
				baseButton.Disabled = false;
			}

			try
			{
				PropertyInfo? isEnabledProperty = button.GetType().GetProperty("IsEnabled", BindingFlags.Instance | BindingFlags.Public);
				if (isEnabledProperty?.CanWrite == true && isEnabledProperty.PropertyType == typeof(bool))
				{
					isEnabledProperty.SetValue(button, true);
				}

				MethodInfo? enableMethod = button.GetType().GetMethod("Enable", BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null);
				enableMethod?.Invoke(button, null);
			}
			catch
			{
				// 某些控件没有 Enable/IsEnabled，可忽略。
			}
		}

		private void TriggerPressed(string source)
		{
			if (_onPressed == null)
			{
				return;
			}

			ulong now = Time.GetTicksMsec();
			if (now - _lastPressInvokeAtMs < 200)
			{
				return;
			}

			_lastPressInvokeAtMs = now;
			GD.Print($"[ReForge.UI] Feedback button clicked via {source}.");
			_onPressed();
		}

		private static bool TryConnectButtonSignal(Control button, string signal, Callable callable)
		{
			Error connectResult = button.Connect(signal, callable);
			return connectResult == Error.Ok;
		}

		private void BindReadyTextRefresh(Control button)
		{
			if (button.HasMeta(MetaReadyTextHookBound))
			{
				return;
			}

			button.SetMeta(MetaReadyTextHookBound, true);
			button.Connect(Node.SignalName.Ready, Callable.From(() => ApplyButtonText(button)), (uint)GodotObject.ConnectFlags.OneShot);
		}

		private void ApplyButtonText(Control button)
		{
			string text = ResolveText();

			if (button.GetNodeOrNull<Godot.Label>("Label") is { } gdLabel)
			{
				gdLabel.Text = text;
			}

			if (button.GetNodeOrNull<Node>("Label") is { } anyLabel)
			{
				MethodInfo? setAutoSize = anyLabel.GetType().GetMethod("SetTextAutoSize", BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(string) }, null);
				if (setAutoSize != null)
				{
					setAutoSize.Invoke(anyLabel, new object?[] { text });
					return;
				}

				PropertyInfo? textProp = anyLabel.GetType().GetProperty("Text", BindingFlags.Instance | BindingFlags.Public);
				if (textProp?.CanWrite == true)
				{
					textProp.SetValue(anyLabel, text);
				}
			}
		}

		private void BindLocalizationRefresh(Control button)
		{
			if (string.IsNullOrWhiteSpace(_textKey)
				&& (string.IsNullOrWhiteSpace(_locTable) || string.IsNullOrWhiteSpace(_locEntryKey)))
			{
				return;
			}

			Action? handler = null;
			handler = () =>
			{
				if (!GodotObject.IsInstanceValid(button))
				{
					if (handler != null)
					{
						UiLocalization.LocaleChanged -= handler;
					}
					return;
				}

				ApplyButtonText(button);
			};

			UiLocalization.LocaleChanged += handler;
			button.TreeExiting += () =>
			{
				if (handler != null)
				{
					UiLocalization.LocaleChanged -= handler;
				}
			};
		}

		private string ResolveText()
		{
			return UiLocalization.GetText(_textKey, _text, _locTable, _locEntryKey);
		}
	}

	private static RichTextLabel BuildOfficialLabel()
	{
		RichTextLabel label = new();

		if (ResourceLoader.Exists(SettingsLineThemePath))
		{
			Theme? theme = ResourceLoader.Load<Theme>(SettingsLineThemePath);
			if (theme != null)
			{
				label.Theme = theme;
			}
		}

		return label;
	}
}
