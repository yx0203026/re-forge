#nullable enable

using Godot;
using ReForgeFramework.UI.Abstractions;

namespace ReForgeFramework.UI.Runtime;

/// <summary>
/// 将视觉配置应用到 Control，并处理可选纹理层与中心枢轴。
/// </summary>
internal static class UiVisualApplier
{
	private const string TextureNodeName = "ReForgeUiTexture";
	private static readonly StringName MetaCenterPivotBound = "__reforge_center_pivot_bound";

	public static void Apply(Control control, UiVisualOptions options)
	{
		if (options.Scale.HasValue)
		{
			control.Scale = options.Scale.Value;
		}

		if (options.SelfModulate.HasValue)
		{
			control.SelfModulate = options.SelfModulate.Value;
		}

		if (options.CenterPivot)
		{
			BindCenterPivot(control);
		}

		ApplyTexture(control, options);
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

		if (control.HasMeta(MetaCenterPivotBound))
		{
			return;
		}

		control.SetMeta(MetaCenterPivotBound, true);
		control.Resized += UpdatePivot;
	}

	private static void ApplyTexture(Control control, UiVisualOptions options)
	{
		TextureRect? textureNode = control.GetNodeOrNull<TextureRect>(TextureNodeName);
		if (options.Texture == null)
		{
			if (textureNode != null)
			{
				textureNode.QueueFree();
			}
			return;
		}

		textureNode ??= CreateTextureNode(control);
		textureNode.Texture = options.Texture;
		textureNode.StretchMode = options.TextureStretchMode;
		textureNode.ShowBehindParent = options.TextureShowBehindParent;
	}

	private static TextureRect CreateTextureNode(Control control)
	{
		TextureRect textureNode = new TextureRect
		{
			Name = TextureNodeName,
			MouseFilter = Control.MouseFilterEnum.Ignore
		};

		textureNode.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		control.AddChild(textureNode);
		control.MoveChild(textureNode, 0);
		return textureNode;
	}
}
