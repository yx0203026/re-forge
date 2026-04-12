#nullable enable

using System;
using System.Collections.Generic;
using Godot;

namespace ReForgeFramework.ModLoading;

public sealed class ReForgeModDiagnostics
{
	private readonly List<ReForgeModDiagnosticEvent> _events = new();
	private readonly object _lock = new();
	private readonly HashSet<string> _resourceMissFingerprints = new(StringComparer.Ordinal);

	public void TrackPhase(string modId, ReForgeModPhase phase, ReForgeModLoadState state, string detail)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(modId);
		ArgumentException.ThrowIfNullOrWhiteSpace(detail);

		Append(new ReForgeModDiagnosticEvent
		{
			ModId = modId,
			Phase = phase,
			State = state,
			Message = detail
		});

		GD.Print($"[ReForge.ModLoader] [{phase}] [{state}] {modId}: {detail}");
	}

	public void TrackResourceResolve(string modId, string resourcePath, string source, bool success, string detail)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(modId);
		ArgumentException.ThrowIfNullOrWhiteSpace(resourcePath);
		ArgumentException.ThrowIfNullOrWhiteSpace(source);
		ArgumentException.ThrowIfNullOrWhiteSpace(detail);

		if (success)
		{
			// 资源命中属于高频热路径，跳过逐条诊断与日志，避免 UI 线程抖动。
			return;
		}

		string fingerprint = string.Concat(modId, "|", source, "|", resourcePath);
		lock (_lock)
		{
			if (!_resourceMissFingerprints.Add(fingerprint))
			{
				return;
			}
		}

		Append(new ReForgeModDiagnosticEvent
		{
			ModId = modId,
			Phase = ReForgeModPhase.ResourceBinding,
			State = ReForgeModLoadState.Failed,
			Message = detail,
			ResourcePath = resourcePath,
			Source = source
		});

		GD.PrintErr($"[ReForge.ModLoader] [Resource] [Miss] {modId} ({source}) -> {resourcePath}, {detail}");
	}

	public ReForgeModDiagnosticsSnapshot BuildSnapshot()
	{
		lock (_lock)
		{
			return new ReForgeModDiagnosticsSnapshot
			{
				Events = _events.ToArray()
			};
		}
	}

	private void Append(ReForgeModDiagnosticEvent item)
	{
		lock (_lock)
		{
			_events.Add(item);
		}
	}
}
