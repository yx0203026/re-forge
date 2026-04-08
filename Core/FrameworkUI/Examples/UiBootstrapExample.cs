using Godot;
using ReForgeFramework.UI.Abstractions;
using ReForgeFramework.UI.Controls;
using ReForgeFramework.UI.Panels;
using ReForgeFramework.UI.Runtime;
using UiButton = ReForgeFramework.UI.Controls.Button;
using UiLabel = ReForgeFramework.UI.Controls.Label;
using UiRichText = ReForgeFramework.UI.Controls.RichText;

namespace ReForgeFramework.UI.Examples;

public static class UiBootstrapExample
{
	private static bool _configured;

	public static void Bootstrap(bool enableDemo = true)
	{
		if (!_configured)
		{
			_configured = true;
			ReForge.UI.Initialize();

			if (enableDemo)
			{
				ConfigureDemo();
			}
		}

		ReForge.UI.ReinjectSystemAreas();
	}

	private static void ConfigureDemo()
	{

		// StackPanel panel = new(horizontal: false, spacing: 8);
		// panel.WithAnchor(UiAnchorPreset.TopLeft);
		// panel.AddChild(new UiLabel("ReForge UI Runtime Ready", locTable: "gameplay_ui", locEntryKey: "REFORGE.UI.RUNTIME_READY").WithHeight(30f));
		// panel.AddChild(new UiRichText("[gold]ReForge[/gold] [green]RichText[/green]").WithHeight(36f));
		// panel.AddChild(
		// 	new UiLabel("Animated Label (Hover/Drag)")
		// 		.WithHeight(34f)
		// 		.WithCenterPivot()
		// 		.WithHoverScaleAnimation(1.08f)
		// 		.OnLeftMouseDown((control, _) =>
		// 		{
		// 			UiTweenAnimation.TweenScale(control, new Vector2(0.95f, 0.95f), 0.12f, Tween.TransitionType.Cubic, Tween.EaseType.Out);
		// 		})
		// 		.OnRightMouseDown((control, _) =>
		// 		{
		// 			control.SelfModulate = new Color(1f, 0.9f, 0.7f);
		// 		})
		// 		.OnDrag((control, motion) =>
		// 		{
		// 			float intensity = Mathf.Clamp(motion.Relative.Length() / 24f, 0f, 1f);
		// 			control.Rotation = Mathf.Lerp(control.Rotation, motion.Relative.X * 0.004f, intensity);
		// 		})
		// );
		// panel.AddChild(
		// 	new UiButton(
		// 		"Global Button",
		// 		() => GD.Print("[ReForge.UI] Global button clicked."),
		// 		locTable: "gameplay_ui",
		// 		locEntryKey: "REFORGE.UI.GLOBAL_BUTTON",
		// 		stylePreset: UiButtonStylePreset.OfficialConfirm)
		// 		.WithHeight(48f)
		// 		.WithAnchor(UiAnchorPreset.TopCenter)
		// 		.WithCenterPivot()
		// 		.WithTexturePath("res://icon.svg", TextureRect.StretchModeEnum.KeepAspectCentered)
		// 		.WithHoverScaleAnimation(1.05f)
		// 		.OnLeftMouseDown((control, _) =>
		// 		{
		// 			UiTweenAnimation.TweenScale(control, new Vector2(0.96f, 0.96f), 0.1f, Tween.TransitionType.Cubic, Tween.EaseType.Out);
		// 		})
		// );
		// panel.Show();

		var mainMenu = ReForge.UI.GetMainMenuButtonPanel();
		mainMenu.AddChild(
			new MainMenuButton("ReForge", () => GD.Print("[ReForge.UI] MainMenu button clicked."), locTable: "gameplay_ui", locEntryKey: "REFORGE.UI.MAIN_MENU_BUTTON")
				.WithHeight(46f)
				.WithAnchor(UiAnchorPreset.Stretch)
		);

		var settingTabs = ReForge.UI.GetSettingTabPanel();
		settingTabs.AddChild(
			new SettingTab("ReForge", () => GD.Print("[ReForge.UI] Settings tab clicked."), selected: true)
				.WithMinHeight(72f)
		);
		settingTabs.GetSettingScreen("ReForge")?.Add(
			SettingOptionItem.Toggle("启用 ReForge 功能", initialValue: true, onToggled: isOn =>
			{
				GD.Print($"[ReForge.UI] ReForge setting toggled: {isOn}");
			})
		);
	}
}
