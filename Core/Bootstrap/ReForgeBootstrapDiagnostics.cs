#nullable enable

using System;
using Godot;

internal static class ReForgeBootstrapDiagnostics
{
	private const string Prefix = "[ReForge.Bootstrap]";

	public static void TrackPhaseStart(string phase, string detail)
	{
		Write(phase, "Start", detail, isError: false, exception: null);
	}

	public static void TrackPhaseCompleted(string phase, string detail)
	{
		Write(phase, "Completed", detail, isError: false, exception: null);
	}

	public static void TrackPhaseDegraded(string phase, string detail, Exception? exception = null)
	{
		Write(phase, "Degraded", detail, isError: true, exception);
	}

	public static void TrackPhaseFailed(string phase, string detail, Exception exception)
	{
		ArgumentNullException.ThrowIfNull(exception);
		Write(phase, "Failed", detail, isError: true, exception);
	}

	private static void Write(string phase, string state, string detail, bool isError, Exception? exception)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(phase);
		ArgumentException.ThrowIfNullOrWhiteSpace(state);
		ArgumentException.ThrowIfNullOrWhiteSpace(detail);

		string line = exception is null
			? $"{Prefix} [{phase}] [{state}] {detail}"
			: $"{Prefix} [{phase}] [{state}] {detail}, reason='{exception.GetType().Name}: {exception.Message}'.";

		if (isError)
		{
			GD.PrintErr(line);
			return;
		}

		GD.Print(line);
	}
}
