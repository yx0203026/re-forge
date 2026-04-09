#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace ReForgeFramework.ModLoading;

internal static class ReForgeModSettingsStore
{
	private const string SettingsDir = "user://reforge";
	private const string SettingsPath = "user://reforge/mod_loader_settings.json";

	internal static ReForgeModSettings Load()
	{
		try
		{
			if (!FileAccess.FileExists(SettingsPath))
			{
				return Clone(ReForgeModSettings.Default);
			}

			using FileAccess file = FileAccess.Open(SettingsPath, FileAccess.ModeFlags.Read);
			string json = file.GetAsText();
			if (string.IsNullOrWhiteSpace(json))
			{
				return Clone(ReForgeModSettings.Default);
			}

			PersistedModLoaderSettings? persisted = JsonSerializer.Deserialize<PersistedModLoaderSettings>(json, new JsonSerializerOptions
			{
				PropertyNameCaseInsensitive = true
			});

			if (persisted == null)
			{
				return Clone(ReForgeModSettings.Default);
			}

			return new ReForgeModSettings
			{
				PlayerAgreedToModLoading = persisted.PlayerAgreedToModLoading,
				DisabledModIds = new HashSet<string>(
					(persisted.DisabledModIds ?? Array.Empty<string>())
						.Where(static id => !string.IsNullOrWhiteSpace(id))
						.Select(static id => id.Trim()),
					StringComparer.OrdinalIgnoreCase)
			};
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[ReForge.ModLoader] Failed to load mod loader settings: {ex.Message}");
			return Clone(ReForgeModSettings.Default);
		}
	}

	internal static void Save(ReForgeModSettings settings)
	{
		ArgumentNullException.ThrowIfNull(settings);

		try
		{
			DirAccess.MakeDirRecursiveAbsolute(SettingsDir);
			PersistedModLoaderSettings persisted = new()
			{
				PlayerAgreedToModLoading = settings.PlayerAgreedToModLoading,
				DisabledModIds = settings.DisabledModIds
					.Where(static id => !string.IsNullOrWhiteSpace(id))
					.Select(static id => id.Trim())
					.Distinct(StringComparer.OrdinalIgnoreCase)
					.OrderBy(static id => id, StringComparer.OrdinalIgnoreCase)
					.ToArray()
			};

			string json = JsonSerializer.Serialize(persisted, new JsonSerializerOptions
			{
				WriteIndented = true
			});

			using FileAccess file = FileAccess.Open(SettingsPath, FileAccess.ModeFlags.Write);
			file.StoreString(json);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[ReForge.ModLoader] Failed to save mod loader settings: {ex.Message}");
		}
	}

	internal static ReForgeModSettings Clone(ReForgeModSettings settings)
	{
		ArgumentNullException.ThrowIfNull(settings);

		return new ReForgeModSettings
		{
			PlayerAgreedToModLoading = settings.PlayerAgreedToModLoading,
			DisabledModIds = new HashSet<string>(settings.DisabledModIds, StringComparer.OrdinalIgnoreCase)
		};
	}

	private sealed class PersistedModLoaderSettings
	{
		public bool PlayerAgreedToModLoading { get; set; } = true;

		public string[] DisabledModIds { get; set; } = Array.Empty<string>();
	}
}