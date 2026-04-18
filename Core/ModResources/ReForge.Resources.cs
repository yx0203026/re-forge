#nullable enable

using Godot;
using ReForgeFramework.ModLoading;

public static partial class ReForge
{
	/// <summary>
	/// ReForge 通用资源 API 入口。
	/// </summary>
	public static class Resources
	{
		/// <summary>
		/// 从 ReForge 模组资源系统按路径读取 PackedScene（支持 PCK 与 Embedded）。
		/// </summary>
		public static PackedScene? LoadPackedSceneFromModResource(string resourcePath)
		{
			if (ReForgeModManager.TryLoadPackedScene(resourcePath, out PackedScene scene))
			{
				return scene;
			}

			return null;
		}

		/// <summary>
		/// 尝试从 ReForge 模组资源系统按路径读取 PackedScene。
		/// </summary>
		public static bool TryLoadPackedSceneFromModResource(string resourcePath, out PackedScene scene)
		{
			return ReForgeModManager.TryLoadPackedScene(resourcePath, out scene);
		}
	}
}
