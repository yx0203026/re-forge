#nullable enable

using System;
using System.Diagnostics;
using System.Text.Json;
using Godot;
using ReForgeFramework.UI.Controls;
using ReForgeFramework.ModLoading.UI;
using ReForgeFramework.UI.SystemAreas;

public static partial class ReForge
{
	private const string SettingsScreenKey = "ReForge.Settings";
	private const string ModManagerScreenKey = "ReForge.ModManager";
	private const string DevModsScreenKey = "ReForge.DevMods";
	private const string FeedbackRepositoryUrl = "https://github.com/yx0203026/re-forge";
	private static bool _settingsInitialized;
	private static readonly ReForgeSettingsData RuntimeSettings = ReForgeSettingsStore.Load();

	private static void InitializeRuntimeSettings()
	{
		if (_settingsInitialized)
		{
			return;
		}

		_settingsInitialized = true;
		RegisterSettingsUi();
	}

	private static void ApplyPostInitializationSettings()
	{
		if (RuntimeSettings.EnableDebugConsole)
		{
			DebugConsole.EnsureOpened();
		}
	}

	private static void RegisterSettingsUi()
	{
		SettingsScreenHost settingsHost = UI.GetSettingsScreen();
		SettingTabPanelHost tabHost = settingsHost.GetSettingTabPanel();
		string tabTitle = UI.T("gameplay_ui", "REFORGE.SETTINGS.TAB", "ReForge");
		tabHost.AddChild(new SettingTab(tabTitle, selected: false, screenKey: SettingsScreenKey).WithMinHeight(72f));

		SettingTab? tab = tabHost.GetSettingTab(SettingsScreenKey);
		if (tab == null)
		{
			GD.PrintErr($"[ReForge.Settings] Cannot find setting tab '{SettingsScreenKey}'.");
			return;
		}

		string optionTitle = UI.T("gameplay_ui", "REFORGE.SETTINGS.DEBUG_TITLE", "Enable Debug Console");
		SettingOptionItem debugToggle = SettingOptionItem
			.Toggle(optionTitle, RuntimeSettings.EnableDebugConsole, OnDebugToggled)
			.WithHoverTip("gameplay_ui", "REFORGE.SETTINGS.DEBUG_TIP_TITLE", "REFORGE.SETTINGS.DEBUG_TIP_DESC");

		tab.Add(debugToggle);

		string feedbackTitle = UI.T("gameplay_ui", "REFORGE.SETTINGS.FEEDBACK_TITLE", "反馈");
		string feedbackButtonText = UI.T("gameplay_ui", "REFORGE.SETTINGS.FEEDBACK_BUTTON", "反馈");
		SettingOptionItem feedbackItem = SettingOptionItem
			.FeedbackButton(feedbackTitle, feedbackButtonText, OpenFeedbackRepository)
			.WithHoverTip("gameplay_ui", "REFORGE.SETTINGS.FEEDBACK_TIP_TITLE", "REFORGE.SETTINGS.FEEDBACK_TIP_DESC");

		tab.Add(feedbackItem);

		RegisterModManagerTab(tabHost);
	}

	private static void RegisterModManagerTab(SettingTabPanelHost tabHost)
	{
		string modManagerTabTitle = UI.T("gameplay_ui", "REFORGE.MOD_MANAGER.TAB", "Mod Manager");
		tabHost.AddChild(new SettingTab(modManagerTabTitle, selected: false, screenKey: ModManagerScreenKey).WithMinHeight(72f));

		SettingTab? modManagerTab = tabHost.GetSettingTab(ModManagerScreenKey);
		if (modManagerTab == null)
		{
			GD.PrintErr($"[ReForge.Settings] Cannot find setting tab '{ModManagerScreenKey}'.");
			return;
		}

		modManagerTab.Add(new ReForgeModManagerDashboard(devMode: false));

		string devModsTabTitle = UI.T("gameplay_ui", "REFORGE.MOD_MANAGER.DEV_TAB", "My Mods");
		tabHost.AddChild(new SettingTab(devModsTabTitle, selected: false, screenKey: DevModsScreenKey).WithMinHeight(72f));

		SettingTab? devModsTab = tabHost.GetSettingTab(DevModsScreenKey);
		if (devModsTab == null)
		{
			GD.PrintErr($"[ReForge.Settings] Cannot find setting tab '{DevModsScreenKey}'.");
			return;
		}

		devModsTab.Add(new ReForgeModManagerDashboard(devMode: true));
	}

	private static void OnDebugToggled(bool enabled)
	{
		RuntimeSettings.EnableDebugConsole = enabled;
		ReForgeSettingsStore.Save(RuntimeSettings);

		if (enabled)
		{
			GD.Print("[ReForge.Settings] Debug console enabled.");
			DebugConsole.EnsureOpened();
		}
		else
		{
			GD.Print("[ReForge.Settings] Debug console disabled.");
		}
	}

	private static void OpenFeedbackRepository()
	{
		GD.Print($"[ReForge.Settings] Opening feedback repository URL: {FeedbackRepositoryUrl}");

		Error openResult = OS.ShellOpen(FeedbackRepositoryUrl);
		if (openResult == Error.Ok)
		{
			GD.Print("[ReForge.Settings] Opened feedback repository via OS.ShellOpen.");
			return;
		}

		try
		{
			Process.Start(new ProcessStartInfo
			{
				FileName = FeedbackRepositoryUrl,
				UseShellExecute = true
			});
			GD.Print("[ReForge.Settings] Opened feedback repository via Process.Start fallback.");
			return;
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[ReForge.Settings] Process.Start fallback failed for URL: {FeedbackRepositoryUrl}, Error={ex.Message}");
		}

		GD.PrintErr($"[ReForge.Settings] Failed to open feedback repository URL: {FeedbackRepositoryUrl}, Error={openResult}");
	}

	private sealed class ReForgeSettingsData
	{
		public bool EnableDebugConsole { get; set; }
	}

	private static class ReForgeSettingsStore
	{
		private const string SettingsDir = "user://reforge";
		private const string SettingsPath = "user://reforge/settings.json";

		public static ReForgeSettingsData Load()
		{
			try
			{
				if (!FileAccess.FileExists(SettingsPath))
				{
					return new ReForgeSettingsData();
				}

				using FileAccess file = FileAccess.Open(SettingsPath, FileAccess.ModeFlags.Read);
				string json = file.GetAsText();
				if (string.IsNullOrWhiteSpace(json))
				{
					return new ReForgeSettingsData();
				}

				ReForgeSettingsData? data = JsonSerializer.Deserialize<ReForgeSettingsData>(json);
				return data ?? new ReForgeSettingsData();
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[ReForge.Settings] Failed to load settings. {ex}");
				return new ReForgeSettingsData();
			}
		}

		public static void Save(ReForgeSettingsData data)
		{
			ArgumentNullException.ThrowIfNull(data);

			try
			{
				DirAccess.MakeDirRecursiveAbsolute(SettingsDir);
				JsonSerializerOptions options = new()
				{
					WriteIndented = true,
				};

				string json = JsonSerializer.Serialize(data, options);
				using FileAccess file = FileAccess.Open(SettingsPath, FileAccess.ModeFlags.Write);
				file.StoreString(json);
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[ReForge.Settings] Failed to save settings. {ex}");
			}
		}
	}
}
