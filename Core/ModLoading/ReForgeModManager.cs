#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Godot;
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

	public static event Action<ReForgeModContext>? OnModDetected;

	public static void Initialize(ReForgeModSettings? settings = null)
	{
		if (_initialized)
		{
			return;
		}

		_initialized = true;
		ReForgeModSettings actualSettings = settings ?? ReForgeModSettings.Default;
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
}
