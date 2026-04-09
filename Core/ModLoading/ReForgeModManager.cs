#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Saves;
using ReForgeFramework.ModResources;

namespace ReForgeFramework.ModLoading;

public static class ReForgeModManager
{
	private static bool _initialized;
	private static readonly ReForgeModDiagnostics Diagnostics = new();
	private static readonly ReForgeModFileIo FileIo = new();
	private static readonly PckResourceSource PckSource = new();
	private static readonly EmbeddedResourceSource EmbeddedSource = new();
	private static readonly ReForgeModLifecycle Lifecycle = new(FileIo, Diagnostics, PckSource, EmbeddedSource);
	private static readonly List<ReForgeModContext> Contexts = new();
	private static ReForgeModSettings _activeSettings = ReForgeModSettingsStore.Clone(ReForgeModSettings.Default);

	public static event Action<ReForgeModContext>? OnModDetected;

	public static void Initialize(ReForgeModSettings? settings = null)
	{
		if (_initialized)
		{
			return;
		}

		_initialized = true;
		ReForgeModSettings actualSettings = settings ?? ReForgeModSettingsStore.Load();
		actualSettings = MergeWithOfficialModSettings(actualSettings);
		_activeSettings = ReForgeModSettingsStore.Clone(actualSettings);
		ReForgeModSettingsStore.Save(_activeSettings);
		List<ReForgeModContext> discovered = Lifecycle.DiscoverMods();
		EnsureSelfContext(discovered);

		List<ReForgeModContext> ordered = Lifecycle.ValidateAndSort(discovered);
		HashSet<string> loadedIds = new(StringComparer.OrdinalIgnoreCase);
		foreach (ReForgeModContext mod in ordered)
		{
			Lifecycle.TryLoad(mod, actualSettings, loadedIds);
			if (mod.State == ReForgeModLoadState.Loaded)
			{
				loadedIds.Add(mod.ModId);
			}

			Contexts.Add(mod);
			OnModDetected?.Invoke(mod);
		}

		int loaded = Contexts.Count(m => m.State == ReForgeModLoadState.Loaded);
		GD.Print($"[ReForge.ModLoader] Initialization completed. loaded={loaded}, total={Contexts.Count}");
	}

	public static IReadOnlyList<ReForgeModContext> GetLoadedMods()
	{
		return Contexts.Where(m => m.State == ReForgeModLoadState.Loaded).ToList();
	}

	public static IReadOnlyList<ReForgeModContext> GetAllMods()
	{
		return Contexts.ToList();
	}

	public static ReForgeModDiagnosticsSnapshot GetDiagnosticsSnapshot()
	{
		return Diagnostics.BuildSnapshot();
	}

