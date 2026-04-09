#nullable enable

using System;
using ReForgeFramework.Mixins.Runtime;

public static partial class ReForge
{
	/// <summary>
	/// 所有方法级注入特性的统一基类，扫描器可直接读取 Kind 与通用字段。
	/// </summary>
	/// <remarks>
	/// 此类定义了注入的基本语义，所有具体注入类型（Prefix、Postfix、Finalizer、Inject 等）
	/// 都应继承自此基类以确保统一的扫描与验证流程。
	/// </remarks>
	[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
	public abstract class InjectionAttributeBase : Attribute
	{
		/// <summary>
		/// 初始化 <see cref="InjectionAttributeBase"/> 的新实例。
		/// </summary>
		/// <param name="kind">注入语义类型，<see cref="InjectionKind.Unknown"/> 会导致异常。</param>
		/// <param name="targetMethod">目标方法名称，不可为空或仅空白。</param>
		/// <exception cref="ArgumentException">当 kind 为 Unknown 或 targetMethod 为空时引发。</exception>
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
		/// 获取统一注入语义枚举，供运行时映射到 Harmony。
		/// </summary>
		public InjectionKind Kind { get; }

		/// <summary>
		/// 获取目标方法名（可由后续扫描器扩展为签名匹配）。
		/// </summary>
		public string TargetMethod { get; }

		/// <summary>
		/// 获取或设置可选注入锚点描述（如 IL 标记、调用点标识），默认为空。
		/// </summary>
		public string At { get; set; } = string.Empty;

		/// <summary>
		/// 获取或设置可选匹配序号。-1 代表不指定特定序号；当同名方法存在多个重载时
		/// 通过此字段明确选择目标。
		/// </summary>
		public int Ordinal { get; set; } = -1;

		/// <summary>
		/// 获取或设置注入优先级，默认为 400（Harmony 标准中位优先级）。
		/// 数值越大，注入执行越晚。
		/// </summary>
		public int Priority { get; set; } = 400;

		/// <summary>
		/// 获取或设置是否允许当找不到目标方法时跳过此注入。默认为 false。
		/// 启用此选项可在宽松模式下忽略注入失败。
		/// </summary>
		public bool Optional { get; set; }

		/// <summary>
		/// 验证给定的字符串值不为 null 且不仅包含空白字符。
		/// </summary>
		/// <param name="value">待验证的字符串。</param>
		/// <param name="paramName">参数名称，用于异常消息中。</param>
		/// <exception cref="ArgumentNullException">当 value 为 null 时。</exception>
		/// <exception cref="ArgumentException">当 value 为空或仅为空白时。</exception>
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
	/// 通用 Inject 特性，支持 Prefix、Postfix、Finalizer 三种注入阶段。
	/// 提供灵活的方式指定目标方法与注入类型。
	/// </summary>
	/// <remarks>
	/// 此特性是一个多用途的注入入口点，允许开发者根据需要选择合适的注入阶段。
	/// 若只关心特定阶段，可使用对应的专用特性（<see cref="PrefixAttribute"/>、
	/// <see cref="PostfixAttribute"/> 或 <see cref="FinalizerAttribute"/>）以获得更好的可读性。
	/// </remarks>
	[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
	public sealed class InjectAttribute : InjectionAttributeBase
	{
		/// <summary>
		/// 初始化 <see cref="InjectAttribute"/> 的新实例。
		/// </summary>
		/// <param name="targetMethod">要注入的目标方法名称。</param>
		/// <param name="injectKind">注入阶段，默认为 <see cref="InjectionKind.InjectPrefix"/>。</param>
		/// <exception cref="ArgumentException">当 injectKind 不是 Prefix、Postfix 或 Finalizer 时。</exception>
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

	/// <summary>
	/// Prefix 注入特性：在目标方法执行前调用处理方法。
	/// </summary>
	/// <remarks>
	/// Prefix 方法可通过返回 false 来阻止目标方法的执行。
	/// </remarks>
	[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
	public sealed class PrefixAttribute : InjectionAttributeBase
	{
		/// <summary>
		/// 初始化 <see cref="PrefixAttribute"/> 的新实例。
		/// </summary>
		/// <param name="targetMethod">要注入的目标方法名称。</param>
		public PrefixAttribute(string targetMethod)
			: base(InjectionKind.InjectPrefix, targetMethod)
		{
		}
	}

	/// <summary>
	/// Postfix 注入特性：在目标方法执行后调用处理方法。
	/// </summary>
	/// <remarks>
	/// Postfix 方法可以访问原方法的返回值，并可修改它。
	/// </remarks>
	[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
	public sealed class PostfixAttribute : InjectionAttributeBase
	{
		/// <summary>
		/// 初始化 <see cref="PostfixAttribute"/> 的新实例。
		/// </summary>
		/// <param name="targetMethod">要注入的目标方法名称。</param>
		public PostfixAttribute(string targetMethod)
			: base(InjectionKind.InjectPostfix, targetMethod)
		{
		}
	}

	/// <summary>
	/// Finalizer 注入特性：在目标方法执行后调用（即使发生异常也会执行）。
	/// </summary>
	/// <remarks>
	/// Finalizer 方法可以捕获异常并进行处理，流程类似于 finally 块。
	/// </remarks>
	[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
	public sealed class FinalizerAttribute : InjectionAttributeBase
	{
		/// <summary>
		/// 初始化 <see cref="FinalizerAttribute"/> 的新实例。
		/// </summary>
		/// <param name="targetMethod">要注入的目标方法名称。</param>
		public FinalizerAttribute(string targetMethod)
			: base(InjectionKind.InjectFinalizer, targetMethod)
		{
		}
	}

	/// <summary>
	/// Redirect 注入特性：完全替换目标方法的实现为自定义处理器。
	/// </summary>
	/// <remarks>
	/// 此注入类型会将对目标方法的所有调用重定向到处理器方法。
	/// 处理器方法的签名应与目标方法完全相同（包括返回值类型）。
	/// </remarks>
	[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
	public sealed class RedirectAttribute : InjectionAttributeBase
	{
		/// <summary>
		/// 初始化 <see cref="RedirectAttribute"/> 的新实例。
		/// </summary>
		/// <param name="targetMethod">要重定向的目标方法名称。</param>
		/// <param name="at">IL 锚点标记或调用点描述。</param>
		/// <exception cref="ArgumentException">当 at 为空或仅为空白时。</exception>
		public RedirectAttribute(string targetMethod, string at)
			: base(InjectionKind.Redirect, targetMethod)
		{
			ValidateRequiredText(at, nameof(at));
			At = at;
		}
	}

	/// <summary>
	/// ModifyArg 注入特性：在目标方法调用前修改指定参数。
	/// </summary>
	/// <remarks>
	/// 处理器方法应接收原参数值，并返回修改后的值。支持参数类型转换。
	/// </remarks>
	[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
	public sealed class ModifyArgAttribute : InjectionAttributeBase
	{
		/// <summary>
		/// 初始化 <see cref="ModifyArgAttribute"/> 的新实例。
		/// </summary>
		/// <param name="targetMethod">要修改参数的目标方法名称。</param>
		/// <param name="argumentIndex">要修改的参数位置（从 0 开始）。</param>
		/// <exception cref="ArgumentOutOfRangeException">当 argumentIndex 小于 0 时。</exception>
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
		/// 获取要修改的参数位置（从 0 开始）。
		/// </summary>
		public int ArgumentIndex { get; }
	}

	/// <summary>
	/// ModifyConstant 注入特性：在目标方法返回前修改常量值。
	/// </summary>
	/// <remarks>
	/// 此特性通过修改 IL 级别的常量值来改变方法行为。
	/// 处理器方法应接收原常量值，并返回修改后的值。
	/// </remarks>
	[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
	public sealed class ModifyConstantAttribute : InjectionAttributeBase
	{
		/// <summary>
		/// 初始化 <see cref="ModifyConstantAttribute"/> 的新实例。
		/// </summary>
		/// <param name="targetMethod">包含常量的目标方法名称。</param>
		public ModifyConstantAttribute(string targetMethod)
			: base(InjectionKind.ModifyConstant, targetMethod)
		{
		}

		/// <summary>
		/// 获取或设置常量匹配表达式（例如 "42"、"true"、"MyText"）。
		/// 用于在 IL 代码中精确定位要修改的常量。
		/// </summary>
		public string ConstantExpression { get; set; } = string.Empty;
	}

	/// <summary>
	/// Overwrite 注入特性：使用完全自定义逻辑覆盖目标方法。
	/// </summary>
	/// <remarks>
	/// 此特性允许通过 Transpiler 完全重写目标方法的 IL 代码。
	/// 处理器方法应是一个 Transpiler 方法，通常用于高级 IL 操作。
	/// </remarks>
	[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
	public sealed class OverwriteAttribute : InjectionAttributeBase
	{
		/// <summary>
		/// 初始化 <see cref="OverwriteAttribute"/> 的新实例。
		/// </summary>
		/// <param name="targetMethod">要完全覆盖的目标方法名称。</param>
		public OverwriteAttribute(string targetMethod)
			: base(InjectionKind.Overwrite, targetMethod)
		{
		}
	}
}
