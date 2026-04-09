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

	/// <summary>
	/// 使用纹理路径初始化图片控件。
	/// </summary>
	/// <param name="texturePath">纹理资源路径。</param>
	public Image(string texturePath)
	{
		_texturePath = texturePath;
	}

	/// <summary>
	/// 使用纹理对象初始化图片控件。
	/// </summary>
	/// <param name="texture">纹理对象。</param>
	public Image(Texture2D texture)
	{
		_texture = texture;
	}

	/// <summary>
	/// 设置纹理拉伸模式。
	/// </summary>
	/// <param name="stretchMode">拉伸模式。</param>
	/// <returns>当前图片控件实例。</returns>
	public Image WithStretchMode(TextureRect.StretchModeEnum stretchMode)
	{
		_stretchMode = stretchMode;
		return this;
	}

	/// <summary>
	/// 设置是否扩展纹理尺寸。
	/// </summary>
	/// <param name="expand">是否扩展。</param>
	/// <returns>当前图片控件实例。</returns>
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
