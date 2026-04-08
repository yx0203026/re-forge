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
            new Image("res://icon.svg")
                .WithAnchor(UiAnchorPreset.Center)
                .WithScale(2.0f)
                .WithCenterPivot()
        );

        GD.Print("[ReForge] Logo built.");
    }
}