	/// <summary>
	/// 在游戏根目录的 dev 子目录下创建一个新的模组 C# 项目脚手架。
	/// </summary>
	/// <param name="modName">用户输入的模组名称，将作为目录名与项目名。</param>
	/// <param name="createdProjectPath">创建成功时输出项目目录路径。</param>
	/// <param name="errorMessage">创建失败时输出错误信息。</param>
	/// <returns>创建成功返回 true，否则返回 false。</returns>
	public static bool TryCreateDevModProject(string modName, out string createdProjectPath, out string errorMessage)
	{
		createdProjectPath = string.Empty;
		errorMessage = string.Empty;

		if (string.IsNullOrWhiteSpace(modName))
		{
			errorMessage = "Mod name cannot be empty.";
			return false;
		}

		string trimmedName = modName.Trim();
		if (!IsValidProjectDirectoryName(trimmedName))
		{
			errorMessage = "Mod name contains invalid path characters.";
			return false;
		}

		string executablePath = OS.GetExecutablePath();
		string? gameRoot = Path.GetDirectoryName(executablePath);
		if (string.IsNullOrWhiteSpace(gameRoot))
		{
			errorMessage = "Cannot resolve game root directory from executable path.";
			return false;
		}

		string devRoot = Path.Combine(gameRoot, "dev");
		string modRoot = Path.Combine(devRoot, trimmedName);
		if (Directory.Exists(modRoot))
		{
			errorMessage = $"Target directory already exists: {modRoot}";
			return false;
		}

		try
		{
			Directory.CreateDirectory(devRoot);
			Directory.CreateDirectory(modRoot);

			string resourceFolderName = BuildResourceFolderName(trimmedName);
			string resourceFolderPath = Path.Combine(modRoot, resourceFolderName);
			Directory.CreateDirectory(resourceFolderPath);

			string projectFilePath = Path.Combine(modRoot, trimmedName + ".csproj");
			string csproj = BuildProjectFileText(resourceFolderName);
			File.WriteAllText(projectFilePath, csproj, Encoding.UTF8);

			string modMainFilePath = Path.Combine(modRoot, "ModMain.cs");
			string modMainCode = BuildModMainText(trimmedName);
			File.WriteAllText(modMainFilePath, modMainCode, Encoding.UTF8);

			createdProjectPath = modRoot;
			return true;
		}
		catch (Exception ex)
		{
			errorMessage = $"Failed to create dev mod project: {ex.Message}";
			return false;
		}
	}

	/// <summary>
	/// 获取当前启动过程中实际使用的模组加载设置。
	/// </summary>
	/// <returns>运行中设置快照。</returns>
	public static ReForgeModSettings GetActiveSettings()
	{
		return ReForgeModSettingsStore.Clone(_activeSettings);
	}

	/// <summary>
	/// 获取下次启动将使用的持久化模组加载设置。
	/// </summary>
	/// <returns>持久化设置快照。</returns>
	public static ReForgeModSettings GetPersistedSettings()
	{
		return ReForgeModSettingsStore.Load();
	}

	/// <summary>
	/// 设置下次启动是否允许加载模组。
	/// </summary>
	/// <param name="agreed">玩家是否同意加载模组。</param>
	public static void SetPlayerAgreementForNextLaunch(bool agreed)
	{
		ReForgeModSettings current = ReForgeModSettingsStore.Load();
		ReForgeModSettings updated = new()
		{
			PlayerAgreedToModLoading = agreed,
			DisabledModIds = new HashSet<string>(current.DisabledModIds, StringComparer.OrdinalIgnoreCase)
		};

		ReForgeModSettingsStore.Save(updated);
		TrySyncOfficialPlayerAgreement(agreed);
	}

	/// <summary>
	/// 查询指定模组在下次启动时是否启用。
	/// </summary>
	/// <param name="modId">模组 ID。</param>
	/// <returns>若下次启动为启用状态则返回 true。</returns>
	public static bool IsModEnabledForNextLaunch(string modId)
	{
		if (string.IsNullOrWhiteSpace(modId))
		{
			return false;
		}

		ReForgeModSettings settings = ReForgeModSettingsStore.Load();
		return !settings.DisabledModIds.Contains(modId);
	}

	/// <summary>
	/// 设置指定模组在下次启动时的启用状态。
	/// </summary>
	/// <param name="modId">模组 ID。</param>
	/// <param name="enabled">是否启用。</param>
	/// <returns>当输入有效并成功写入设置时返回 true。</returns>
	public static bool SetModEnabledForNextLaunch(string modId, bool enabled)
	{
		if (string.IsNullOrWhiteSpace(modId))
		{
			return false;
		}

		if (modId.Equals("reforge", StringComparison.OrdinalIgnoreCase) && !enabled)
		{
			GD.Print("[ReForge.ModLoader] Ignored disabling core framework mod 'reforge'.");
			return false;
		}

		ReForgeModSettings settings = ReForgeModSettingsStore.Load();
		HashSet<string> disabled = new(settings.DisabledModIds, StringComparer.OrdinalIgnoreCase);
		if (enabled)
		{
			disabled.Remove(modId);
		}
		else
		{
			disabled.Add(modId);
		}

		ReForgeModSettings updated = new()
		{
			PlayerAgreedToModLoading = settings.PlayerAgreedToModLoading,
			DisabledModIds = disabled
		};

		ReForgeModSettingsStore.Save(updated);
		TrySyncOfficialSingleModEnabled(modId, enabled);
		return true;
	}

