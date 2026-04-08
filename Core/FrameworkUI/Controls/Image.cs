#nullable enable

using Godot;
using ReForgeFramework.UI.Abstractions;

namespace ReForgeFramework.UI.Controls;

/// <summary>
/// 图片控件：用于显示纹理。
/// </summary>
public class Image : UiElement
{
	private readonly string? _texturePath;
	private readonly Texture2D? _texture;
	private TextureRect.StretchModeEnum _stretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
	private bool _expand;

	public Image(string texturePath)
	{
		_texturePath = texturePath;
	}

	public Image(Texture2D texture)
	{
		_texture = texture;
	}

	public Image WithStretchMode(TextureRect.StretchModeEnum stretchMode)
	{
		_stretchMode = stretchMode;
		return this;
	}

	public Image WithExpand(bool expand)
	{
		_expand = expand;
		return this;
	}

	protected override Control CreateControl()
	{
		TextureRect rect = new()
		{
			StretchMode = _stretchMode,
			ExpandMode = _expand ? TextureRect.ExpandModeEnum.IgnoreSize : TextureRect.ExpandModeEnum.KeepSize,
			MouseFilter = Control.MouseFilterEnum.Ignore
		};

		if (!string.IsNullOrEmpty(_texturePath))
		{
			if (ResourceLoader.Exists(_texturePath))
			{
				rect.Texture = ResourceLoader.Load<Texture2D>(_texturePath);
			}
		}
		else if (_texture != null)
		{
			rect.Texture = _texture;
		}

		if (!_expand && rect.Texture != null)
		{
			rect.CustomMinimumSize = rect.Texture.GetSize();
		}

		return rect;
	}
}
