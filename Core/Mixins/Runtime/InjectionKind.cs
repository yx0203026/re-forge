#nullable enable

namespace ReForgeFramework.Mixins.Runtime;

/// <summary>
/// 统一注入语义枚举，定义所有支持的注入类型。
/// 供扫描器与 Harmony 绑定层使用。
/// </summary>
/// <remarks>
/// 每种注入类型对应 Harmony 的不同 patch 方法：
/// • Prefix：在目标方法前执行
/// • Postfix：在目标方法后执行
/// • Finalizer：方法结束后执行（支持异常处理）
/// • Redirect：完全替换目标方法
/// • ModifyArg：修改方法参数
/// • ModifyConstant：修改常量值
/// • Overwrite：使用 Transpiler 重写 IL
/// </remarks>
public enum InjectionKind
{
	/// <summary>
	/// 没有指定或无效的注入类型。不应在实际注入中使用，仅用于错误检测。
	/// </summary>
	Unknown = 0,

	/// <summary>
	/// Prefix 注入：在目标方法执行前调用。
	/// 处理方法可通过返回 false 阻止目标方法执行。
	/// </summary>
	InjectPrefix = 1,

	/// <summary>
	/// Postfix 注入：在目标方法正常执行后调用。
	/// 处理方法可修改返回值。
	/// </summary>
	InjectPostfix = 2,

	/// <summary>
	/// Finalizer 注入：在目标方法执行后调用（即使方法抛出异常也执行）。
	/// 处理方法可捕获并处理异常，流程类似 finally 块。
	/// </summary>
	InjectFinalizer = 3,

	/// <summary>
	/// Redirect 注入：完全替换对目标方法的调用。
	/// 所有调用都会被重定向到处理方法。
	/// </summary>
	Redirect = 4,

	/// <summary>
	/// ModifyArg 注入：在目标方法调用前修改指定的参数值。
	/// 通过 Transpiler 在 IL 级别实现参数篡改。
	/// </summary>
	ModifyArg = 5,

	/// <summary>
	/// ModifyConstant 注入：在目标方法中修改使用的常量值。
	/// 通过 Transpiler 在 IL 级别查找并修改常量。
	/// </summary>
	ModifyConstant = 6,

	/// <summary>
	/// Overwrite 注入：使用自定义 Transpiler 完全重写目标方法的 IL 代码。
	/// 处理方法应返回修改后的 IL 指令序列。
	/// </summary>
	Overwrite = 7,
}