	/// <summary>
	/// 判断当前运行状态与下次启动设置之间是否存在待应用改动。
	/// </summary>
	/// <returns>存在需要重启才能生效的改动时返回 true。</returns>
	public static bool HasPendingRestartChanges()
	{
		ReForgeModSettings persisted = ReForgeModSettingsStore.Load();
		if (_activeSettings.PlayerAgreedToModLoading != persisted.PlayerAgreedToModLoading)
		{
			return true;
		}

		foreach (ReForgeModContext mod in Contexts)
		{
			bool enabledByPersisted = persisted.PlayerAgreedToModLoading && !persisted.DisabledModIds.Contains(mod.ModId);
			if ((mod.State == ReForgeModLoadState.Loaded && !enabledByPersisted)
				|| (mod.State == ReForgeModLoadState.Disabled && enabledByPersisted))
			{
				return true;
			}
		}

		return false;
	}

	public static bool TryReadResourceText(string resourcePath, out string text, ReForgeModContext? preferredMod = null)
	{
		text = string.Empty;
		if (!TryReadResourceBytes(resourcePath, out byte[] bytes, preferredMod))
		{
			return false;
		}

		text = Encoding.UTF8.GetString(bytes);
		return true;
	}

	public static bool TryReadResourceBytes(string resourcePath, out byte[] bytes, ReForgeModContext? preferredMod = null)
	{
		bytes = Array.Empty<byte>();
		if (!_initialized)
		{
			Initialize();
		}

		string normalized = ResourcePathResolver.Normalize(resourcePath);
		if (preferredMod != null && TryReadFromMod(preferredMod, normalized, out bytes))
		{
			return true;
		}

		string? ownerModId = ResourcePathResolver.ResolveOwnerModId(normalized);
		if (!string.IsNullOrWhiteSpace(ownerModId))
		{
			ReForgeModContext? owner = Contexts.FirstOrDefault(m => m.ModId.Equals(ownerModId, StringComparison.OrdinalIgnoreCase));
			if (owner != null && TryReadFromMod(owner, normalized, out bytes))
			{
				return true;
			}
		}

		foreach (ReForgeModContext mod in Contexts.Where(m => m.State == ReForgeModLoadState.Loaded))
		{
			if (TryReadFromMod(mod, normalized, out bytes))
			{
				return true;
			}
		}

		return false;
	}

	/// <summary>
	/// 尝试从已加载的模组中加载纹理资源。路径解析遵循与文本资源相同的规则。
	/// </summary>
	/// <param name="resourcePath">资源路径，支持模组前缀和所有权标识。</param>
	/// <param name="texture">输出的纹理对象，如果加载失败则为 null。</param>
	public static bool TryLoadTexture(string resourcePath, out Texture2D texture)
	{
		texture = null!;
		if (!TryReadResourceBytes(resourcePath, out byte[] bytes))
		{
			return false;
		}

		Godot.Image image = new();
		if (image.LoadPngFromBuffer(bytes) != Error.Ok)
		{
			return false;
		}

		texture = ImageTexture.CreateFromImage(image);
		return true;
	}

