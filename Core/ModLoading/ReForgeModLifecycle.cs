#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using ReForgeFramework.ModResources;

namespace ReForgeFramework.ModLoading;

public sealed class ReForgeModLifecycle
{
	private readonly ReForgeModFileIo _fileIo;
	private readonly ReForgeModDiagnostics _diagnostics;
	private readonly PckResourceSource _pckSource;
	private readonly EmbeddedResourceSource _embeddedSource;

	public ReForgeModLifecycle(
		ReForgeModFileIo fileIo,
		ReForgeModDiagnostics diagnostics,
		PckResourceSource pckSource,
		EmbeddedResourceSource embeddedSource)
	{
		_fileIo = fileIo;
		_diagnostics = diagnostics;
		_pckSource = pckSource;
		_embeddedSource = embeddedSource;
	}

	public List<ReForgeModContext> DiscoverMods()
	{
		List<ReForgeModContext> results = new();
		string executablePath = OS.GetExecutablePath();
		string? gameDirectory = Path.GetDirectoryName(executablePath);
		if (string.IsNullOrWhiteSpace(gameDirectory))
		{
			return results;
		}

		string modsRoot = Path.Combine(gameDirectory, "mods");
		if (!_fileIo.DirectoryExists(modsRoot))
		{
			return results;
		}

		ReadModsInDirRecursive(modsRoot, results);
		return results;
	}

	public List<ReForgeModContext> ValidateAndSort(List<ReForgeModContext> mods)
	{
		ArgumentNullException.ThrowIfNull(mods);
		Dictionary<string, List<ReForgeModContext>> byId = mods
			.GroupBy(m => m.ModId, StringComparer.OrdinalIgnoreCase)
			.ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

		foreach ((string modId, List<ReForgeModContext> duplicates) in byId)
		{
			if (duplicates.Count <= 1)
			{
				continue;
			}

			for (int i = 1; i < duplicates.Count; i++)
			{
				MarkFailed(duplicates[i], $"Duplicate mod id detected: {modId}");
			}
		}

		HashSet<string> knownIds = byId.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
		foreach (ReForgeModContext mod in mods)
		{
			if (mod.State == ReForgeModLoadState.Failed)
			{
				continue;
			}

			foreach (string dependency in mod.Manifest.Dependencies ?? new List<string>())
			{
				if (knownIds.Contains(dependency))
				{
					continue;
				}

				MarkFailed(mod, $"Missing dependency: {dependency}");
				break;
			}
		}

		Dictionary<string, int> indegree = new(StringComparer.OrdinalIgnoreCase);
		Dictionary<string, List<string>> edges = new(StringComparer.OrdinalIgnoreCase);
		foreach (ReForgeModContext mod in mods.Where(m => m.State == ReForgeModLoadState.None))
		{
			indegree[mod.ModId] = 0;
			edges[mod.ModId] = new List<string>();
		}

		foreach (ReForgeModContext mod in mods.Where(m => m.State == ReForgeModLoadState.None))
		{
			foreach (string dep in mod.Manifest.Dependencies ?? new List<string>())
			{
				if (!indegree.ContainsKey(dep))
				{
					continue;
				}

				edges[dep].Add(mod.ModId);
				indegree[mod.ModId]++;
			}
		}

		Queue<string> queue = new(indegree.Where(item => item.Value == 0).Select(item => item.Key));
		List<string> sortedIds = new();
		while (queue.Count > 0)
		{
			string current = queue.Dequeue();
			sortedIds.Add(current);
			foreach (string next in edges[current])
			{
				indegree[next]--;
				if (indegree[next] == 0)
				{
					queue.Enqueue(next);
				}
			}
		}

		if (sortedIds.Count != indegree.Count)
		{
			foreach (string modId in indegree.Keys)
			{
				if (sortedIds.Contains(modId, StringComparer.OrdinalIgnoreCase))
				{
					continue;
				}

				ReForgeModContext? mod = mods.FirstOrDefault(m => m.ModId.Equals(modId, StringComparison.OrdinalIgnoreCase));
				if (mod != null)
				{
					MarkFailed(mod, "Circular dependency detected.");
				}
			}
		}

		List<ReForgeModContext> ordered = new();
		ordered.AddRange(mods.Where(m => m.State == ReForgeModLoadState.Failed));
		foreach (string modId in sortedIds)
		{
			ReForgeModContext? mod = mods.FirstOrDefault(m => m.ModId.Equals(modId, StringComparison.OrdinalIgnoreCase));
			if (mod != null)
			{
				ordered.Add(mod);
			}
		}

		return ordered;
	}

