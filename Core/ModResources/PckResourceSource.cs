#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using ReForgeFramework.ModLoading;

namespace ReForgeFramework.ModResources;

public sealed class PckResourceSource : IModResourceSource
{
	private readonly HashSet<string> _mountedMods = new(StringComparer.OrdinalIgnoreCase);

	public string Name => "pck";

	public bool CanHandle(ReForgeModContext mod)
	{
		ArgumentNullException.ThrowIfNull(mod);
		return mod.Manifest.HasPck;
	}

	public bool Prepare(ReForgeModContext mod, ReForgeModDiagnostics diagnostics)
	{
		ArgumentNullException.ThrowIfNull(mod);
		ArgumentNullException.ThrowIfNull(diagnostics);

		if (!mod.Manifest.HasPck)
		{
			return false;
		}

		if (_mountedMods.Contains(mod.ModId))
		{
			return true;
		}

		string pckPath = Path.Combine(mod.ModPath, mod.ModId + ".pck");
		if (!File.Exists(pckPath))
		{
			diagnostics.TrackPhase(mod.ModId, ReForgeModPhase.ResourceBinding, ReForgeModLoadState.Failed, $"PCK file not found: {pckPath}");
			return false;
		}

		if (!ProjectSettings.LoadResourcePack(pckPath))
		{
			diagnostics.TrackPhase(mod.ModId, ReForgeModPhase.ResourceBinding, ReForgeModLoadState.Failed, $"Failed to mount PCK: {pckPath}");
			return false;
		}

		_mountedMods.Add(mod.ModId);
		diagnostics.TrackPhase(mod.ModId, ReForgeModPhase.ResourceBinding, ReForgeModLoadState.Loaded, $"Mounted PCK: {pckPath}");
		return true;
	}

	public bool Exists(ReForgeModContext mod, string resourcePath)
	{
		ArgumentNullException.ThrowIfNull(mod);
		ArgumentException.ThrowIfNullOrWhiteSpace(resourcePath);
		return ResourceLoader.Exists(resourcePath);
	}

	public byte[]? ReadAllBytes(ReForgeModContext mod, string resourcePath)
	{
		ArgumentNullException.ThrowIfNull(mod);
		ArgumentException.ThrowIfNullOrWhiteSpace(resourcePath);

		try
		{
			if (!Exists(mod, resourcePath))
			{
				return null;
			}

			return Godot.FileAccess.GetFileAsBytes(resourcePath);
		}
		catch
		{
			return null;
		}
	}
}
