using System;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using ReForgeFramework.Settings;

[ModInitializer(nameof(Initialize))]
public static partial class ReForge
{
	private const bool EnableEventBusDemo = true;
	private const bool EnableMixinDemo = false;

	private static bool _initialized;
	private static Harmony _harmony = null!;
	private static readonly ReForgeSettingsApi _settingsApi = new();

	public static ReForgeSettingsApi Settings => _settingsApi;

	private static void Initialize()
	{
		InitializeCoreEntry();
	}

	/// <summary>
	/// 旧入口兼容层：保持历史调用可用，并输出迁移建议。
	/// </summary>
	public static void InitializeCompatEntry()
	{
		ReForgeEntryCompatibilityFacade.InitializeCompatEntry(InitializeCoreEntry);
	}

	private static void InitializeCoreEntry()
	{
		if (_initialized)
		{
			return;
		}

		_initialized = true;
		try
		{
			ReForgeBootstrapOrchestrator.InitializeCore(
				harmonyId: "reforge.mod",
				strictMixinMode: true,
				onHarmonyCreated: harmony => _harmony = harmony,
				initializeEventWheel: InitializeEventWheelSafely
			);
			ReForgePostInitializationPipeline.Schedule(
				initializeRuntimeSettings: InitializeRuntimeSettings,
				applyPostInitializationSettings: ApplyPostInitializationSettings
			);
			GD.Print("[ReForge] initialized.");
		}
		catch (Exception ex)
		{
			_initialized = false;
			GD.PrintErr($"[ReForge] InitializeCoreEntry failed and initialization gate was reset. {ex.GetType().Name}: {ex.Message}");
			throw;
		}
	}

	private static void InitializeEventWheelSafely()
	{
		try
		{
			EventWheel.Initialize();
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[ReForge] EventWheel initialization threw unexpectedly and has been degraded. {ex.GetType().Name}: {ex.Message}");
		}
	}

}
