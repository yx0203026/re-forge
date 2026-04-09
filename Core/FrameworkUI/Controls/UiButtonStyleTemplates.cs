#nullable enable

using Godot;
using MegaCrit.Sts2.Core.Helpers;
using ReForgeFramework.UI.Runtime;

namespace ReForgeFramework.UI.Controls;

/// <summary>
/// 官方按钮模板提炼：保留 STS2 常见交互节奏与色彩倾向。
/// </summary>
internal static class UiButtonStyleTemplates
{
	private static readonly StringName MetaAnimBound = "__reforge_button_anim_bound";
	private static readonly StringName MetaPivotBound = "__reforge_button_pivot_bound";

	private static readonly Color MainMenuDefaultColor = StsColors.cream;
	private static readonly Color MainMenuHoverColor = StsColors.gold;
	private static readonly Color MainMenuPressedColor = StsColors.halfTransparentWhite;

	private static readonly Color ConfirmOutlineColor = new Color("F0B400");
	private static readonly Color ConfirmPressedColor = Colors.Gray;

	private static readonly Color BackOutlineColor = new Color("F0B400");
	private static readonly Color BackPressedColor = Colors.Gray;

	private static readonly Color SettingsOptionBaseColor = new Color("26373d");
	private static readonly Color SettingsOptionHoverColor = new Color("235161");
	private static readonly Color SettingsOptionPressedColor = StsColors.lightGray;
	private static readonly Color SettingsArrowColor = new Color("e0b12a");

	/// <summary>
	/// 应用指定按钮样式预设。
	/// </summary>
	/// <param name="button">目标按钮。</param>
	/// <param name="preset">样式预设。</param>
	public static void Apply(Godot.Button button, UiButtonStylePreset preset)
	{
		switch (preset)
		{
			case UiButtonStylePreset.OfficialMainMenuText:
				ApplyOfficialMainMenuText(button);
				break;
			case UiButtonStylePreset.OfficialConfirm:
				ApplyOfficialConfirm(button);
				break;
			case UiButtonStylePreset.OfficialBack:
				ApplyOfficialBack(button);
				break;
			case UiButtonStylePreset.OfficialSettingsOption:
				ApplyOfficialSettingsOption(button);
				break;
			case UiButtonStylePreset.OfficialSettingsArrow:
				ApplyOfficialSettingsArrow(button);
				break;
			case UiButtonStylePreset.Custom:
			case UiButtonStylePreset.GodotDefault:
			default:
				break;
		}
	}

	private static void ApplyOfficialMainMenuText(Godot.Button button)
	{
		button.Flat = true;
		button.Alignment = HorizontalAlignment.Center;
		button.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
		button.AddThemeStyleboxOverride("hover", new StyleBoxEmpty());
		button.AddThemeStyleboxOverride("pressed", new StyleBoxEmpty());
		button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		button.AddThemeColorOverride("font_color", MainMenuDefaultColor);
		button.AddThemeColorOverride("font_hover_color", MainMenuHoverColor);
		button.AddThemeColorOverride("font_pressed_color", MainMenuPressedColor);

		BindCenterPivot(button);
		BindScaleAnimation(
			button,
			hoverScale: new Vector2(1.05f, 1.05f),
			pressScale: new Vector2(0.95f, 0.95f),
			hoverDuration: 0.05f,
			unhoverDuration: 0.5f,
			pressDuration: 0.2f,
			releaseDuration: 0.2f,
			hoverModulate: MainMenuHoverColor,
			pressedModulate: MainMenuPressedColor,
			defaultModulate: MainMenuDefaultColor);
	}

	private static void ApplyOfficialConfirm(Godot.Button button)
	{
		button.Flat = false;
		button.Alignment = HorizontalAlignment.Center;
		button.AddThemeStyleboxOverride("normal", CreateFlatStyle(new Color("26231B"), ConfirmOutlineColor, 2, 8));
		button.AddThemeStyleboxOverride("hover", CreateFlatStyle(new Color("3A3528"), ConfirmOutlineColor, 2, 8));
		button.AddThemeStyleboxOverride("pressed", CreateFlatStyle(new Color("1E1B14"), ConfirmOutlineColor, 2, 8));
		button.AddThemeColorOverride("font_color", Colors.White);
		button.AddThemeColorOverride("font_hover_color", MainMenuHoverColor);
		button.AddThemeColorOverride("font_pressed_color", ConfirmPressedColor);

		BindCenterPivot(button);
		BindScaleAnimation(
			button,
			hoverScale: new Vector2(1.05f, 1.05f),
			pressScale: new Vector2(0.95f, 0.95f),
			hoverDuration: 0.05f,
			unhoverDuration: 0.5f,
			pressDuration: 0.25f,
			releaseDuration: 0.2f,
			hoverModulate: Colors.White,
			pressedModulate: ConfirmPressedColor,
			defaultModulate: Colors.White);
	}

	private static void ApplyOfficialBack(Godot.Button button)
	{
		button.Flat = false;
		button.Alignment = HorizontalAlignment.Center;
		button.AddThemeStyleboxOverride("normal", CreateFlatStyle(new Color("1F232E"), BackOutlineColor, 2, 8));
		button.AddThemeStyleboxOverride("hover", CreateFlatStyle(new Color("2B3240"), BackOutlineColor, 2, 8));
		button.AddThemeStyleboxOverride("pressed", CreateFlatStyle(new Color("151922"), BackOutlineColor, 2, 8));
		button.AddThemeColorOverride("font_color", StsColors.cream);
		button.AddThemeColorOverride("font_hover_color", MainMenuHoverColor);
		button.AddThemeColorOverride("font_pressed_color", BackPressedColor);

		BindCenterPivot(button);
		BindScaleAnimation(
			button,
			hoverScale: new Vector2(1.05f, 1.05f),
			pressScale: Vector2.One,
			hoverDuration: 0.05f,
			unhoverDuration: 0.5f,
			pressDuration: 0.25f,
			releaseDuration: 0.2f,
			hoverModulate: Colors.White,
			pressedModulate: BackPressedColor,
			defaultModulate: Colors.White);
	}

