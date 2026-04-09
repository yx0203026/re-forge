#nullable enable

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json.Serialization;

namespace ReForgeFramework.ModLoading;

public enum ReForgeModLoadState
{
	None = 0,
	Disabled = 1,
	Loaded = 2,
	Failed = 3,
	AddedAtRuntime = 4
}

public enum ReForgeModSourceKind
{
	Unknown = 0,
	Pck = 1,
	Embedded = 2
}

public enum ReForgeModPhase
{
	Discovery = 0,
	Validation = 1,
	ResourceBinding = 2,
	AssemblyLoad = 3,
	Initialization = 4,
	Completed = 5
}

public sealed class ReForgeModManifest
{
	[JsonPropertyName("id")]
	public string? Id { get; init; }

	[JsonPropertyName("name")]
	public string? Name { get; init; }

	[JsonPropertyName("author")]
	public string? Author { get; init; }

	[JsonPropertyName("description")]
	public string? Description { get; init; }

	[JsonPropertyName("version")]
	public string? Version { get; init; }

	[JsonPropertyName("has_pck")]
	public bool HasPck { get; init; }

	[JsonPropertyName("has_dll")]
	public bool HasDll { get; init; }

	[JsonPropertyName("has_embedded_resources")]
	public bool HasEmbeddedResources { get; init; }

	[JsonPropertyName("dependencies")]
	public List<string>? Dependencies { get; init; }

	[JsonPropertyName("affects_gameplay")]
	public bool AffectsGameplay { get; init; } = true;
}

public sealed class ReForgeModContext
{
	public required string ModId { get; init; }

	public required string ManifestPath { get; init; }

	public required string ModPath { get; init; }

	public required ReForgeModManifest Manifest { get; init; }

	public ReForgeModLoadState State { get; set; }

	public ReForgeModSourceKind SourceKind { get; set; }

	public Assembly? Assembly { get; set; }

	public List<string> Errors { get; } = new();
}

public sealed class ReForgeDevModProject
{
	public required string ModName { get; init; }

	public required string ModDirectory { get; init; }

	public string? ProjectFilePath { get; init; }

	public bool HasProjectFile => !string.IsNullOrWhiteSpace(ProjectFilePath);

	public string? ManifestFilePath { get; init; }

	public bool HasManifestFile => !string.IsNullOrWhiteSpace(ManifestFilePath);

	public bool HasModMainFile { get; init; }

	public bool HasResourceDirectory { get; init; }

	public DateTimeOffset LastModifiedAtUtc { get; init; }
}

public sealed class ReForgeDevBuildResult
{
	public required bool Succeeded { get; init; }

	public required string Summary { get; init; }

	public required string Output { get; init; }
}

public sealed class ReForgeModRuntimeActionResult
{
	public required bool Succeeded { get; init; }

	public required bool RequiresRestart { get; init; }

	public required string Summary { get; init; }

	public required string Details { get; init; }
}

public sealed class ReForgeModSettings
{
	public static ReForgeModSettings Default { get; } = new();

	public bool PlayerAgreedToModLoading { get; init; } = true;

	public HashSet<string> DisabledModIds { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ReForgeModDiagnosticEvent
{
	public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

	public required string ModId { get; init; }

	public required ReForgeModPhase Phase { get; init; }

	public required ReForgeModLoadState State { get; init; }

	public required string Message { get; init; }

	public string? ResourcePath { get; init; }

	public string? Source { get; init; }
}

public sealed class ReForgeModDiagnosticsSnapshot
{
	public required IReadOnlyList<ReForgeModDiagnosticEvent> Events { get; init; }
}
