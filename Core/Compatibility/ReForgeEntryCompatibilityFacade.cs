using System;
using Godot;

internal static class ReForgeEntryCompatibilityFacade
{
	private static readonly object MigrationLogGate = new();
	private static bool _migrationHintLogged;

	public static void InitializeCompatEntry(Action initializeNewEntry)
	{
		Forward(
			action: initializeNewEntry,
			oldEntryName: "ReForge.InitializeCompatEntry()",
			newEntryName: "[ModInitializer(nameof(Initialize))] -> ReForge.Initialize()"
		);
	}

	public static void Forward(Action action, string oldEntryName, string newEntryName)
	{
		ArgumentNullException.ThrowIfNull(action);
		_ = Forward(
			action: () =>
			{
				action();
				return true;
			},
			oldEntryName: oldEntryName,
			newEntryName: newEntryName
		);
	}

	public static T Forward<T>(Func<T> action, string oldEntryName, string newEntryName)
	{
		ArgumentNullException.ThrowIfNull(action);
		ArgumentException.ThrowIfNullOrWhiteSpace(oldEntryName);
		ArgumentException.ThrowIfNullOrWhiteSpace(newEntryName);

		LogMigrationHintOnce(oldEntryName, newEntryName);
		return action();
	}

	private static void LogMigrationHintOnce(string oldEntryName, string newEntryName)
	{
		lock (MigrationLogGate)
		{
			if (_migrationHintLogged)
			{
				return;
			}

			_migrationHintLogged = true;
			GD.Print($"[ReForge] Compatibility entry used. '{oldEntryName}' is kept for backward compatibility. Please migrate to '{newEntryName}'.");
		}
	}
}
