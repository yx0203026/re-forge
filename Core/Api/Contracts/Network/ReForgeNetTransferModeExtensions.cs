#nullable enable

using MegaCrit.Sts2.Core.Multiplayer.Transport;

namespace ReForgeFramework.Networking;

public static class ReForgeNetTransferModeExtensions
{
	public static int ToChannelId(this ReForgeNetTransferMode mode)
	{
		return mode switch
		{
			ReForgeNetTransferMode.Unreliable => 1,
			_ => 0
		};
	}

	public static NetTransferMode ToOfficial(this ReForgeNetTransferMode mode)
	{
		return mode switch
		{
			ReForgeNetTransferMode.Unreliable => NetTransferMode.Unreliable,
			_ => NetTransferMode.Reliable
		};
	}
}
