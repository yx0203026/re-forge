using System;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Nodes;
using ReForgeFramework.EventBus.Examples;
using ReForgeFramework.Mixins.Runtime;
using ReForgeFramework.UI;
using ReForgeFramework.UI.Examples;

[ModInitializer(nameof(Initialize))]
public static partial class ReForge
{
	private const bool EnableUiDemo = false;
	private const bool EnableEventBusDemo = true;
	private const bool EnableMixinDemo = false;

	private static bool _initialized;
	private static bool _postInitializeScheduled;
	private static Harmony _harmony = null!;
	private static readonly ReForgeUiFacade _uiFacade = new();

	public static ReForgeUiFacade UI => _uiFacade;

	private static void Initialize()
	{
		if (_initialized)
		{
			return;
		}

		_initialized = true;
		_harmony = new Harmony("reforge.mod");
		_harmony.PatchAll();

		MixinRegistrationResult mixinRegistration = Mixins.Register(
			typeof(ReForge).Assembly,
			_harmony.Id,
			_harmony,
			strictMode: true
		);

		if (mixinRegistration.Summary.Failed > 0 || mixinRegistration.State != MixinRegistrationState.Registered)
		{
			GD.PrintErr(
				$"[ReForge] Mixins registration finished with issues. installed={mixinRegistration.Summary.Installed}, failed={mixinRegistration.Summary.Failed}, skipped={mixinRegistration.Summary.Skipped}, state='{mixinRegistration.State}', message='{mixinRegistration.Message}'."
			);
		}
		else
		{
			GD.Print(
				$"[ReForge] Mixins registered. installed={mixinRegistration.Summary.Installed}, failed={mixinRegistration.Summary.Failed}, skipped={mixinRegistration.Summary.Skipped}."
			);
		}

		EventBus.Initialize();
		SchedulePostInitialization();
		GD.Print("[ReForge] initialized.");
	}

	private static void SchedulePostInitialization()
	{
		if (_postInitializeScheduled)
		{
			return;
		}

		_postInitializeScheduled = true;
		if (Engine.GetMainLoop() is SceneTree tree)
		{
			tree.Connect(SceneTree.SignalName.ProcessFrame, Callable.From(() =>
			{
				_postInitializeScheduled = false;
				RefreshLocalizationTablesForLoadedMods();
				InitializeRuntimeSettings();
				ApplyPostInitializationSettings();
			}), (uint)GodotObject.ConnectFlags.OneShot);
			return;
		}

		_postInitializeScheduled = false;
		RefreshLocalizationTablesForLoadedMods();
		InitializeRuntimeSettings();
		ApplyPostInitializationSettings();
	}

	private static void RefreshLocalizationTablesForLoadedMods()
	{
		try
		{
			if (LocManager.Instance == null)
			{
				return;
			}

			string currentLanguage = LocManager.Instance.Language;
			LocManager.Instance.SetLanguage(currentLanguage);
			NGame.Instance?.Relocalize();
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[ReForge] Failed to refresh localization tables. {ex}");
		}
	}
}
