#nullable enable

using Godot;
using ReForgeFramework.Settings.Abstractions;

namespace ReForgeFramework.Settings.Runtime;

/// <summary>
/// 灏嗚瑙夐厤缃簲鐢ㄥ埌 Control锛屽苟澶勭悊鍙€夌汗鐞嗗眰涓庝腑蹇冩灑杞淬€?/// </summary>
internal static class UiVisualApplier
{
	private const string TextureNodeName = "ReForgeUiTexture";
	private static readonly StringName MetaPivotAnchorBound = "__reforge_pivot_anchor_bound";
	private static readonly StringName MetaPivotAnchorPreset = "__reforge_pivot_anchor_preset";
	private static readonly StringName MetaVisibilityScope = "__reforge_visibility_scope";

	/// <summary>
	/// 灏嗚瑙夐厤缃簲鐢ㄥ埌鎺т欢銆?	/// </summary>
	/// <param name="control">鐩爣鎺т欢銆?/param>
	/// <param name="options">瑙嗚閰嶇疆銆?/param>
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

		if (options.LayerPriority.HasValue)
		{
			control.ZIndex = options.LayerPriority.Value;
			control.ZAsRelative = options.LayerPriorityRelative;
		}

		control.SetMeta(MetaVisibilityScope, (int)options.VisibilityScope);

		if (options.PivotAnchorPreset is UiPivotAnchorPreset pivotAnchor)
		{
			BindPivotAnchor(control, pivotAnchor);
		}
		else if (options.CenterPivot)
		{
			BindPivotAnchor(control, UiPivotAnchorPreset.Center);
		}

		ApplyTexture(control, options);
	}

	internal static UiVisibilityScope GetVisibilityScope(Control control)
	{
		if (!control.HasMeta(MetaVisibilityScope))
		{
			return UiVisibilityScope.Always;
		}

		return (UiVisibilityScope)(int)control.GetMeta(MetaVisibilityScope);
	}

	private static void BindPivotAnchor(Control control, UiPivotAnchorPreset preset)
	{
		void UpdatePivot()
		{
			UpdatePivotOffset(control);
		}

		control.SetMeta(MetaPivotAnchorPreset, (int)preset);

		UpdatePivot();
		Callable.From(UpdatePivot).CallDeferred();

		if (control.HasMeta(MetaPivotAnchorBound))
		{
			return;
		}

		control.SetMeta(MetaPivotAnchorBound, true);
		control.Resized += UpdatePivot;
	}

	private static void UpdatePivotOffset(Control control)
	{
		if (!GodotObject.IsInstanceValid(control))
		{
			return;
		}

		UiPivotAnchorPreset preset = control.HasMeta(MetaPivotAnchorPreset)
			? (UiPivotAnchorPreset)(int)control.GetMeta(MetaPivotAnchorPreset)
			: UiPivotAnchorPreset.Center;

		Vector2 pivotFactor = MapPivotFactor(preset);
		control.PivotOffset = new Vector2(control.Size.X * pivotFactor.X, control.Size.Y * pivotFactor.Y);
	}

	private static Vector2 MapPivotFactor(UiPivotAnchorPreset preset)
	{
		return preset switch
		{
			UiPivotAnchorPreset.TopLeft => new Vector2(0f, 0f),
			UiPivotAnchorPreset.TopCenter => new Vector2(0.5f, 0f),
			UiPivotAnchorPreset.TopRight => new Vector2(1f, 0f),
			UiPivotAnchorPreset.CenterLeft => new Vector2(0f, 0.5f),
			UiPivotAnchorPreset.Center => new Vector2(0.5f, 0.5f),
			UiPivotAnchorPreset.CenterRight => new Vector2(1f, 0.5f),
			UiPivotAnchorPreset.BottomLeft => new Vector2(0f, 1f),
			UiPivotAnchorPreset.BottomCenter => new Vector2(0.5f, 1f),
			UiPivotAnchorPreset.BottomRight => new Vector2(1f, 1f),
			_ => new Vector2(0.5f, 0.5f)
		};
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