	public void TryLoad(ReForgeModContext mod, ReForgeModSettings settings, HashSet<string> loadedIds)
	{
		ArgumentNullException.ThrowIfNull(mod);
		ArgumentNullException.ThrowIfNull(settings);
		ArgumentNullException.ThrowIfNull(loadedIds);

		if (mod.State != ReForgeModLoadState.None)
		{
			return;
		}

		if (settings.DisabledModIds.Contains(mod.ModId))
		{
			mod.State = ReForgeModLoadState.Disabled;
			_diagnostics.TrackPhase(mod.ModId, ReForgeModPhase.Validation, mod.State, "Disabled by settings.");
			return;
		}

		if (!settings.PlayerAgreedToModLoading)
		{
			mod.State = ReForgeModLoadState.Disabled;
			_diagnostics.TrackPhase(mod.ModId, ReForgeModPhase.Validation, mod.State, "Player has not agreed to mod loading.");
			return;
		}

		foreach (string dependency in mod.Manifest.Dependencies ?? new List<string>())
		{
			if (loadedIds.Contains(dependency))
			{
				continue;
			}

			MarkFailed(mod, $"Dependency not loaded: {dependency}");
			return;
		}

		if (!TryLoadAssembly(mod))
		{
			return;
		}

		if (!BindResourceSource(mod))
		{
			return;
		}

		if (!InvokeInitializer(mod))
		{
			return;
		}

		mod.State = ReForgeModLoadState.Loaded;
		_diagnostics.TrackPhase(mod.ModId, ReForgeModPhase.Completed, mod.State, "Mod loaded successfully.");
	}

