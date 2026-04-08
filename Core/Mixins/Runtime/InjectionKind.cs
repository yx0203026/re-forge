#nullable enable

namespace ReForgeFramework.Mixins.Runtime;

/// <summary>
/// 统一注入语义枚举，供扫描器与 Harmony 绑定层使用。
/// </summary>
public enum InjectionKind
{
	Unknown = 0,
	InjectPrefix = 1,
	InjectPostfix = 2,
	InjectFinalizer = 3,
	Redirect = 4,
	ModifyArg = 5,
	ModifyConstant = 6,
	Overwrite = 7,
}
