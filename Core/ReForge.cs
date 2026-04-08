using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using ReForgeFramework.EventBus.Examples;
using ReForgeFramework.Mixins.Runtime;
using ReForgeFramework.UI;
using ReForgeFramework.UI.Examples;

[ModInitializer(nameof(Initialize))]
public static partial class ReForge
{
	private const bool EnableUiDemo = true;
	private const bool EnableEventBusDemo = true;
	private const bool EnableMixinDemo = false;

	private static bool _initialized;
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
		EventExamples.Configure(EnableEventBusDemo);
		UiBootstrapExample.Bootstrap(EnableUiDemo);
		GD.Print("[ReForge] initialized.");
	}
}
