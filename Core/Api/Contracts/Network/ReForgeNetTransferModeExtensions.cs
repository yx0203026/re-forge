#nullable enable

using System;

namespace ReForgeFramework.Networking;

public static class ReForgeNetTransferModeExtensions
{
	/// <summary>
	/// 将传输模式映射到默认通道。
	/// </summary>
	public static int ToChannelId(this ReForgeNetTransferMode mode)
	{
		return mode switch
		{
			ReForgeNetTransferMode.Reliable => 0,
			ReForgeNetTransferMode.Unreliable => 1,
			_ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
		};
	}
}
