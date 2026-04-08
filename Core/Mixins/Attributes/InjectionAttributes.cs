#nullable enable

using System;
using ReForgeFramework.Mixins.Runtime;

public static partial class ReForge
{
	/// <summary>
	/// 所有方法级注入特性的统一基类，扫描器可直接读取 Kind 与通用字段。
	/// </summary>
	[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
	public abstract class InjectionAttributeBase : Attribute
	{
		protected InjectionAttributeBase(InjectionKind kind, string targetMethod)
		{
			if (kind == InjectionKind.Unknown)
			{
				throw new ArgumentException("Injection kind cannot be Unknown.", nameof(kind));
			}

			ValidateRequiredText(targetMethod, nameof(targetMethod));
			Kind = kind;
			TargetMethod = targetMethod;
		}

		/// <summary>
		/// 统一语义枚举，供运行时映射到 Harmony。
		/// </summary>
		public InjectionKind Kind { get; }

		/// <summary>
		/// 目标方法名（可由后续扫描器扩展为签名匹配）。
		/// </summary>
		public string TargetMethod { get; }

		/// <summary>
		/// 可选注入锚点描述（如 IL 标记、调用点标识）。
		/// </summary>
		public string At { get; set; } = string.Empty;

		/// <summary>
		/// 可选匹配序号，-1 代表不指定。
		/// </summary>
		public int Ordinal { get; set; } = -1;

		/// <summary>
		/// 可选优先级，默认与 Harmony 常见中位优先级一致。
		/// </summary>
		public int Priority { get; set; } = 400;

		/// <summary>
		/// 是否允许找不到目标时跳过（宽松模式辅助字段）。
		/// </summary>
		public bool Optional { get; set; }

		protected static void ValidateRequiredText(string value, string paramName)
		{
			ArgumentNullException.ThrowIfNull(value);
			if (string.IsNullOrWhiteSpace(value))
			{
				throw new ArgumentException("Value cannot be empty.", paramName);
			}
		}
	}

	/// <summary>
	/// 通用 Inject 入口，支持 Prefix/Postfix/Finalizer 三种注入阶段。
	/// </summary>
	[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
	public sealed class InjectAttribute : InjectionAttributeBase
	{
		public InjectAttribute(string targetMethod, InjectionKind injectKind = InjectionKind.InjectPrefix)
			: base(ValidateInjectKind(injectKind), targetMethod)
		{
		}

		private static InjectionKind ValidateInjectKind(InjectionKind kind)
		{
			return kind switch
			{
				InjectionKind.InjectPrefix => kind,
				InjectionKind.InjectPostfix => kind,
				InjectionKind.InjectFinalizer => kind,
				_ => throw new ArgumentException(
					"Inject only supports InjectPrefix, InjectPostfix, or InjectFinalizer.",
					nameof(kind)
				),
			};
		}
	}

	[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
	public sealed class PrefixAttribute : InjectionAttributeBase
	{
		public PrefixAttribute(string targetMethod)
			: base(InjectionKind.InjectPrefix, targetMethod)
		{
		}
	}

	[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
	public sealed class PostfixAttribute : InjectionAttributeBase
	{
		public PostfixAttribute(string targetMethod)
			: base(InjectionKind.InjectPostfix, targetMethod)
		{
		}
	}

	[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
	public sealed class FinalizerAttribute : InjectionAttributeBase
	{
		public FinalizerAttribute(string targetMethod)
			: base(InjectionKind.InjectFinalizer, targetMethod)
		{
		}
	}

	[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
	public sealed class RedirectAttribute : InjectionAttributeBase
	{
		public RedirectAttribute(string targetMethod, string at)
			: base(InjectionKind.Redirect, targetMethod)
		{
			ValidateRequiredText(at, nameof(at));
			At = at;
		}
	}

	[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
	public sealed class ModifyArgAttribute : InjectionAttributeBase
	{
		public ModifyArgAttribute(string targetMethod, int argumentIndex)
			: base(InjectionKind.ModifyArg, targetMethod)
		{
			if (argumentIndex < 0)
			{
				throw new ArgumentOutOfRangeException(nameof(argumentIndex), "Value must be greater than or equal to 0.");
			}

			ArgumentIndex = argumentIndex;
		}

		/// <summary>
		/// 要修改的参数位置（从 0 开始）。
		/// </summary>
		public int ArgumentIndex { get; }
	}

	[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
	public sealed class ModifyConstantAttribute : InjectionAttributeBase
	{
		public ModifyConstantAttribute(string targetMethod)
			: base(InjectionKind.ModifyConstant, targetMethod)
		{
		}

		/// <summary>
		/// 常量匹配表达式（例如 "42"、"true"、"MyText"）。
		/// </summary>
		public string ConstantExpression { get; set; } = string.Empty;
	}

	[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
	public sealed class OverwriteAttribute : InjectionAttributeBase
	{
		public OverwriteAttribute(string targetMethod)
			: base(InjectionKind.Overwrite, targetMethod)
		{
		}
	}
}
