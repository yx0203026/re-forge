#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;
using Godot;
using HarmonyLib;
using ReForgeFramework.Mixins.Runtime;

public static partial class ReForge
{
	public static class Mixins
	{
		private static readonly object SyncRoot = new();
		private static readonly Dictionary<string, MixinRegistrationResult> Registrations = new(StringComparer.Ordinal);
		private static readonly MixinLifecycleManager LifecycleManager = new();
		private static readonly MixinDiagnostics Diagnostics = new();

		private static bool _warningPrinted;

		public static MixinRegistrationResult Register(MixinRegistrationOptions options)
		{
			ArgumentNullException.ThrowIfNull(options);
			ValidateRegistrationSource(options.Source);
			MixinLifecycleInstallResult installResult = LifecycleManager.InitializeAndInstall(options);
			MixinRegistrationResult result = BuildRegistrationResultFromInstall(options, installResult);

			lock (SyncRoot)
			{
				Registrations[options.ModId] = result;
				_warningPrinted = false;
			}

			if (result.State == MixinRegistrationState.Registered && result.Summary.Failed == 0)
			{
				GD.Print(
					$"[ReForge.Mixins] Explicit registration completed. modId='{options.ModId}', strictMode={options.StrictMode}, installed={result.Summary.Installed}, failed={result.Summary.Failed}, skipped={result.Summary.Skipped}."
				);
			}
			else
			{
				GD.PrintErr(
					$"[ReForge.Mixins] Explicit registration finished with issues. modId='{options.ModId}', strictMode={options.StrictMode}, installed={result.Summary.Installed}, failed={result.Summary.Failed}, skipped={result.Summary.Skipped}, state='{result.State}', message='{result.Message}'."
				);
			}

			if (installResult.State == MixinLifecycleState.Failed || result.Summary.Failed > 0)
			{
				throw new InvalidOperationException(
					$"Mixin registration failed. modId='{options.ModId}', strictMode={options.StrictMode}, installed={result.Summary.Installed}, failed={result.Summary.Failed}, skipped={result.Summary.Skipped}, state='{installResult.State}', message='{installResult.Message}'."
				);
			}

			return result;
		}

		public static MixinRegistrationResult Register(Assembly assembly, string modId, Harmony harmony, bool strictMode = true)
		{
			MixinRegistrationOptions options = MixinRegistrationOptions.CreateMainClassOptions(
				assembly,
				modId,
				harmony,
				strictMode
			);

			return Register(options);
		}

		public static MixinUnregisterResult UnregisterAll(string modId)
		{
			ValidateRequiredKey(modId, nameof(modId));
			MixinLifecycleUnloadResult unloadResult = LifecycleManager.Unload(modId);
			MixinModLifecycleStatus? status = LifecycleManager.GetStatus(modId);

			MixinRegistrationSummary summary = status == null
				? new MixinRegistrationSummary(0, 0, 0)
				: new MixinRegistrationSummary(status.Counters.Installed, status.Counters.Failed, status.Counters.Skipped);

			MixinRegistrationResult registrationResult = new(
				modId,
				MixinRegistrationSource.MainClassExplicit,
				MapLifecycleStateToRegistrationState(unloadResult.State),
				status?.StrictMode ?? false,
				summary,
				unloadResult.Message,
				unloadResult.TimestampUtc
			);

			lock (SyncRoot)
			{
				Registrations[modId] = registrationResult;
				_warningPrinted = false;
			}

			bool removed = unloadResult.RemovedCount > 0 || unloadResult.State == MixinLifecycleState.Unloaded;
			if (removed)
			{
				GD.Print($"[ReForge.Mixins] Unregistered mixins. modId='{modId}', removed={unloadResult.RemovedCount}, unpatchFailures={unloadResult.UnpatchFailures}.");
			}
			else
			{
				GD.Print($"[ReForge.Mixins] Unregister skipped. modId='{modId}', message='{unloadResult.Message}'.");
			}

			return new MixinUnregisterResult(
				modId,
				Removed: removed,
				RemovedInstalledCount: unloadResult.RemovedCount,
				RemovedFailedCount: unloadResult.UnpatchFailures,
				unloadResult.Message,
				unloadResult.TimestampUtc
			);
		}

