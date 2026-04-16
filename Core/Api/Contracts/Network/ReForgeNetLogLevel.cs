#nullable enable

namespace ReForgeFramework.Networking;

/// <summary>
/// 网络日志级别（与 STS2 官方 LogLevel 思路保持一致，但不强耦合官方类型）。
/// </summary>
public enum ReForgeNetLogLevel
{
	Trace = 0,
	Debug = 1,
	Info = 2,
	Warn = 3,
	Error = 4,
}
