#nullable enable

using System;

public static partial class ReForge
{
	/// <summary>
	/// 声明一个 Mixin 类及其目标类型。
	/// 使用方式： [ReForge.Mixin(typeof(TargetType))]
	/// </summary>
	[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
	public sealed class MixinAttribute : Attribute
	{
		public MixinAttribute(Type targetType)
		{
			ArgumentNullException.ThrowIfNull(targetType);
			TargetType = targetType;
		}

		/// <summary>
		/// 目标类型（必填）。
		/// </summary>
		public Type TargetType { get; }

		/// <summary>
		/// 可选 Mixin 标识；为空时可由扫描器回退到类型全名。
		/// </summary>
		public string Id { get; set; } = string.Empty;

		/// <summary>
		/// 注入优先级（越大越后执行）。
		/// </summary>
		public int Priority { get; set; } = 400;

		/// <summary>
		/// 严格模式：true 时注册失败可触发上层中止策略。
		/// </summary>
		public bool StrictMode { get; set; } = true;
	}
}
