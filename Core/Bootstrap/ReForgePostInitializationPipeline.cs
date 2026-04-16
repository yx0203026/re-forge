#nullable enable

using System;
using Godot;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes;
using ReForgeFramework.ModResources;

internal static class ReForgePostInitializationPipeline
{
	private static bool _scheduled;

	public static void Schedule(Action initializeRuntimeSettings, Action applyPostInitializationSettings)
	{
		ArgumentNullException.ThrowIfNull(initializeRuntimeSettings);
		ArgumentNullException.ThrowIfNull(applyPostInitializationSettings);

		if (_scheduled)
		{
			return;
		}

		_scheduled = true;
		if (Engine.GetMainLoop() is SceneTree tree)
		{
			tree.Connect(SceneTree.SignalName.ProcessFrame, Callable.From(() =>
			{
				_scheduled = false;
				ExecutePostInitialization(initializeRuntimeSettings, applyPostInitializationSettings);
			}), (uint)GodotObject.ConnectFlags.OneShot);
			return;
		}

		_scheduled = false;
		ExecutePostInitialization(initializeRuntimeSettings, applyPostInitializationSettings);
	}

	private static void ExecutePostInitialization(Action initializeRuntimeSettings, Action applyPostInitializationSettings)
	{
		RefreshLocalizationTablesForLoadedMods();
		initializeRuntimeSettings();
		applyPostInitializationSettings();
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
			LocalizationResourceBridge.RefreshCurrentLanguage();
			LocManager.Instance.SetLanguage(currentLanguage);
			NGame.Instance?.Relocalize();
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[ReForge] Failed to refresh localization tables. {ex}");
		}
	}
}
