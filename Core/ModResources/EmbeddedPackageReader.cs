#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace ReForgeFramework.ModResources;

public sealed class EmbeddedPackageReader
{
	private readonly Assembly _assembly;
	private readonly Dictionary<string, string> _pathToResource = new(StringComparer.OrdinalIgnoreCase);
	private readonly object _bytesCacheSync = new();
	private readonly Dictionary<string, byte[]> _resourceBytesCache = new(StringComparer.OrdinalIgnoreCase);

	public EmbeddedPackageReader(Assembly assembly)
	{
		_assembly = assembly ?? throw new ArgumentNullException(nameof(assembly));
		BuildMap();
	}

	public IReadOnlyCollection<string> KnownPaths => _pathToResource.Keys;

	public bool HasPath(string resourcePath)
	{
		string normalized = ResourcePathResolver.NormalizeToRelative(resourcePath);
		return _pathToResource.ContainsKey(normalized);
	}

	public bool TryReadAllBytes(string resourcePath, out byte[] bytes)
	{
		bytes = Array.Empty<byte>();
		string normalized = ResourcePathResolver.NormalizeToRelative(resourcePath);
		lock (_bytesCacheSync)
		{
			if (_resourceBytesCache.TryGetValue(normalized, out byte[]? cached))
			{
				bytes = cached;
				return true;
			}
		}

		if (!_pathToResource.TryGetValue(normalized, out string? resourceName))
		{
			return false;
		}

		using Stream? stream = _assembly.GetManifestResourceStream(resourceName);
		if (stream == null)
		{
			return false;
		}

		using MemoryStream memory = new();
		stream.CopyTo(memory);
		bytes = memory.ToArray();
		lock (_bytesCacheSync)
		{
			_resourceBytesCache[normalized] = bytes;
		}
		return true;
	}

	private void BuildMap()
	{
		foreach (string resourceName in _assembly.GetManifestResourceNames())
		{
			AddCandidate(resourceName, resourceName);

			if (TryConvertConventionalName(resourceName, out string? normalizedPath))
			{
				if (!string.IsNullOrWhiteSpace(normalizedPath))
				{
					AddCandidate(normalizedPath, resourceName);
				}
			}
		}
	}

	private void AddCandidate(string resourcePath, string resourceName)
	{
		string normalized = ResourcePathResolver.NormalizeToRelative(resourcePath);
		if (string.IsNullOrWhiteSpace(normalized))
		{
			return;
		}

		if (!_pathToResource.ContainsKey(normalized))
		{
			_pathToResource[normalized] = resourceName;
		}
	}

	private bool TryConvertConventionalName(string resourceName, out string? normalizedPath)
	{
		normalizedPath = null;
		if (string.IsNullOrWhiteSpace(resourceName))
		{
			return false;
		}

		// 显式 LogicalName（包含斜杠）直接作为可读取路径。
		if (resourceName.IndexOf('/', StringComparison.Ordinal) >= 0)
		{
			normalizedPath = resourceName;
			return true;
		}

		string candidate = resourceName;
		string? assemblyName = _assembly.GetName().Name;
		if (!string.IsNullOrWhiteSpace(assemblyName))
		{
			string assemblyPrefix = assemblyName + ".";
			if (candidate.StartsWith(assemblyPrefix, StringComparison.OrdinalIgnoreCase))
			{
				candidate = candidate[assemblyPrefix.Length..];
			}
		}

		// 兼容旧版路径（例如 ReForge.rewardrefresh.localization.xxx.json）。
		int marker = candidate.IndexOf(".reforge.", StringComparison.OrdinalIgnoreCase);
		if (marker >= 0)
		{
			candidate = candidate[(marker + 1)..];
		}

		if (candidate.StartsWith("reforge/", StringComparison.OrdinalIgnoreCase))
		{
			normalizedPath = candidate;
			return true;
		}

		string[] parts = candidate.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		if (parts.Length < 2)
		{
			return false;
		}

		string extension = parts[^1];
		string body = string.Join('/', parts.Take(parts.Length - 1));
		normalizedPath = body + "." + extension;
		return true;
	}
}