	private static bool TryReadFromMod(ReForgeModContext mod, string normalizedPath, out byte[] bytes)
	{
		bytes = Array.Empty<byte>();
		if (mod.State != ReForgeModLoadState.Loaded)
		{
			return false;
		}

		byte[]? result = mod.SourceKind switch
		{
			ReForgeModSourceKind.Pck => PckSource.ReadAllBytes(mod, normalizedPath),
			ReForgeModSourceKind.Embedded => EmbeddedSource.ReadAllBytes(mod, normalizedPath),
			_ => null
		};

		if (result == null || result.Length == 0)
		{
			Diagnostics.TrackResourceResolve(mod.ModId, normalizedPath, mod.SourceKind.ToString(), success: false, "Resource not found.");
			return false;
		}

		bytes = result;
		Diagnostics.TrackResourceResolve(mod.ModId, normalizedPath, mod.SourceKind.ToString(), success: true, "Resource loaded.");
		return true;
	}

	private static bool IsValidProjectDirectoryName(string modName)
	{
		if (modName.Equals(".", StringComparison.Ordinal) || modName.Equals("..", StringComparison.Ordinal))
		{
			return false;
		}

		char[] invalidChars = Path.GetInvalidFileNameChars();
		for (int i = 0; i < modName.Length; i++)
		{
			char c = modName[i];
			if (Array.IndexOf(invalidChars, c) >= 0)
			{
				return false;
			}

			if (c == '/' || c == '\\')
			{
				return false;
			}
		}

		return true;
	}

	private static string BuildResourceFolderName(string modName)
	{
		StringBuilder builder = new();
		bool previousWasSeparator = false;
		for (int i = 0; i < modName.Length; i++)
		{
			char current = modName[i];
			if (char.IsLetterOrDigit(current))
			{
				builder.Append(char.ToLowerInvariant(current));
				previousWasSeparator = false;
			}
			else if (!previousWasSeparator)
			{
				builder.Append('_');
				previousWasSeparator = true;
			}
		}

		string normalized = builder.ToString().Trim('_');
		return string.IsNullOrWhiteSpace(normalized) ? "new_mod" : normalized;
	}

	private static string BuildProjectFileText(string resourceFolderName)
	{
		StringBuilder builder = new();
		builder.AppendLine("<Project Sdk=\"Godot.NET.Sdk/4.5.1\">");
		builder.AppendLine();
		builder.AppendLine("  <PropertyGroup>");
		builder.AppendLine("    <TargetFramework>net9.0</TargetFramework>");
		builder.AppendLine("    <TargetFramework Condition=\" '$(GodotTargetPlatform)' == 'android' \">net9.0</TargetFramework>");
		builder.AppendLine("    <EnableDynamicLoading>true</EnableDynamicLoading>");
		builder.AppendLine("    <OutputPath>build\\</OutputPath>");
		builder.AppendLine("  </PropertyGroup>");
		builder.AppendLine();
		builder.AppendLine("  <ItemGroup>");
		builder.AppendLine("    <Reference Include=\"ReForge.dll\" />");
		builder.AppendLine("    <Reference Include=\"data_sts2_windows_x86_64\\0Harmony.dll\" />");
		builder.AppendLine("    <Reference Include=\"data_sts2_windows_x86_64\\sts2.dll\" />");
		builder.AppendLine("  </ItemGroup>");
		builder.AppendLine();
		builder.AppendLine("  <ItemGroup>");
		builder.AppendLine($"    <EmbeddedResource Include=\"{resourceFolderName}\\**\\*\" />");
		builder.AppendLine("  </ItemGroup>");
		builder.AppendLine();
		builder.AppendLine("</Project>");
		return builder.ToString();
	}

