using System;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;

namespace RewardRefresh.Core;

[ModInitializer(nameof(Initialize))]
public static class ModMain
{
	private static bool _initialized;
	private static Harmony _harmony;

	private static void Initialize()
	{
		if (_initialized)
		{
			return;
		}

		_initialized = true;
		_harmony = new Harmony("reward_refresh.mod");
		_harmony.PatchAll();
		GD.Print("[reward_refresh.mod] initialized.");
	}
}

