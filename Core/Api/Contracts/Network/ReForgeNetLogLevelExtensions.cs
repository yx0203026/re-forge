#nullable enable

using MegaCrit.Sts2.Core.Logging;

namespace ReForgeFramework.Networking;

public static class ReForgeNetLogLevelExtensions
{
	public static LogLevel ToOfficial(this ReForgeNetLogLevel level)
	{
		return level switch
		{
			ReForgeNetLogLevel.Trace => LogLevel.VeryDebug,
			ReForgeNetLogLevel.Debug => LogLevel.Debug,
			ReForgeNetLogLevel.Info => LogLevel.Info,
			ReForgeNetLogLevel.Warn => LogLevel.Warn,
			ReForgeNetLogLevel.Error => LogLevel.Error,
			_ => LogLevel.Debug
		};
	}
}