	private static string BuildModMainText(string modName)
	{
		string modNamespace = BuildCSharpIdentifier(modName) + ".Generated";
		string harmonyId = BuildResourceFolderName(modName) + ".mod";

		StringBuilder builder = new();
		builder.AppendLine("using Godot;");
		builder.AppendLine("using HarmonyLib;");
		builder.AppendLine("using MegaCrit.Sts2.Core.Modding;");
		builder.AppendLine();
		builder.Append("namespace ").Append(modNamespace).AppendLine(";");
		builder.AppendLine();
		builder.AppendLine("[ModInitializer(nameof(Initialize))]");
		builder.AppendLine("public static class ModMain");
		builder.AppendLine("{");
		builder.AppendLine("\tprivate static bool _initialized;");
		builder.AppendLine("\tprivate static Harmony? _harmony;");
		builder.AppendLine();
		builder.AppendLine("\tprivate static void Initialize()");
		builder.AppendLine("\t{");
		builder.AppendLine("\t\tif (_initialized)");
		builder.AppendLine("\t\t{");
		builder.AppendLine("\t\t\treturn;");
		builder.AppendLine("\t\t}");
		builder.AppendLine();
		builder.AppendLine("\t\t_initialized = true;");
		builder.Append("\t\t_harmony = new Harmony(\"").Append(harmonyId).AppendLine("\");");
		builder.AppendLine("\t\t_harmony.PatchAll();");
		builder.Append("\t\tGD.Print(\"[").Append(harmonyId).AppendLine("] initialized.\");");
		builder.AppendLine("\t}");
		builder.AppendLine("}");

		return builder.ToString();
	}

	private static string BuildCSharpIdentifier(string modName)
	{
		StringBuilder builder = new();
		bool capitalizeNext = true;
		for (int i = 0; i < modName.Length; i++)
		{
			char current = modName[i];
			if (!char.IsLetterOrDigit(current))
			{
				capitalizeNext = true;
				continue;
			}

			if (builder.Length == 0 && char.IsDigit(current))
			{
				builder.Append('_');
			}

			builder.Append(capitalizeNext ? char.ToUpperInvariant(current) : current);
			capitalizeNext = false;
		}

		if (builder.Length == 0)
		{
			return "NewMod";
		}

		return builder.ToString();
	}

	private static void EnsureSelfContext(List<ReForgeModContext> discovered)
	{
		if (discovered.Any(m => m.ModId.Equals("reforge", StringComparison.OrdinalIgnoreCase)))
		{
			return;
		}

		ReForgeModManifest manifest = TryReadSelfManifest() ?? new ReForgeModManifest
		{
			Id = "reforge",
			Name = "re-forge",
			Author = "ReForgeTeam",
			Version = "dev",
			HasDll = true,
			HasPck = false,
			HasEmbeddedResources = true,
			AffectsGameplay = false
		};

		if (string.IsNullOrWhiteSpace(manifest.Id))
		{
			manifest = new ReForgeModManifest
			{
				Id = "reforge",
				Name = manifest.Name,
				Author = manifest.Author,
				Description = manifest.Description,
				Version = manifest.Version,
				HasPck = manifest.HasPck,
				HasDll = manifest.HasDll,
				HasEmbeddedResources = manifest.HasEmbeddedResources,
				Dependencies = manifest.Dependencies,
				AffectsGameplay = manifest.AffectsGameplay
			};
		}

		string assemblyPath = Assembly.GetExecutingAssembly().Location;
		discovered.Add(new ReForgeModContext
		{
			ModId = manifest.Id ?? "reforge",
			ManifestPath = "[self]",
			ModPath = Path.GetDirectoryName(assemblyPath) ?? string.Empty,
			Manifest = manifest,
			State = ReForgeModLoadState.None,
			SourceKind = ReForgeModSourceKind.Unknown,
			Assembly = Assembly.GetExecutingAssembly()
		});
	}

	private static ReForgeModManifest? TryReadSelfManifest()
	{
		try
		{
			string projectBuildManifest = Path.Combine(AppContext.BaseDirectory, "build", "reforge.json");
			if (File.Exists(projectBuildManifest))
			{
				string json = File.ReadAllText(projectBuildManifest);
				return JsonSerializer.Deserialize<ReForgeModManifest>(json, new JsonSerializerOptions
				{
					PropertyNameCaseInsensitive = true
				});
			}
		}
		catch
		{
			// 忽略读取失败，回退到默认自描述清单。
		}

		return null;
	}

