#nullable enable

using System;
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;
using ReForgeFramework.UI.Controls;
using ReForgeFramework.UI.SystemAreas;

public static partial class ReForge
{
	private const string SettingsScreenKey = "ReForge.Settings";
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
		SettingTabPanelHost tabHost = UI.GetSettingTabPanel();
		string tabTitle = UI.T("gameplay_ui", "REFORGE.SETTINGS.TAB", "ReForge");
		tabHost.AddChild(new SettingTab(tabTitle, selected: false, screenKey: SettingsScreenKey).WithMinHeight(72f));

		SettingScreen? screen = tabHost.GetSettingScreen(SettingsScreenKey);
		if (screen == null)
		{
			GD.PrintErr($"[ReForge.Settings] Cannot find setting screen '{SettingsScreenKey}'.");
			return;
		}

		string optionTitle = UI.T("gameplay_ui", "REFORGE.SETTINGS.DEBUG_TITLE", "Enable Debug Console");
		SettingOptionItem debugToggle = SettingOptionItem.Toggle(optionTitle, RuntimeSettings.EnableDebugConsole, OnDebugToggled);
		debugToggle
			.OnHoverEnter(ShowDebugHoverTip)
			.OnHoverExit(RemoveDebugHoverTip);

		screen.Add(debugToggle);
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

	private static void ShowDebugHoverTip(Control owner)
	{
		if (!GodotObject.IsInstanceValid(owner))
		{
			return;
		}

		NHoverTipSet.Remove(owner);
		HoverTip tip = new(
			new LocString("gameplay_ui", "REFORGE.SETTINGS.DEBUG_TIP_TITLE"),
			new LocString("gameplay_ui", "REFORGE.SETTINGS.DEBUG_TIP_DESC")
		);

		NHoverTipSet tipSet = NHoverTipSet.CreateAndShow(owner, tip);
		tipSet.GlobalPosition = owner.GlobalPosition + NSettingsScreen.settingTipsOffset;
	}

	private static void RemoveDebugHoverTip(Control owner)
	{
		if (!GodotObject.IsInstanceValid(owner))
		{
			return;
		}

		NHoverTipSet.Remove(owner);
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