		public static MixinStatusSnapshot GetStatus()
		{
			bool shouldWarn = false;
			IReadOnlyDictionary<string, MixinRegistrationResult> snapshot;
			Dictionary<string, MixinRegistrationResult> combined = new(StringComparer.Ordinal);

			lock (SyncRoot)
			{
				foreach (KeyValuePair<string, MixinRegistrationResult> pair in Registrations)
				{
					combined[pair.Key] = pair.Value;
				}
			}

			MixinLifecycleSnapshot lifecycleSnapshot = LifecycleManager.Snapshot();
			foreach (KeyValuePair<string, MixinModLifecycleStatus> pair in lifecycleSnapshot.Mods)
			{
				MixinRegistrationSource source = MixinRegistrationSource.MainClassExplicit;
				if (combined.TryGetValue(pair.Key, out MixinRegistrationResult? existing))
				{
					source = existing.Source;
				}

				combined[pair.Key] = BuildRegistrationResultFromLifecycleStatus(pair.Key, source, pair.Value);
			}

			lock (SyncRoot)
			{
				snapshot = new ReadOnlyDictionary<string, MixinRegistrationResult>(combined);
				if (snapshot.Count == 0 && !_warningPrinted)
				{
					_warningPrinted = true;
					shouldWarn = true;
				}
			}

			if (shouldWarn)
			{
				GD.Print("[ReForge.Mixins] Warning: no explicit registration found. Please call ReForge.Mixins.Register(...) in mod main initializer.");
			}

			return new MixinStatusSnapshot(
				isExplicitlyRegistered: snapshot.Count > 0,
				registeredModCount: snapshot.Count,
				registrations: snapshot
			);
		}

		public static MixinDiagnosticsSnapshot GetDiagnosticsSnapshot()
		{
			MixinStatusSnapshot registrationStatus = GetStatus();
			MixinLifecycleSnapshot lifecycleSnapshot = LifecycleManager.Snapshot();
			IReadOnlyList<MixinAppliedEntry> appliedEntries = LifecycleManager.GetAppliedEntries();
			return Diagnostics.BuildSnapshot(registrationStatus, lifecycleSnapshot, appliedEntries);
		}

		public static string GetDiagnosticsJson(bool indented = true)
		{
			MixinDiagnosticsSnapshot snapshot = GetDiagnosticsSnapshot();
			return Diagnostics.ToJson(snapshot, indented);
		}

		private static void ValidateRegistrationSource(MixinRegistrationSource source)
		{
			if (source != MixinRegistrationSource.MainClassExplicit)
			{
				throw new ArgumentException("Only main-class explicit registration is allowed.", nameof(source));
			}
		}

		private static MixinRegistrationResult BuildRegistrationResultFromInstall(
			MixinRegistrationOptions options,
			MixinLifecycleInstallResult installResult)
		{
			MixinRegistrationSummary summary = new(
				installResult.Counters.Installed,
				installResult.Counters.Failed,
				installResult.Counters.Skipped
			);

			return new MixinRegistrationResult(
				options.ModId,
				options.Source,
				MapLifecycleStateToRegistrationState(installResult.State),
				options.StrictMode,
				summary,
				installResult.Message,
				installResult.TimestampUtc
			);
		}

		private static MixinRegistrationResult BuildRegistrationResultFromLifecycleStatus(
			string modId,
			MixinRegistrationSource source,
			MixinModLifecycleStatus lifecycleStatus)
		{
			MixinRegistrationSummary summary = new(
				lifecycleStatus.Counters.Installed,
				lifecycleStatus.Counters.Failed,
				lifecycleStatus.Counters.Skipped
			);

			return new MixinRegistrationResult(
				modId,
				source,
				MapLifecycleStateToRegistrationState(lifecycleStatus.State),
				lifecycleStatus.StrictMode,
				summary,
				lifecycleStatus.Message,
				lifecycleStatus.UpdatedAtUtc
			);
		}

		private static MixinRegistrationState MapLifecycleStateToRegistrationState(MixinLifecycleState state)
		{
			return state switch
			{
				MixinLifecycleState.Active => MixinRegistrationState.Registered,
				MixinLifecycleState.Installing => MixinRegistrationState.Registered,
				MixinLifecycleState.Unloading => MixinRegistrationState.Unregistered,
				MixinLifecycleState.Unloaded => MixinRegistrationState.Unregistered,
				_ => MixinRegistrationState.NotRegistered,
			};
		}

		private static void ValidateRequiredKey(string value, string paramName)
		{
			ArgumentNullException.ThrowIfNull(value);
			if (string.IsNullOrWhiteSpace(value))
			{
				throw new ArgumentException("Value cannot be empty.", paramName);
			}
		}
	}
}
