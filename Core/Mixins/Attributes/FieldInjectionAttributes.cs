#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

public static partial class ReForge
{
	/// <summary>
	/// 声明 Mixin 静态字段与目标类型字段的映射关系。
	/// </summary>
	[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
	public sealed class ShadowAttribute : Attribute
	{
		public ShadowAttribute(string? targetName = null, params string[] aliases)
		{
			TargetName = string.IsNullOrWhiteSpace(targetName)
				? string.Empty
				: targetName.Trim();
			Aliases = NormalizeAliases(aliases);
		}

		/// <summary>
		/// 目标字段主名称；空值代表运行时按字段名（含 shadow$ 前缀回退）推断。
		/// </summary>
		public string TargetName { get; }

		/// <summary>
		/// 目标字段候选别名，按声明顺序参与解析。
		/// </summary>
		public IReadOnlyList<string> Aliases { get; }

		/// <summary>
		/// 标记是否允许字段缺失时跳过绑定。
		/// </summary>
		public bool Optional { get; set; }

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
