using System;
using Godot;
using ReForgeFramework.UI.Abstractions;
using ReForgeFramework.UI.Controls;
using Image = ReForgeFramework.UI.Controls.Image;

public static partial class ReForge
{
    private static void BuildLogo()
    {
        ReForge.UI.GetMainMenuScreen().AddChild(
            new Image("res://reforge/image/reforge-logo-text.png")
                .WithAnchor(UiAnchorPreset.Center)
                .WithScale(0.4f)
                .WithPositionOffset(450, 0)
                .WithLayerPriority(120)
                .WithScope(UiVisibilityScope.MainMenuHomeOnly)
                .WithCenterPivot()
        );

        GD.Print("[ReForge] Logo built.");
    }
}
