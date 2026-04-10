#nullable enable

using System;
using System.Collections.Generic;
using System.Text.Json;
using ReForgeFramework.ModLoading;

namespace ReForgeFramework.ModResources;

public static class LocalizationResourceBridge
{
	private static readonly object Sync = new();
	private static readonly Dictionary<string, Dictionary<string, Dictionary<string, string>>> Cache = new(StringComparer.OrdinalIgnoreCase);

	public static void RefreshCurrentLanguage()
	{
		lock (Sync)
		{
			Cache.Clear();
		}
	}

	public static bool TryGetText(string table, string key, string language, out string value)
	{
		value = string.Empty;
		if (string.IsNullOrWhiteSpace(table) || string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(language))
		{
			return false;
		}

		Dictionary<string, string> tableEntries = GetOrBuildTable(table, language);
		if (!tableEntries.TryGetValue(key, out string? text) || string.IsNullOrWhiteSpace(text))
		{
			return false;
		}

		value = text;
		return true;
	}

	private static Dictionary<string, string> GetOrBuildTable(string table, string language)
	{
		lock (Sync)
		{
			if (Cache.TryGetValue(language, out Dictionary<string, Dictionary<string, string>>? languageBucket)
				&& languageBucket.TryGetValue(table, out Dictionary<string, string>? tableBucket))
			{
				return tableBucket;
			}

			Dictionary<string, string> merged = new(StringComparer.OrdinalIgnoreCase);
			foreach (ReForgeModContext mod in ReForgeModManager.GetAllMods())
			{
				if (mod.SourceKind != ReForgeModSourceKind.Embedded)
				{
					continue;
				}

				if (mod.State != ReForgeModLoadState.Loaded
					&& mod.State != ReForgeModLoadState.AddedAtRuntime
					&& mod.State != ReForgeModLoadState.Failed)
				{
					continue;
				}

				string path = ResourcePathResolver.BuildLocalizationFilePath(mod.ModId, language, table);
				if (!ReForgeModManager.TryReadResourceText(path, out string json, mod))
				{
					continue;
				}

				try
				{
					Dictionary<string, string>? entries = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
					if (entries == null)
					{
						continue;
					}

					foreach ((string entryKey, string entryValue) in entries)
					{
						if (string.IsNullOrWhiteSpace(entryKey) || string.IsNullOrWhiteSpace(entryValue))
						{
							continue;
						}

						merged[entryKey] = entryValue;
					}
				}
				catch
				{
					// 避免单个模组本地化 JSON 损坏影响整体查询。
				}
			}

			if (!Cache.TryGetValue(language, out languageBucket))
			{
				languageBucket = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
				Cache[language] = languageBucket;
			}

			languageBucket[table] = merged;
			return merged;
		}
	}
}
