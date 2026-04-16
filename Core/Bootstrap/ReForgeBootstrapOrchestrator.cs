#nullable enable

using System;
using Godot;
using HarmonyLib;
using ReForgeFramework.Mixins.Runtime;
using ReForgeFramework.ModLoading;

internal static class ReForgeBootstrapOrchestrator
{
	public static void InitializeCore(string harmonyId, bool strictMixinMode, Action<Harmony> onHarmonyCreated, Action initializeEventWheel)
	{
		ArgumentNullException.ThrowIfNull(harmonyId);
		ArgumentNullException.ThrowIfNull(onHarmonyCreated);
		ArgumentNullException.ThrowIfNull(initializeEventWheel);

		ReForgeBootstrapDiagnostics.TrackPhaseStart(
			phase: "InitializeCore",
			detail: $"harmonyId='{harmonyId}', strictMixinMode={strictMixinMode.ToString().ToLowerInvariant()}."
		);

		try
		{
			ReForgeBootstrapDiagnostics.TrackPhaseStart(phase: "Harmony", detail: "Creating Harmony instance and applying patches.");

			Harmony harmony = new(harmonyId);
			onHarmonyCreated(harmony);
			harmony.PatchAll();

			ReForgeBootstrapDiagnostics.TrackPhaseCompleted(phase: "Harmony", detail: "Harmony patches applied.");

			ReForgeBootstrapDiagnostics.TrackPhaseStart(phase: "Mixins.Register", detail: "Registering mixins from ReForge assembly.");
			MixinRegistrationResult mixinRegistration = ReForge.Mixins.Register(
				typeof(ReForge).Assembly,
				harmony.Id,
				harmony,
				strictMode: strictMixinMode
			);

			LogMixinRegistration(mixinRegistration);
			ExecuteCoreModules(initializeEventWheel);
			ReForgeBootstrapDiagnostics.TrackPhaseCompleted(phase: "InitializeCore", detail: "Core bootstrap completed.");
		}
		catch (Exception ex)
		{
			ReForgeBootstrapDiagnostics.TrackPhaseFailed(phase: "InitializeCore", detail: "Core bootstrap aborted.", exception: ex);
			throw;
		}
	}

	private static void ExecuteCoreModules(Action initializeEventWheel)
	{
		foreach (ReForgeBootstrapModules.BootstrapModule module in ReForgeBootstrapModules.BuildCoreModules(initializeEventWheel))
		{
			string phase = $"CoreModule.{module.Name}";
			ReForgeBootstrapDiagnostics.TrackPhaseStart(phase, $"allowDegrade={module.AllowDegrade.ToString().ToLowerInvariant()}.");

			try
			{
				module.Execute();
				ReForgeBootstrapDiagnostics.TrackPhaseCompleted(phase, "Initialized.");
			}
			catch (Exception ex)
			{
				if (module.AllowDegrade)
				{
					ReForgeBootstrapDiagnostics.TrackPhaseDegraded(phase, "Module degraded and initialization continues.", ex);
					continue;
				}

				ReForgeBootstrapDiagnostics.TrackPhaseFailed(phase, "Module initialization failed and bootstrap will stop.", ex);

				throw new InvalidOperationException($"[ReForge] Core module initialization failed. name='{module.Name}'.", ex);
			}
		}
	}

	private static void LogMixinRegistration(MixinRegistrationResult mixinRegistration)
	{
		if (mixinRegistration.Summary.Failed > 0 || mixinRegistration.State != MixinRegistrationState.Registered)
		{
			ReForgeBootstrapDiagnostics.TrackPhaseDegraded(
				phase: "Mixins.Register",
				detail: $"Registration finished with issues. installed={mixinRegistration.Summary.Installed}, failed={mixinRegistration.Summary.Failed}, skipped={mixinRegistration.Summary.Skipped}, state='{mixinRegistration.State}', message='{mixinRegistration.Message}'."
			);
			return;
		}

		ReForgeBootstrapDiagnostics.TrackPhaseCompleted(
			phase: "Mixins.Register",
			detail: $"Registered. installed={mixinRegistration.Summary.Installed}, failed={mixinRegistration.Summary.Failed}, skipped={mixinRegistration.Summary.Skipped}."
		);
	}
}
