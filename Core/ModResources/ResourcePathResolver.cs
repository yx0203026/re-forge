#nullable enable

using System;

namespace ReForgeFramework.ModResources;

public static class ResourcePathResolver
{
	private const string ResPrefix = "res://";

	public static string Normalize(string resourcePath)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(resourcePath);
		string value = resourcePath.Trim().Replace('\\', '/');
		if (value.StartsWith(ResPrefix, StringComparison.OrdinalIgnoreCase))
		{
			return value;
		}

		if (value.StartsWith('/'))
		{
			value = value.TrimStart('/');
		}

		return ResPrefix + value;
	}

	public static string NormalizeToRelative(string resourcePath)
	{
		string normalized = Normalize(resourcePath);
		if (normalized.StartsWith(ResPrefix, StringComparison.OrdinalIgnoreCase))
		{
			normalized = normalized[ResPrefix.Length..];
		}

		return normalized.Trim().Replace('\\', '/');
	}

	public static string? ResolveOwnerModId(string resourcePath)
	{
		string relative = NormalizeToRelative(resourcePath);
		int separator = relative.IndexOf('/');
		if (separator <= 0)
		{
			return null;
		}

		return relative[..separator];
	}

	public static string BuildLocalizationFilePath(string modId, string language, string table)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(modId);
		ArgumentException.ThrowIfNullOrWhiteSpace(language);
		ArgumentException.ThrowIfNullOrWhiteSpace(table);
		return Normalize($"{modId}/localization/{language}/{table}.json");
	}
}
