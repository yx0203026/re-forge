#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using ReForgeFramework.ModLoading;

namespace ReForgeFramework.ModResources;

public sealed class EmbeddedResourceSource : IModResourceSource
{
	private readonly Dictionary<string, EmbeddedPackageReader> _readers = new(StringComparer.OrdinalIgnoreCase);

	public string Name => "embedded";

	public bool CanHandle(ReForgeModContext mod)
	{
		ArgumentNullException.ThrowIfNull(mod);
		return mod.Manifest.HasEmbeddedResources || !mod.Manifest.HasPck;
	}

	public bool Prepare(ReForgeModContext mod, ReForgeModDiagnostics diagnostics)
	{
		ArgumentNullException.ThrowIfNull(mod);
		ArgumentNullException.ThrowIfNull(diagnostics);

		if (_readers.ContainsKey(mod.ModId))
		{
			return true;
		}

		Assembly? assembly = mod.Assembly;
		if (assembly == null && mod.ModId.Equals("reforge", StringComparison.OrdinalIgnoreCase))
		{
			assembly = Assembly.GetExecutingAssembly();
		}

		if (assembly == null)
		{
			diagnostics.TrackPhase(mod.ModId, ReForgeModPhase.ResourceBinding, ReForgeModLoadState.Failed, "Embedded source requires loaded assembly.");
			return false;
		}

		EmbeddedPackageReader reader = new(assembly);
		_readers[mod.ModId] = reader;
		diagnostics.TrackPhase(mod.ModId, ReForgeModPhase.ResourceBinding, ReForgeModLoadState.Loaded, $"Embedded source prepared, indexed {reader.KnownPaths.Count} entries.");
		return true;
	}

	public bool Exists(ReForgeModContext mod, string resourcePath)
	{
		ArgumentNullException.ThrowIfNull(mod);
		ArgumentException.ThrowIfNullOrWhiteSpace(resourcePath);

		if (!_readers.TryGetValue(mod.ModId, out EmbeddedPackageReader? reader))
		{
			return false;
		}

		return reader.HasPath(resourcePath);
	}

	public byte[]? ReadAllBytes(ReForgeModContext mod, string resourcePath)
	{
		ArgumentNullException.ThrowIfNull(mod);
		ArgumentException.ThrowIfNullOrWhiteSpace(resourcePath);

		if (!_readers.TryGetValue(mod.ModId, out EmbeddedPackageReader? reader))
		{
			return null;
		}

		if (!reader.TryReadAllBytes(resourcePath, out byte[] bytes))
		{
			return null;
		}

		return bytes;
	}
}
