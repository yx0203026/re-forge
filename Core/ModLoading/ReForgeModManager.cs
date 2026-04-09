#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Nodes;
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
	/// <param name="author">用户输入的模组作者。</param>
	/// <param name="version">用户输入的模组版本号。</param>
	/// <param name="createdProjectPath">创建成功时输出项目目录路径。</param>
	/// <param name="errorMessage">创建失败时输出错误信息。</param>
	/// <returns>创建成功返回 true，否则返回 false。</returns>
	public static bool TryCreateDevModProject(
		string modName,
		string author,
		string version,
		out string createdProjectPath,
		out string errorMessage)
	{
		createdProjectPath = string.Empty;
		errorMessage = string.Empty;

		if (string.IsNullOrWhiteSpace(modName))
		{
			errorMessage = "Mod name cannot be empty.";
			return false;
		}

		string trimmedName = modName.Trim();
		string trimmedAuthor = author?.Trim() ?? string.Empty;
		string trimmedVersion = version?.Trim() ?? string.Empty;

		if (!IsValidProjectDirectoryName(trimmedName))
		{
			errorMessage = "Mod name contains invalid path characters.";
			return false;
		}

		if (string.IsNullOrWhiteSpace(trimmedAuthor))
		{
			errorMessage = "Mod author cannot be empty.";
			return false;
		}

		if (string.IsNullOrWhiteSpace(trimmedVersion))
		{
			errorMessage = "Mod version cannot be empty.";
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
		ResolveReferencePaths(
			gameRoot,
			out string reforgeDllPath,
			out string harmonyDllPath,
			out string sts2DllPath);

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

			string manifestFilePath = Path.Combine(modRoot, resourceFolderName + ".json");
			string manifestJson = BuildManifestText(trimmedName, trimmedAuthor, trimmedVersion, resourceFolderName);
			File.WriteAllText(manifestFilePath, manifestJson, Encoding.UTF8);

			string projectFilePath = Path.Combine(modRoot, trimmedName + ".csproj");
			string csproj = BuildProjectFileText(
				resourceFolderName,
				reforgeDllPath,
				harmonyDllPath,
				sts2DllPath);
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
	/// 获取开发中模组目录（dev）的绝对路径。
	/// </summary>
	/// <returns>若能解析游戏根目录则返回 dev 目录路径，否则返回空字符串。</returns>
	public static string GetDevModsRootPath()
	{
		string? gameRoot = ResolveGameRootDirectory();
		if (string.IsNullOrWhiteSpace(gameRoot))
		{
			return string.Empty;
		}

		return Path.Combine(gameRoot, "dev");
	}

	/// <summary>
	/// 获取开发中模组（dev 目录）下可识别的项目列表。
	/// </summary>
	/// <returns>开发模组项目快照列表。</returns>
	public static IReadOnlyList<ReForgeDevModProject> GetDevModProjects()
	{
		string devRoot = GetDevModsRootPath();
		if (string.IsNullOrWhiteSpace(devRoot) || !Directory.Exists(devRoot))
		{
			return Array.Empty<ReForgeDevModProject>();
		}

		List<ReForgeDevModProject> results = new();
		foreach (string modDirectory in Directory.GetDirectories(devRoot))
		{
			string modName = Path.GetFileName(modDirectory) ?? "unknown";
			string? projectFile = ResolveProjectFile(modDirectory, modName);
			string? manifestFile = ResolveManifestFile(modDirectory);
			string expectedResourceDirectory = BuildResourceFolderName(modName);

			DateTimeOffset lastWriteTimeUtc;
			if (!string.IsNullOrWhiteSpace(projectFile) && File.Exists(projectFile))
			{
				lastWriteTimeUtc = File.GetLastWriteTimeUtc(projectFile);
			}
			else
			{
				lastWriteTimeUtc = Directory.GetLastWriteTimeUtc(modDirectory);
			}

			results.Add(new ReForgeDevModProject
			{
				ModName = modName,
				ModDirectory = modDirectory,
				ProjectFilePath = projectFile,
				ManifestFilePath = manifestFile,
				HasModMainFile = File.Exists(Path.Combine(modDirectory, "ModMain.cs")),
				HasResourceDirectory = Directory.Exists(Path.Combine(modDirectory, expectedResourceDirectory)),
				LastModifiedAtUtc = lastWriteTimeUtc
			});
		}

		return results
			.OrderBy(static project => project.ModName, StringComparer.OrdinalIgnoreCase)
			.ThenBy(static project => project.ModDirectory, StringComparer.OrdinalIgnoreCase)
			.ToList();
	}

	/// <summary>
	/// 构建指定的开发模组项目（.csproj）。
	/// </summary>
	/// <param name="projectFilePath">待构建的项目文件绝对路径。</param>
	/// <returns>构建结果摘要与输出日志。</returns>
	public static ReForgeDevBuildResult BuildDevModProject(string projectFilePath)
	{
		if (string.IsNullOrWhiteSpace(projectFilePath))
		{
			return new ReForgeDevBuildResult
			{
				Succeeded = false,
				Summary = "Project path is empty.",
				Output = ""
			};
		}

		string fullProjectPath;
		try
		{
			fullProjectPath = Path.GetFullPath(projectFilePath);
		}
		catch (Exception ex)
		{
			return new ReForgeDevBuildResult
			{
				Succeeded = false,
				Summary = $"Invalid project path: {ex.Message}",
				Output = ""
			};
		}

		if (!File.Exists(fullProjectPath))
		{
			return new ReForgeDevBuildResult
			{
				Succeeded = false,
				Summary = $"Project file not found: {fullProjectPath}",
				Output = ""
			};
		}

		string projectDirectory = Path.GetDirectoryName(fullProjectPath) ?? string.Empty;
		if (string.IsNullOrWhiteSpace(projectDirectory) || !Directory.Exists(projectDirectory))
		{
			return new ReForgeDevBuildResult
			{
				Succeeded = false,
				Summary = $"Project directory not found: {projectDirectory}",
				Output = ""
			};
		}

		try
		{
			ProcessStartInfo startInfo = new()
			{
				FileName = "dotnet",
				WorkingDirectory = projectDirectory,
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true
			};
			startInfo.ArgumentList.Add("build");
			startInfo.ArgumentList.Add(fullProjectPath);
			startInfo.ArgumentList.Add("-c");
			startInfo.ArgumentList.Add("Release");

			using Process process = new()
			{
				StartInfo = startInfo
			};

			if (!process.Start())
			{
				return new ReForgeDevBuildResult
				{
					Succeeded = false,
					Summary = $"Failed to start dotnet build process for '{fullProjectPath}'.",
					Output = ""
				};
			}

			var stdoutTask = process.StandardOutput.ReadToEndAsync();
			var stderrTask = process.StandardError.ReadToEndAsync();
			process.WaitForExit();

			string stdout = stdoutTask.GetAwaiter().GetResult();
			string stderr = stderrTask.GetAwaiter().GetResult();
			StringBuilder outputBuilder = new();
			outputBuilder.Append(MergeBuildOutput(stdout, stderr));

			bool success = process.ExitCode == 0;
			string summary = success
				? $"Build succeeded. project='{fullProjectPath}', exitCode={process.ExitCode}."
				: $"Build failed. project='{fullProjectPath}', exitCode={process.ExitCode}.";

			if (success)
			{
				if (TryDeployDevBuildArtifacts(projectDirectory, fullProjectPath, out string deployedDirectory, out string deployMessage))
				{
					summary = $"Build and deploy succeeded. project='{fullProjectPath}', deployed='{deployedDirectory}'.";
					outputBuilder.Append(System.Environment.NewLine)
						.Append(System.Environment.NewLine)
						.Append("----- DEPLOY -----")
						.Append(System.Environment.NewLine)
						.Append(deployMessage);
				}
				else
				{
					success = false;
					summary = $"Build succeeded but deploy failed. project='{fullProjectPath}'.";
					outputBuilder.Append(System.Environment.NewLine)
						.Append(System.Environment.NewLine)
						.Append("----- DEPLOY ERROR -----")
						.Append(System.Environment.NewLine)
						.Append(deployMessage);
				}
			}

			return new ReForgeDevBuildResult
			{
				Succeeded = success,
				Summary = summary,
				Output = TrimBuildOutput(outputBuilder.ToString(), maxLines: 180, maxChars: 20000)
			};
		}
		catch (Exception ex)
		{
			return new ReForgeDevBuildResult
			{
				Succeeded = false,
				Summary = $"Build exception: {ex.Message}",
				Output = ex.ToString()
			};
		}
	}

	/// <summary>
	/// Uninstalls a deployed development mod from the runtime mods directory.
	/// </summary>
	/// <param name="projectDirectoryPath">The absolute project directory path under the dev root.</param>
	/// <returns>A runtime action result that describes success state, restart requirement, and operation details.</returns>
	public static ReForgeModRuntimeActionResult UninstallDevModProject(string projectDirectoryPath)
	{
		if (string.IsNullOrWhiteSpace(projectDirectoryPath))
		{
			return new ReForgeModRuntimeActionResult
			{
				Succeeded = false,
				RequiresRestart = false,
				Summary = "Project directory path is empty.",
				Details = string.Empty
			};
		}

		string fullProjectDirectory;
		try
		{
			fullProjectDirectory = Path.GetFullPath(projectDirectoryPath);
		}
		catch (Exception ex)
		{
			return new ReForgeModRuntimeActionResult
			{
				Succeeded = false,
				RequiresRestart = false,
				Summary = $"Invalid project directory path: {ex.Message}",
				Details = ex.ToString()
			};
		}

		string? gameRoot = ResolveGameRootDirectory();
		if (string.IsNullOrWhiteSpace(gameRoot))
		{
			return new ReForgeModRuntimeActionResult
			{
				Succeeded = false,
				RequiresRestart = false,
				Summary = "Cannot resolve game root directory.",
				Details = fullProjectDirectory
			};
		}

		string projectFolderName = Path.GetFileName(fullProjectDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
		if (string.IsNullOrWhiteSpace(projectFolderName))
		{
			return new ReForgeModRuntimeActionResult
			{
				Succeeded = false,
				RequiresRestart = false,
				Summary = "Cannot infer project folder name.",
				Details = fullProjectDirectory
			};
		}

		string deployedDirectory = Path.Combine(gameRoot, "mods", projectFolderName);
		bool deployedExists = Directory.Exists(deployedDirectory);

		string? modId = null;
		bool loadedAtRuntime = false;
		if (TryResolveDevManifest(fullProjectDirectory, out _, out ReForgeModManifest manifest)
			&& !string.IsNullOrWhiteSpace(manifest.Id))
		{
			modId = manifest.Id;
			loadedAtRuntime = Contexts.Any(mod => mod.ModId.Equals(modId, StringComparison.OrdinalIgnoreCase)
				&& mod.State == ReForgeModLoadState.Loaded);

			SetModEnabledForNextLaunch(modId, enabled: false);
		}

		StringBuilder detailsBuilder = new();
		detailsBuilder.AppendLine($"Project directory: {fullProjectDirectory}");
		detailsBuilder.AppendLine($"Deployed directory: {deployedDirectory}");

		if (loadedAtRuntime)
		{
			detailsBuilder.AppendLine("The target mod is currently loaded. Runtime unload is not supported for already loaded DLL/PCK assets.");
			detailsBuilder.AppendLine("The mod has been set to disabled for next launch. Restart the game to complete unload.");
			return new ReForgeModRuntimeActionResult
			{
				Succeeded = true,
				RequiresRestart = true,
				Summary = string.IsNullOrWhiteSpace(modId)
					? "Mod unload was scheduled for next launch."
					: $"Mod '{modId}' unload was scheduled for next launch.",
				Details = detailsBuilder.ToString()
			};
		}

		if (!deployedExists)
		{
			detailsBuilder.AppendLine("No deployed directory was found under mods. Nothing to delete.");
			return new ReForgeModRuntimeActionResult
			{
				Succeeded = true,
				RequiresRestart = false,
				Summary = string.IsNullOrWhiteSpace(modId)
					? "Mod was already uninstalled from mods directory."
					: $"Mod '{modId}' was already uninstalled from mods directory.",
				Details = detailsBuilder.ToString()
			};
		}

		try
		{
			Directory.Delete(deployedDirectory, recursive: true);
			detailsBuilder.AppendLine("Deleted deployed mod directory successfully.");

			if (!string.IsNullOrWhiteSpace(modId))
			{
				for (int i = 0; i < Contexts.Count; i++)
				{
					ReForgeModContext context = Contexts[i];
					if (!context.ModId.Equals(modId, StringComparison.OrdinalIgnoreCase))
					{
						continue;
					}

					if (context.State != ReForgeModLoadState.Loaded)
					{
						context.State = ReForgeModLoadState.Disabled;
					}
				}
			}

			return new ReForgeModRuntimeActionResult
			{
				Succeeded = true,
				RequiresRestart = false,
				Summary = string.IsNullOrWhiteSpace(modId)
					? "Mod files were uninstalled from mods directory."
					: $"Mod '{modId}' files were uninstalled from mods directory.",
				Details = detailsBuilder.ToString()
			};
		}
		catch (Exception ex)
		{
			detailsBuilder.AppendLine("Failed to delete deployed directory.");
			detailsBuilder.AppendLine(ex.ToString());
			return new ReForgeModRuntimeActionResult
			{
				Succeeded = false,
				RequiresRestart = false,
				Summary = "Uninstall failed while deleting deployed files.",
				Details = detailsBuilder.ToString()
			};
		}
	}

	/// <summary>
	/// Reloads a development mod by re-discovering its manifest and attempting runtime load when possible.
	/// </summary>
	/// <param name="projectDirectoryPath">The absolute project directory path under the dev root.</param>
	/// <returns>A runtime action result that describes success state, restart requirement, and operation details.</returns>
	public static ReForgeModRuntimeActionResult ReloadDevModProject(string projectDirectoryPath)
	{
		if (string.IsNullOrWhiteSpace(projectDirectoryPath))
		{
			return new ReForgeModRuntimeActionResult
			{
				Succeeded = false,
				RequiresRestart = false,
				Summary = "Project directory path is empty.",
				Details = string.Empty
			};
		}

		string fullProjectDirectory;
		try
		{
			fullProjectDirectory = Path.GetFullPath(projectDirectoryPath);
		}
		catch (Exception ex)
		{
			return new ReForgeModRuntimeActionResult
			{
				Succeeded = false,
				RequiresRestart = false,
				Summary = $"Invalid project directory path: {ex.Message}",
				Details = ex.ToString()
			};
		}

		if (!TryResolveDevManifest(fullProjectDirectory, out _, out ReForgeModManifest manifest)
			|| string.IsNullOrWhiteSpace(manifest.Id))
		{
			return new ReForgeModRuntimeActionResult
			{
				Succeeded = false,
				RequiresRestart = false,
				Summary = "Cannot reload because no valid manifest was found in project directory.",
				Details = fullProjectDirectory
			};
		}

		string modId = manifest.Id;
		SetModEnabledForNextLaunch(modId, enabled: true);

		ReForgeModContext? loadedContext = Contexts.FirstOrDefault(mod => mod.ModId.Equals(modId, StringComparison.OrdinalIgnoreCase)
			&& mod.State == ReForgeModLoadState.Loaded);
		if (loadedContext != null)
		{
			return new ReForgeModRuntimeActionResult
			{
				Succeeded = true,
				RequiresRestart = true,
				Summary = $"Mod '{modId}' is already loaded. Full reload requires restart.",
				Details =
					"Official STS2 lifecycle does not support unloading already loaded DLL/PCK content at runtime.\n" +
					"The mod remains enabled for next launch, and restart is required to apply new binaries."
			};
		}

		List<ReForgeModContext> discovered = Lifecycle.DiscoverMods();
		EnsureSelfContext(discovered);
		ReForgeModContext? candidate = discovered.FirstOrDefault(mod => mod.ModId.Equals(modId, StringComparison.OrdinalIgnoreCase));
		if (candidate == null)
		{
			return new ReForgeModRuntimeActionResult
			{
				Succeeded = false,
				RequiresRestart = false,
				Summary = $"No deployed mod with id '{modId}' was found under the mods directory.",
				Details = "Build and deploy the project first, then click Reload."
			};
		}

		for (int i = Contexts.Count - 1; i >= 0; i--)
		{
			if (!Contexts[i].ModId.Equals(modId, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			if (Contexts[i].State == ReForgeModLoadState.Loaded)
			{
				continue;
			}

			Contexts.RemoveAt(i);
		}

		ReForgeModSettings runtimeSettings = ReForgeModSettingsStore.Load();
		HashSet<string> loadedIds = Contexts
			.Where(mod => mod.State == ReForgeModLoadState.Loaded)
			.Select(mod => mod.ModId)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);

		Lifecycle.TryLoad(candidate, runtimeSettings, loadedIds);
		Contexts.Add(candidate);
		OnModDetected?.Invoke(candidate);

		if (candidate.State == ReForgeModLoadState.Loaded)
		{
			TryRunRuntimeReloadPostHooks(modId);
			return new ReForgeModRuntimeActionResult
			{
				Succeeded = true,
				RequiresRestart = false,
				Summary = $"Mod '{modId}' reloaded at runtime.",
				Details =
					$"Manifest path: {candidate.ManifestPath}{System.Environment.NewLine}" +
					$"Mod path: {candidate.ModPath}{System.Environment.NewLine}" +
					"Initializer and runtime localization refresh hooks have been executed."
			};
		}

		bool requiresRestart = candidate.State == ReForgeModLoadState.Disabled;
		return new ReForgeModRuntimeActionResult
		{
			Succeeded = false,
			RequiresRestart = requiresRestart,
			Summary = $"Runtime reload failed for '{modId}'. State: {candidate.State}.",
			Details = candidate.Errors.Count > 0
				? string.Join(System.Environment.NewLine, candidate.Errors)
				: "See diagnostics panel for phase details."
		};
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

	private static void TryRunRuntimeReloadPostHooks(string modId)
	{
		try
		{
			LocalizationResourceBridge.RefreshCurrentLanguage();

			if (LocManager.Instance != null)
			{
				string currentLanguage = LocManager.Instance.Language;
				LocManager.Instance.SetLanguage(currentLanguage);
			}

			NGame.Instance?.Relocalize();
			GD.Print($"[ReForge.ModLoader] Runtime reload hooks executed for '{modId}'.");
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[ReForge.ModLoader] Runtime reload post hooks failed for '{modId}': {ex}");
		}
	}

	private static bool TryDeployDevBuildArtifacts(
		string projectDirectory,
		string projectFilePath,
		out string deployedDirectory,
		out string deployMessage)
	{
		deployedDirectory = string.Empty;
		deployMessage = string.Empty;

		string? gameRoot = ResolveGameRootDirectory();
		if (string.IsNullOrWhiteSpace(gameRoot))
		{
			deployMessage = "Cannot resolve game root directory from executable path.";
			return false;
		}

		string? builtDllPath = ResolveBuildAssemblyPath(projectDirectory, projectFilePath);
		if (string.IsNullOrWhiteSpace(builtDllPath) || !File.Exists(builtDllPath))
		{
			deployMessage = "Cannot locate built dll under the project build directory.";
			return false;
		}

		if (!TryResolveDevManifest(projectDirectory, out string manifestPath, out ReForgeModManifest manifest))
		{
			deployMessage = "Cannot find a valid mod manifest json (with non-empty id) in the project root.";
			return false;
		}

		if (string.IsNullOrWhiteSpace(manifest.Id))
		{
			deployMessage = "Manifest id is empty.";
			return false;
		}

		string modDirectoryName = Path.GetFileName(projectDirectory) ?? manifest.Id;
		string modsRoot = Path.Combine(gameRoot, "mods");
		string targetModDirectory = Path.Combine(modsRoot, modDirectoryName);
		Directory.CreateDirectory(targetModDirectory);

		string targetManifestPath = Path.Combine(targetModDirectory, Path.GetFileName(manifestPath));
		string targetDllPath = Path.Combine(targetModDirectory, manifest.Id + ".dll");

		File.Copy(manifestPath, targetManifestPath, overwrite: true);
		File.Copy(builtDllPath, targetDllPath, overwrite: true);

		deployedDirectory = targetModDirectory;
		deployMessage =
			$"Copied manifest: {targetManifestPath}" + System.Environment.NewLine +
			$"Copied dll: {targetDllPath}";

		return true;
	}

	private static string? ResolveBuildAssemblyPath(string projectDirectory, string projectFilePath)
	{
		string projectName = Path.GetFileNameWithoutExtension(projectFilePath);
		string preferredPath = Path.Combine(projectDirectory, "build", projectName + ".dll");
		if (File.Exists(preferredPath))
		{
			return preferredPath;
		}

		string buildDirectory = Path.Combine(projectDirectory, "build");
		if (!Directory.Exists(buildDirectory))
		{
			return null;
		}

		return Directory
			.GetFiles(buildDirectory, "*.dll", SearchOption.TopDirectoryOnly)
			.OrderByDescending(File.GetLastWriteTimeUtc)
			.ThenBy(static path => path, StringComparer.OrdinalIgnoreCase)
			.FirstOrDefault();
	}

	private static bool TryResolveDevManifest(string projectDirectory, out string manifestPath, out ReForgeModManifest manifest)
	{
		manifestPath = string.Empty;
		manifest = null!;

		string[] candidates = Directory
			.GetFiles(projectDirectory, "*.json", SearchOption.TopDirectoryOnly)
			.OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
			.ToArray();

		foreach (string path in candidates)
		{
			if (!TryReadManifestFromJson(path, out ReForgeModManifest parsed))
			{
				continue;
			}

			if (string.IsNullOrWhiteSpace(parsed.Id))
			{
				continue;
			}

			manifestPath = path;
			manifest = parsed;
			return true;
		}

		return false;
	}

	private static bool TryReadManifestFromJson(string filePath, out ReForgeModManifest manifest)
	{
		manifest = null!;

		try
		{
			string json = File.ReadAllText(filePath);
			ReForgeModManifest? parsed = JsonSerializer.Deserialize<ReForgeModManifest>(json, new JsonSerializerOptions
			{
				PropertyNameCaseInsensitive = true
			});

			if (parsed == null)
			{
				return false;
			}

			manifest = parsed;
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static string? ResolveProjectFile(string modDirectory, string modName)
	{
		string preferred = Path.Combine(modDirectory, modName + ".csproj");
		if (File.Exists(preferred))
		{
			return preferred;
		}

		string[] candidates = Directory
			.GetFiles(modDirectory, "*.csproj", SearchOption.TopDirectoryOnly)
			.OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
			.ToArray();

		return candidates.FirstOrDefault();
	}

	private static string? ResolveManifestFile(string modDirectory)
	{
		string[] candidates = Directory
			.GetFiles(modDirectory, "*.json", SearchOption.TopDirectoryOnly)
			.OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
			.ToArray();

		foreach (string path in candidates)
		{
			if (!TryReadManifestFromJson(path, out ReForgeModManifest manifest))
			{
				continue;
			}

			if (string.IsNullOrWhiteSpace(manifest.Id))
			{
				continue;
			}

			return path;
		}

		return null;
	}

	private static string MergeBuildOutput(string stdout, string stderr)
	{
		if (string.IsNullOrWhiteSpace(stderr))
		{
			return stdout ?? string.Empty;
		}

		if (string.IsNullOrWhiteSpace(stdout))
		{
			return stderr;
		}

		return stdout + System.Environment.NewLine + "----- STDERR -----" + System.Environment.NewLine + stderr;
	}

	private static string TrimBuildOutput(string output, int maxLines, int maxChars)
	{
		if (string.IsNullOrWhiteSpace(output))
		{
			return string.Empty;
		}

		string[] allLines = output
			.Replace("\r\n", "\n", StringComparison.Ordinal)
			.Split('\n');

		int skipCount = Math.Max(0, allLines.Length - maxLines);
		IEnumerable<string> tailLines = allLines.Skip(skipCount);
		string tail = string.Join(System.Environment.NewLine, tailLines);

		if (tail.Length > maxChars)
		{
			tail = tail[^maxChars..];
		}

		if (skipCount <= 0)
		{
			return tail;
		}

		return $"... (trimmed {skipCount} lines)" + System.Environment.NewLine + tail;
	}

	private static string? ResolveGameRootDirectory()
	{
		string executablePath = OS.GetExecutablePath();
		string? gameRoot = Path.GetDirectoryName(executablePath);
		if (string.IsNullOrWhiteSpace(gameRoot))
		{
			return null;
		}

		return gameRoot;
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

	private static string BuildProjectFileText(
		string resourceFolderName,
		string reforgeDllPath,
		string harmonyDllPath,
		string sts2DllPath)
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
		builder.AppendLine("    <Reference Include=\"ReForge\">");
		builder.AppendLine($"      <HintPath>{EscapeXmlValue(reforgeDllPath)}</HintPath>");
		builder.AppendLine("      <Private>false</Private>");
		builder.AppendLine("    </Reference>");
		builder.AppendLine("    <Reference Include=\"0Harmony\">");
		builder.AppendLine($"      <HintPath>{EscapeXmlValue(harmonyDllPath)}</HintPath>");
		builder.AppendLine("      <Private>false</Private>");
		builder.AppendLine("    </Reference>");
		builder.AppendLine("    <Reference Include=\"sts2\">");
		builder.AppendLine($"      <HintPath>{EscapeXmlValue(sts2DllPath)}</HintPath>");
		builder.AppendLine("      <Private>false</Private>");
		builder.AppendLine("    </Reference>");
		builder.AppendLine("  </ItemGroup>");
		builder.AppendLine();
		builder.AppendLine("  <ItemGroup>");
		builder.AppendLine($"    <EmbeddedResource Include=\"{resourceFolderName}\\**\\*\" />");
		builder.AppendLine("  </ItemGroup>");
		builder.AppendLine();
		builder.AppendLine("</Project>");
		return builder.ToString();
	}

	private static void ResolveReferencePaths(
		string gameRoot,
		out string reforgeDllPath,
		out string harmonyDllPath,
		out string sts2DllPath)
	{
		reforgeDllPath = Path.GetFullPath(Assembly.GetExecutingAssembly().Location);

		string dataDirectory = Path.Combine(gameRoot, "data_sts2_windows_x86_64");
		harmonyDllPath = Path.GetFullPath(Path.Combine(dataDirectory, "0Harmony.dll"));
		sts2DllPath = Path.GetFullPath(Path.Combine(dataDirectory, "sts2.dll"));
	}

	private static string EscapeXmlValue(string value)
	{
		if (string.IsNullOrEmpty(value))
		{
			return string.Empty;
		}

		return value
			.Replace("&", "&amp;", StringComparison.Ordinal)
			.Replace("<", "&lt;", StringComparison.Ordinal)
			.Replace(">", "&gt;", StringComparison.Ordinal)
			.Replace("\"", "&quot;", StringComparison.Ordinal)
			.Replace("'", "&apos;", StringComparison.Ordinal);
	}

	private static string BuildManifestText(string modName, string author, string version, string modId)
	{
		ReForgeModManifest manifest = new()
		{
			Id = modId,
			Name = modName,
			Author = author,
			Description = "A development mod created by ReForge.",
			Version = version,
			HasPck = false,
			HasDll = true,
			HasEmbeddedResources = true,
			Dependencies = new List<string>(),
			AffectsGameplay = true
		};

		return JsonSerializer.Serialize(manifest, new JsonSerializerOptions
		{
			WriteIndented = true
		});
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