	private static ReForgeModSettings MergeWithOfficialModSettings(ReForgeModSettings settings)
	{
		ArgumentNullException.ThrowIfNull(settings);

		bool playerAgreedToModLoading = settings.PlayerAgreedToModLoading;
		HashSet<string> disabledModIds = new(settings.DisabledModIds, StringComparer.OrdinalIgnoreCase);
		if (!TryGetOfficialModSettings(out ModSettings? officialSettings) || officialSettings == null)
		{
			return new ReForgeModSettings
			{
				PlayerAgreedToModLoading = playerAgreedToModLoading,
				DisabledModIds = disabledModIds
			};
		}

		if (!officialSettings.PlayerAgreedToModLoading)
		{
			playerAgreedToModLoading = false;
		}

		for (int i = 0; i < officialSettings.ModList.Count; i++)
		{
			SettingsSaveMod entry = officialSettings.ModList[i];
			if (entry.IsEnabled || string.IsNullOrWhiteSpace(entry.Id))
			{
				continue;
			}

			disabledModIds.Add(entry.Id);
		}

		return new ReForgeModSettings
		{
			PlayerAgreedToModLoading = playerAgreedToModLoading,
			DisabledModIds = disabledModIds
		};
	}

	private static bool TryGetOfficialModSettings(out ModSettings? modSettings)
	{
		modSettings = null;

		try
		{
			SettingsSave settingsSave = SaveManager.Instance.SettingsSave;
			settingsSave.ModSettings ??= new ModSettings();
			modSettings = settingsSave.ModSettings;
			return true;
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[ReForge.ModLoader] Failed to access official mod settings: {ex.Message}");
			return false;
		}
	}

	private static void TrySyncOfficialPlayerAgreement(bool agreed)
	{
		if (!TryGetOfficialModSettings(out ModSettings? modSettings) || modSettings == null)
		{
			return;
		}

		try
		{
			modSettings.PlayerAgreedToModLoading = agreed;
			SaveManager.Instance.SaveSettings();
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[ReForge.ModLoader] Failed to sync player agreement to official settings: {ex.Message}");
		}
	}

	private static void TrySyncOfficialSingleModEnabled(string modId, bool enabled)
	{
		if (string.IsNullOrWhiteSpace(modId))
		{
			return;
		}

		if (!TryGetOfficialModSettings(out ModSettings? modSettings) || modSettings == null)
		{
			return;
		}

		try
		{
			bool updatedAny = false;
			for (int i = 0; i < modSettings.ModList.Count; i++)
			{
				SettingsSaveMod entry = modSettings.ModList[i];
				if (!entry.Id.Equals(modId, StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}

				entry.IsEnabled = enabled;
				updatedAny = true;
			}

			if (!updatedAny)
			{
				ModSource inferredSource = InferOfficialModSource(modId);
				modSettings.ModList.Add(new SettingsSaveMod
				{
					Id = modId,
					Source = inferredSource,
					IsEnabled = enabled
				});
			}

			SaveManager.Instance.SaveSettings();
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[ReForge.ModLoader] Failed to sync mod '{modId}' enabled={enabled} to official settings: {ex.Message}");
		}
	}

	private static ModSource InferOfficialModSource(string modId)
	{
		ReForgeModContext? context = Contexts.FirstOrDefault(m => m.ModId.Equals(modId, StringComparison.OrdinalIgnoreCase));
		if (context == null)
		{
			return ModSource.ModsDirectory;
		}

		string normalizedPath = context.ModPath.Replace('\\', '/');
		if (normalizedPath.Contains("/workshop/", StringComparison.OrdinalIgnoreCase)
			|| (normalizedPath.Contains("/steamapps/", StringComparison.OrdinalIgnoreCase)
				&& normalizedPath.Contains("/content/", StringComparison.OrdinalIgnoreCase)))
		{
			return ModSource.SteamWorkshop;
		}

		return ModSource.ModsDirectory;
	}
}