	private void ReadModsInDirRecursive(string path, List<ReForgeModContext> results)
	{
		foreach (string fileName in _fileIo.GetFilesAt(path))
		{
			if (!fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			string manifestPath = Path.Combine(path, fileName);
			ReForgeModContext? context = TryReadManifest(manifestPath);
			if (context != null)
			{
				results.Add(context);
			}
		}

		foreach (string childDir in _fileIo.GetDirectoriesAt(path))
		{
			string next = Path.Combine(path, childDir);
			if (_fileIo.DirectoryExists(next))
			{
				ReadModsInDirRecursive(next, results);
			}
		}
	}

	private ReForgeModContext? TryReadManifest(string manifestPath)
	{
		try
		{
			string json = _fileIo.ReadAllText(manifestPath);
			ReForgeModManifest? manifest = JsonSerializer.Deserialize<ReForgeModManifest>(json, new JsonSerializerOptions
			{
				PropertyNameCaseInsensitive = true
			});

			if (manifest?.Id == null)
			{
				return null;
			}

			ReForgeModContext context = new()
			{
				ModId = manifest.Id,
				ManifestPath = manifestPath,
				ModPath = Path.GetDirectoryName(manifestPath) ?? string.Empty,
				Manifest = manifest,
				State = ReForgeModLoadState.None,
				SourceKind = ReForgeModSourceKind.Unknown
			};

			_diagnostics.TrackPhase(context.ModId, ReForgeModPhase.Discovery, context.State, $"Detected manifest: {manifestPath}");
			return context;
		}
		catch
		{
			return null;
		}
	}

	private bool TryLoadAssembly(ReForgeModContext mod)
	{
		if (!mod.Manifest.HasDll)
		{
			_diagnostics.TrackPhase(mod.ModId, ReForgeModPhase.AssemblyLoad, mod.State, "Manifest does not require DLL load.");
			return true;
		}

		string assemblyPath = Path.Combine(mod.ModPath, mod.ModId + ".dll");
		if (_fileIo.FileExists(assemblyPath))
		{
			AssemblyLoadContext? loadContext = AssemblyLoadContext.GetLoadContext(Assembly.GetExecutingAssembly());
			if (loadContext == null)
			{
				MarkFailed(mod, $"Could not resolve assembly load context for: {assemblyPath}");
				return false;
			}

			mod.Assembly = loadContext.LoadFromAssemblyPath(assemblyPath);
			_diagnostics.TrackPhase(mod.ModId, ReForgeModPhase.AssemblyLoad, mod.State, $"Loaded assembly: {assemblyPath}");
			return true;
		}

		if (mod.ModId.Equals("reforge", StringComparison.OrdinalIgnoreCase))
		{
			mod.Assembly = Assembly.GetExecutingAssembly();
			_diagnostics.TrackPhase(mod.ModId, ReForgeModPhase.AssemblyLoad, mod.State, "Using executing assembly for self mod context.");
			return true;
		}

		MarkFailed(mod, $"Manifest requires DLL but file not found: {assemblyPath}");
		return false;
	}

	private bool BindResourceSource(ReForgeModContext mod)
	{
		if (mod.Manifest.HasPck && _pckSource.CanHandle(mod) && _pckSource.Prepare(mod, _diagnostics))
		{
			mod.SourceKind = ReForgeModSourceKind.Pck;
			return true;
		}

		if (_embeddedSource.CanHandle(mod) && _embeddedSource.Prepare(mod, _diagnostics))
		{
			mod.SourceKind = ReForgeModSourceKind.Embedded;
			return true;
		}

		if (mod.Manifest.HasPck || mod.Manifest.HasEmbeddedResources)
		{
			MarkFailed(mod, "No resource source could be prepared.");
			return false;
		}

		return true;
	}

	private bool InvokeInitializer(ReForgeModContext mod)
	{
		if (mod.Assembly == null)
		{
			return true;
		}

		if (ReferenceEquals(mod.Assembly, Assembly.GetExecutingAssembly()) && mod.ModId.Equals("reforge", StringComparison.OrdinalIgnoreCase))
		{
			_diagnostics.TrackPhase(mod.ModId, ReForgeModPhase.Initialization, mod.State, "Skipped self-initializer invocation to avoid recursion.");
			return true;
		}

		try
		{
			List<Type> initializerTypes = mod.Assembly.GetTypes()
				.Where(t => t.GetCustomAttribute<ModInitializerAttribute>() != null)
				.ToList();

			if (initializerTypes.Count > 0)
			{
				foreach (Type type in initializerTypes)
				{
					if (!CallModInitializer(type))
					{
						MarkFailed(mod, $"Initializer invocation failed for type: {type.FullName}");
						return false;
					}
				}

				_diagnostics.TrackPhase(mod.ModId, ReForgeModPhase.Initialization, mod.State, $"Invoked {initializerTypes.Count} initializer(s).");
				return true;
			}

			Harmony harmony = new(($"{mod.Manifest.Author ?? "unknown"}.{mod.ModId}"));
			harmony.PatchAll(mod.Assembly);
			_diagnostics.TrackPhase(mod.ModId, ReForgeModPhase.Initialization, mod.State, "No ModInitializer found, Harmony.PatchAll executed.");
			return true;
		}
		catch (Exception ex)
		{
			MarkFailed(mod, $"Initializer execution exception: {ex.GetType().Name}, {ex.Message}");
			return false;
		}
	}

	private static bool CallModInitializer(Type initializerType)
	{
		ModInitializerAttribute? attribute = initializerType.GetCustomAttribute<ModInitializerAttribute>();
		if (attribute == null)
		{
			return false;
		}

		MethodInfo? method = initializerType.GetMethod(attribute.initializerMethod, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
		if (method == null)
		{
			return false;
		}

		method.Invoke(null, null);
		return true;
	}

	private void MarkFailed(ReForgeModContext mod, string message)
	{
		mod.State = ReForgeModLoadState.Failed;
		mod.Errors.Add(message);
		_diagnostics.TrackPhase(mod.ModId, ReForgeModPhase.Validation, mod.State, message);
	}
}
