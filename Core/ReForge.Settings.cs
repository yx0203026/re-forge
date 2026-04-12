#nullable enable

using System;
using System.Diagnostics;
using System.Text.Json;
using Godot;
using ReForgeFramework.ModLoading.UI;
using ReForgeFramework.Settings;

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
		Settings.Initialize();

		string tabTitle = Settings.T("gameplay_ui", "REFORGE.SETTINGS.TAB", "ReForge");
		string optionTitle = Settings.T("gameplay_ui", "REFORGE.SETTINGS.DEBUG_TITLE", "Enable Debug Console");
		string optionIndex8BitTitle = Settings.T("gameplay_ui", "REFORGE.SETTINGS.OPTION_INDEX_8BIT_TITLE", "Enable 8-bit Event Option Index (MP Compatibility Risk)");
		string feedbackTitle = Settings.T("gameplay_ui", "REFORGE.SETTINGS.FEEDBACK_TITLE", "反馈");
		string feedbackButtonText = Settings.T("gameplay_ui", "REFORGE.SETTINGS.FEEDBACK_BUTTON", "反馈");
		string noSteamTitle = Settings.T("gameplay_ui", "REFORGE.SETTINGS.NO_STEAM_TITLE", "Launch No-Steam Instance");
		string noSteamButtonText = Settings.T("gameplay_ui", "REFORGE.SETTINGS.NO_STEAM_BUTTON", "Launch");

		Settings.Page(SettingsScreenKey, tabTitle, selected: false)
			.AddToggle(
				title: optionTitle,
				initialValue: RuntimeSettings.EnableDebugConsole,
				onToggled: OnDebugToggled,
				tipLocTable: "gameplay_ui",
				tipTitleEntryKey: "REFORGE.SETTINGS.DEBUG_TIP_TITLE",
				tipDescriptionEntryKey: "REFORGE.SETTINGS.DEBUG_TIP_DESC")
			.AddToggle(
				title: optionIndex8BitTitle,
				initialValue: RuntimeSettings.EnableOptionIndex8BitPatch,
				onToggled: OnOptionIndex8BitPatchToggled,
				tipLocTable: "gameplay_ui",
				tipTitleEntryKey: "REFORGE.SETTINGS.OPTION_INDEX_8BIT_TIP_TITLE",
				tipDescriptionEntryKey: "REFORGE.SETTINGS.OPTION_INDEX_8BIT_TIP_DESC")
			.AddFeedbackButton(
				title: feedbackTitle,
				buttonText: feedbackButtonText,
				onPressed: OpenFeedbackRepository,
				tipLocTable: "gameplay_ui",
				tipTitleEntryKey: "REFORGE.SETTINGS.FEEDBACK_TIP_TITLE",
				tipDescriptionEntryKey: "REFORGE.SETTINGS.FEEDBACK_TIP_DESC")
			.AddFeedbackButton(
				title: noSteamTitle,
				buttonText: noSteamButtonText,
				onPressed: LaunchNoSteamInstance,
				tipLocTable: "gameplay_ui",
				tipTitleEntryKey: "REFORGE.SETTINGS.NO_STEAM_TIP_TITLE",
				tipDescriptionEntryKey: "REFORGE.SETTINGS.NO_STEAM_TIP_DESC");

		RegisterModManagerTab();
	}

	private static void RegisterModManagerTab()
	{
		string modManagerTabTitle = Settings.T("gameplay_ui", "REFORGE.MOD_MANAGER.TAB", "Mod Manager");
		Settings.Page(ModManagerScreenKey, modManagerTabTitle, selected: false)
			.AddElement(new ReForgeModManagerDashboard(devMode: false));

		string devModsTabTitle = Settings.T("gameplay_ui", "REFORGE.MOD_MANAGER.DEV_TAB", "My Mods");
		Settings.Page(DevModsScreenKey, devModsTabTitle, selected: false)
			.AddElement(new ReForgeModManagerDashboard(devMode: true));
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

	private static void OnOptionIndex8BitPatchToggled(bool enabled)
	{
		RuntimeSettings.EnableOptionIndex8BitPatch = enabled;
		ReForgeSettingsStore.Save(RuntimeSettings);

		if (enabled)
		{
			GD.Print("[ReForge.Settings] OptionIndex 8-bit patch enabled. Restart required to apply and all peers should use the same setting.");
			return;
		}

		GD.Print("[ReForge.Settings] OptionIndex 8-bit patch disabled. Restart required to apply.");
	}

	internal static bool IsOptionIndex8BitPatchEnabled()
	{
		return RuntimeSettings.EnableOptionIndex8BitPatch;
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

	private static void LaunchNoSteamInstance()
	{
		string executablePath = OS.GetExecutablePath();
		if (string.IsNullOrWhiteSpace(executablePath) || !System.IO.File.Exists(executablePath))
		{
			GD.PrintErr($"[ReForge.Settings] Cannot launch no-Steam instance: invalid executable path '{executablePath}'.");
			return;
		}

		string? workingDirectory = System.IO.Path.GetDirectoryName(executablePath);
		if (string.IsNullOrWhiteSpace(workingDirectory) || !System.IO.Directory.Exists(workingDirectory))
		{
			GD.PrintErr($"[ReForge.Settings] Cannot launch no-Steam instance: invalid working directory '{workingDirectory}'.");
			return;
		}

		try
		{
			ProcessStartInfo startInfo = new()
			{
				FileName = executablePath,
				WorkingDirectory = workingDirectory,
				UseShellExecute = false
			};
			startInfo.ArgumentList.Add("--force-steam=off");

			using Process? launchedProcess = Process.Start(startInfo);
			if (launchedProcess == null)
			{
				GD.PrintErr("[ReForge.Settings] Failed to start no-Steam instance process.");
				return;
			}

			GD.Print($"[ReForge.Settings] Launched no-Steam instance. pid={launchedProcess.Id}, exe='{executablePath}'.");
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[ReForge.Settings] Failed to launch no-Steam instance. {ex}");
		}
	}

	private sealed class ReForgeSettingsData
	{
		public bool EnableDebugConsole { get; set; }

		public bool EnableOptionIndex8BitPatch { get; set; }
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