	private static void ApplyOfficialSettingsOption(Godot.Button button)
	{
		button.Flat = false;
		button.Alignment = HorizontalAlignment.Left;
		button.AddThemeStyleboxOverride("normal", CreateFlatStyle(SettingsOptionBaseColor, new Color("22404f"), 1, 0));
		button.AddThemeStyleboxOverride("hover", CreateFlatStyle(SettingsOptionHoverColor, new Color("3da6c8"), 1, 0));
		button.AddThemeStyleboxOverride("pressed", CreateFlatStyle(SettingsOptionBaseColor, new Color("3da6c8"), 1, 0));
		button.AddThemeColorOverride("font_color", StsColors.cream);
		button.AddThemeColorOverride("font_hover_color", StsColors.gold);
		button.AddThemeColorOverride("font_pressed_color", SettingsOptionPressedColor);

		BindCenterPivot(button);
		BindScaleAnimation(
			button,
			hoverScale: new Vector2(1.03f, 1.03f),
			pressScale: new Vector2(0.95f, 0.95f),
			hoverDuration: 0.03f,
			unhoverDuration: 0.5f,
			pressDuration: 0.2f,
			releaseDuration: 0.05f,
			hoverModulate: Colors.White,
			pressedModulate: SettingsOptionPressedColor,
			defaultModulate: Colors.White);
	}

	private static void ApplyOfficialSettingsArrow(Godot.Button button)
	{
		button.Flat = true;
		button.Alignment = HorizontalAlignment.Center;
		button.CustomMinimumSize = new Vector2(40f, 34f);
		button.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
		button.AddThemeStyleboxOverride("hover", new StyleBoxEmpty());
		button.AddThemeStyleboxOverride("pressed", new StyleBoxEmpty());
		button.AddThemeColorOverride("font_color", SettingsArrowColor);
		button.AddThemeColorOverride("font_hover_color", Colors.White);
		button.AddThemeColorOverride("font_pressed_color", StsColors.gray);

		BindCenterPivot(button);
		BindScaleAnimation(
			button,
			hoverScale: new Vector2(1.1f, 1.1f),
			pressScale: new Vector2(0.9f, 0.9f),
			hoverDuration: 0.05f,
			unhoverDuration: 0.5f,
			pressDuration: 0.2f,
			releaseDuration: 0.05f,
			hoverModulate: Colors.White,
			pressedModulate: StsColors.gray,
			defaultModulate: Colors.White);
	}

	private static StyleBoxFlat CreateFlatStyle(Color bgColor, Color borderColor, int borderWidth, int cornerRadius)
	{
		return new StyleBoxFlat
		{
			BgColor = bgColor,
			BorderColor = borderColor,
			BorderWidthBottom = borderWidth,
			BorderWidthTop = borderWidth,
			BorderWidthLeft = borderWidth,
			BorderWidthRight = borderWidth,
			CornerRadiusBottomLeft = cornerRadius,
			CornerRadiusBottomRight = cornerRadius,
			CornerRadiusTopLeft = cornerRadius,
			CornerRadiusTopRight = cornerRadius
		};
	}

	private static void BindCenterPivot(Control control)
	{
		void UpdatePivot()
		{
			if (!GodotObject.IsInstanceValid(control))
			{
				return;
			}

			control.PivotOffset = control.Size * 0.5f;
		}

		UpdatePivot();
		Callable.From(UpdatePivot).CallDeferred();

		if (control.HasMeta(MetaPivotBound))
		{
			return;
		}

		control.SetMeta(MetaPivotBound, true);
		control.Resized += UpdatePivot;
	}

	private static void BindScaleAnimation(
		Godot.Button button,
		Vector2 hoverScale,
		Vector2 pressScale,
		float hoverDuration,
		float unhoverDuration,
		float pressDuration,
		float releaseDuration,
		Color hoverModulate,
		Color pressedModulate,
		Color defaultModulate)
	{
		if (button.HasMeta(MetaAnimBound))
		{
			return;
		}

		button.SetMeta(MetaAnimBound, true);

		button.MouseEntered += () =>
		{
			UiTweenAnimation.TweenScale(button, hoverScale, hoverDuration, Tween.TransitionType.Cubic, Tween.EaseType.Out);
			button.SelfModulate = hoverModulate;
		};

		button.MouseExited += () =>
		{
			UiTweenAnimation.TweenScale(button, Vector2.One, unhoverDuration, Tween.TransitionType.Expo, Tween.EaseType.Out);
			button.SelfModulate = defaultModulate;
		};

		button.ButtonDown += () =>
		{
			UiTweenAnimation.TweenScale(button, pressScale, pressDuration, Tween.TransitionType.Cubic, Tween.EaseType.Out);
			button.SelfModulate = pressedModulate;
		};

		button.ButtonUp += () =>
		{
			bool inside = new Rect2(Vector2.Zero, button.Size).HasPoint(button.GetLocalMousePosition());
			UiTweenAnimation.TweenScale(
				button,
				inside ? hoverScale : Vector2.One,
				releaseDuration,
				Tween.TransitionType.Cubic,
				Tween.EaseType.Out);
			button.SelfModulate = inside ? hoverModulate : defaultModulate;
		};
	}
}
