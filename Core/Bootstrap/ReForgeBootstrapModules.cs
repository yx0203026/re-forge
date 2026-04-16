#nullable enable

using System;
using System.Collections.Generic;
using ReForgeFramework.ModLoading;

internal static class ReForgeBootstrapModules
{
	internal sealed record BootstrapModule(string Name, Action Execute, bool AllowDegrade);

	public static IReadOnlyList<BootstrapModule> BuildCoreModules(Action initializeEventWheel)
	{
		ArgumentNullException.ThrowIfNull(initializeEventWheel);

		return
		[
			new BootstrapModule("EventBus", () => ReForge.EventBus.Initialize(), AllowDegrade: false),
			new BootstrapModule("Network", () => ReForge.Network.Initialize(), AllowDegrade: false),
			new BootstrapModule("BattleEvents", () => ReForge.BattleEvents.Initialize(), AllowDegrade: false),
			new BootstrapModule("ModManager", () => ReForgeModManager.Initialize(), AllowDegrade: false),
			new BootstrapModule("EventWheel", initializeEventWheel, AllowDegrade: true)
		];
	}
}
