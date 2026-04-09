#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

public static partial class ReForge
{
	/// <summary>
	/// Shadow 特性：声明 Mixin 静态字段与目标类型字段之间的映射关系。
	/// </summary>
	/// <remarks>
	/// 此特性用于在 Mixin 类中声明字段，其将自动映射到目标类型中的字段。
	/// 通过 FieldInfo 的反射，可以访问目标类型的私有字段而无需修改目标类型。
	/// 
	/// 使用场景：
	/// • 访问目标类型的私有字段
	/// • 读取或修改目标对象的内部状态
	/// • 实现数据级别的植入与拦截
	/// </remarks>
	[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
	public sealed class ShadowAttribute : Attribute
	{
		/// <summary>
		/// 初始化 <see cref="ShadowAttribute"/> 的新实例。
		/// </summary>
		/// <param name="targetName">
		/// 目标字段的名称。若为 null 或仅包含空白，系统将按以下规则推断：
		/// 1. 移除 Mixin 字段名中的 "shadow$" 前缀（如有）
		/// 2. 使用剩余字段名作为目标字段名
		/// </param>
		/// <param name="aliases">
		/// 目标字段的备用名称列表，按声明顺序作为回退方案。
		/// 当主名称不存在时，系统将按顺序尝试这些别名。
		/// </param>
		public ShadowAttribute(string? targetName = null, params string[] aliases)
		{
			TargetName = string.IsNullOrWhiteSpace(targetName)
				? string.Empty
				: targetName.Trim();
			Aliases = NormalizeAliases(aliases);
		}

		/// <summary>
		/// 获取目标字段的主名称。
		/// </summary>
		/// <remarks>
		/// 若值为空字符串，运行时将根据 Mixin 字段名推断实际的目标字段名。
		/// </remarks>
		public string TargetName { get; }

		/// <summary>
		/// 获取目标字段的备用名称列表（按声明顺序）。
		/// </summary>
		/// <remarks>
		/// 当主名称 (<see cref="TargetName"/>) 无法匹配时，系统将尝试这些别名。
		/// 重复的名称会被自动去重，空白值会被自动剔除。
		/// </remarks>
		public IReadOnlyList<string> Aliases { get; }

		/// <summary>
		/// 获取或设置当目标字段不存在时是否允许跳过绑定。默认为 false。
		/// </summary>
		/// <remarks>
		/// 启用此选项时，若目标字段在目标类型中不存在，
		/// Mixin 系统将记录警告但继续执行，而非中止安装。
		/// </remarks>
		public bool Optional { get; set; }

		/// <summary>
		/// 规范化别名列表：移除 null 值、去重、修剪空白符。
		/// </summary>
		private static IReadOnlyList<string> NormalizeAliases(string[]? aliases)
		{
			if (aliases == null || aliases.Length == 0)
			{
				return Array.Empty<string>();
			}

			List<string> normalized = new(aliases.Length);
			HashSet<string> dedup = new(StringComparer.Ordinal);
			for (int i = 0; i < aliases.Length; i++)
			{
				string? alias = aliases[i];
				if (string.IsNullOrWhiteSpace(alias))
				{
					continue;
				}

				string trimmed = alias.Trim();
				if (!dedup.Add(trimmed))
				{
					continue;
				}

				normalized.Add(trimmed);
			}

			if (normalized.Count == 0)
			{
				return Array.Empty<string>();
			}

			return new ReadOnlyCollection<string>(normalized);
		}
	}
}
