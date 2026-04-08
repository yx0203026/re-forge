#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;
using Godot;

namespace ReForgeFramework.Mixins.Runtime;

internal sealed class MixinLifecycleManager
{
	private readonly object _syncRoot = new();
	private readonly Dictionary<string, ModContext> _contexts = new(StringComparer.Ordinal);
	private readonly MixinScanner _scanner;
	private readonly HarmonyPatchBinder _binder;

	public MixinLifecycleManager(MixinScanner? scanner = null, HarmonyPatchBinder? binder = null)
	{
		_scanner = scanner ?? new MixinScanner();
		_binder = binder ?? new HarmonyPatchBinder();
	}

	public MixinLifecycleInstallResult InitializeAndInstall(MixinRegistrationOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);
		string modId = options.ModId;
		DateTimeOffset now = DateTimeOffset.UtcNow;
		string assemblyKey = BuildAssemblyKey(options.Assembly);

		ModContext context;
		lock (_syncRoot)
		{
			if (!_contexts.TryGetValue(modId, out context!))
			{
				context = new ModContext(modId);
				_contexts[modId] = context;
			}

			if (context.State == MixinLifecycleState.Active
				&& string.Equals(context.AssemblyKey, assemblyKey, StringComparison.Ordinal)
				&& string.Equals(context.HarmonyId, options.Harmony.Id, StringComparison.Ordinal)
				&& context.StrictMode == options.StrictMode)
			{
				return new MixinLifecycleInstallResult(
					modId,
					context.State,
					NoOp: true,
					AbortedByStrictMode: false,
					context.Counters,
					"Install skipped because the same mod runtime is already active.",
					now
				);
			}

			if (context.State == MixinLifecycleState.Installing || context.State == MixinLifecycleState.Unloading)
			{
				return new MixinLifecycleInstallResult(
					modId,
					context.State,
					NoOp: true,
					AbortedByStrictMode: false,
					context.Counters,
					"Install skipped because another lifecycle operation is in progress.",
					now
				);
			}

			context.State = MixinLifecycleState.Installing;
			context.StrictMode = options.StrictMode;
			context.HarmonyId = options.Harmony.Id;
			context.AssemblyName = options.Assembly.GetName().Name ?? options.Assembly.FullName ?? "UnknownAssembly";
			context.AssemblyKey = assemblyKey;
			context.Message = "Installing...";
			context.UpdatedAtUtc = now;
		}

		try
		{
			MixinScanResult scanResult = _scanner.Scan(options.Assembly);
			int scannerWarnings = CountSeverity(scanResult.Diagnostics, MixinScanDiagnosticSeverity.Warning);
			int scannerErrors = scanResult.ErrorCount;
			int installed = 0;
			int failed = 0;
			int skipped = 0;
			bool abortedByStrictMode = false;
			List<AppliedPatchHandle> appliedHandles = new();

			if (options.StrictMode && scannerErrors > 0)
			{
				MixinLifecycleCounters counters = new(
					Installed: 0,
					Failed: 0,
					Skipped: 0,
					ScannerErrors: scannerErrors,
					ScannerWarnings: scannerWarnings,
					UnpatchFailures: 0
				);

				string strictScanMessage =
					$"Install aborted because strict mode detected scanner errors. modId='{modId}', scannerErrors={scannerErrors}.";
				GD.PrintErr($"[ReForge.Mixins] {strictScanMessage}");
				UpdateInstallFinalState(modId, MixinLifecycleState.Failed, counters, strictScanMessage, appliedHandles, DateTimeOffset.UtcNow);

				return new MixinLifecycleInstallResult(
					modId,
					MixinLifecycleState.Failed,
					NoOp: false,
					AbortedByStrictMode: true,
					counters,
					strictScanMessage,
					DateTimeOffset.UtcNow
				);
			}

			List<MixinDescriptor> orderedDescriptors = new(scanResult.Descriptors);
			orderedDescriptors.Sort(static (a, b) => string.CompareOrdinal(a.DescriptorKey, b.DescriptorKey));
			for (int i = 0; i < orderedDescriptors.Count; i++)
			{
				MixinDescriptor descriptor = orderedDescriptors[i];
				MixinPatchBindResult bindResult = _binder.BindAndApply(descriptor, options.Harmony);
				installed += bindResult.Installed;
				failed += bindResult.Failed;
				skipped += bindResult.Skipped;
				abortedByStrictMode |= bindResult.AbortedByStrictMode;

				Dictionary<string, MethodInfo> injectionHandlerMap = BuildInjectionHandlerMap(descriptor);
				for (int j = 0; j < bindResult.Records.Count; j++)
				{
					MixinPatchApplyRecord record = bindResult.Records[j];
					if (!record.Success || record.PatchedTarget == null)
					{
						continue;
					}

					if (!injectionHandlerMap.TryGetValue(record.InjectionDescriptorKey, out MethodInfo? handlerMethod) || handlerMethod == null)
					{
						continue;
					}

					appliedHandles.Add(new AppliedPatchHandle(record.InjectionDescriptorKey, record.PatchedTarget, handlerMethod));
				}

				if (options.StrictMode && (bindResult.AbortedByStrictMode || bindResult.Failed > 0))
				{
					abortedByStrictMode = true;
					break;
				}
			}

			MixinLifecycleState finalState = (options.StrictMode && (abortedByStrictMode || failed > 0))
				? MixinLifecycleState.Failed
				: MixinLifecycleState.Active;
			MixinLifecycleCounters finalCounters = new(
				Installed: installed,
				Failed: failed,
				Skipped: skipped,
				ScannerErrors: scannerErrors,
				ScannerWarnings: scannerWarnings,
				UnpatchFailures: 0
			);

			string finalMessage =
				$"Install completed. modId='{modId}', installed={installed}, failed={failed}, skipped={skipped}, scannerErrors={scannerErrors}, strict={options.StrictMode}.";
			if (finalState == MixinLifecycleState.Failed)
			{
				GD.PrintErr($"[ReForge.Mixins] {finalMessage}");
			}
			else
			{
				GD.Print($"[ReForge.Mixins] {finalMessage}");
			}

			UpdateInstallFinalState(modId, finalState, finalCounters, finalMessage, appliedHandles, DateTimeOffset.UtcNow);
			return new MixinLifecycleInstallResult(
				modId,
				finalState,
				NoOp: false,
				AbortedByStrictMode: abortedByStrictMode,
				finalCounters,
				finalMessage,
				DateTimeOffset.UtcNow
			);
		}
		catch (Exception exception)
		{
			MixinLifecycleCounters counters = new(0, 1, 0, 0, 0, 0);
			string errorMessage = $"Install crashed but was isolated. modId='{modId}'. {exception}";
			GD.PrintErr($"[ReForge.Mixins] {errorMessage}");
			UpdateInstallFinalState(modId, MixinLifecycleState.Failed, counters, errorMessage, appliedHandles: null, DateTimeOffset.UtcNow);
			return new MixinLifecycleInstallResult(
				modId,
				MixinLifecycleState.Failed,
				NoOp: false,
				AbortedByStrictMode: true,
				counters,
				errorMessage,
				DateTimeOffset.UtcNow
			);
		}
	}

	public MixinLifecycleUnloadResult Unload(string modId)
	{
		ValidateRequiredKey(modId, nameof(modId));
		DateTimeOffset now = DateTimeOffset.UtcNow;

		ModContext? context;
		List<AppliedPatchHandle> handles;
		string harmonyId;
		lock (_syncRoot)
		{
			if (!_contexts.TryGetValue(modId, out context))
			{
				return new MixinLifecycleUnloadResult(
					modId,
					MixinLifecycleState.NotInstalled,
					NoOp: true,
					RemovedCount: 0,
					UnpatchFailures: 0,
					"Unload skipped because modId is not registered.",
					now
				);
			}

			if (context.State == MixinLifecycleState.Unloading || context.State == MixinLifecycleState.Installing)
			{
				return new MixinLifecycleUnloadResult(
					modId,
					context.State,
					NoOp: true,
					RemovedCount: 0,
					UnpatchFailures: 0,
					"Unload skipped because another lifecycle operation is in progress.",
					now
				);
			}

			if (context.AppliedPatches.Count == 0)
			{
				context.State = MixinLifecycleState.Unloaded;
				context.Message = "Unload completed with no applied patches.";
				context.UpdatedAtUtc = now;
				return new MixinLifecycleUnloadResult(
					modId,
					MixinLifecycleState.Unloaded,
					NoOp: true,
					RemovedCount: 0,
					UnpatchFailures: 0,
					context.Message,
					now
				);
			}

			context.State = MixinLifecycleState.Unloading;
			context.Message = "Unloading...";
			context.UpdatedAtUtc = now;
			handles = new List<AppliedPatchHandle>(context.AppliedPatches);
			harmonyId = context.HarmonyId;
		}

		int removedCount = 0;
		int unpatchFailures = 0;
		HashSet<string> removedKeys = new(StringComparer.Ordinal);
		HarmonyLib.Harmony? harmony = string.IsNullOrWhiteSpace(harmonyId) ? null : new HarmonyLib.Harmony(harmonyId);
		for (int i = 0; i < handles.Count; i++)
		{
			AppliedPatchHandle handle = handles[i];
			try
			{
				if (harmony == null)
				{
					throw new InvalidOperationException($"HarmonyId is empty for mod '{modId}'.");
				}

				harmony.Unpatch(handle.TargetMethod, handle.HandlerMethod);
				removedKeys.Add(handle.InjectionDescriptorKey);
				removedCount++;
			}
			catch (Exception exception)
			{
				unpatchFailures++;
				GD.PrintErr($"[ReForge.Mixins] Unpatch failed but was isolated. modId='{modId}', injectionKey='{handle.InjectionDescriptorKey}'. {exception}");
			}
		}

		if (removedKeys.Count > 0)
		{
			_binder.RemoveAppliedByInjectionKeys(new ReadOnlyCollection<string>(new List<string>(removedKeys)));
		}

		lock (_syncRoot)
		{
			if (_contexts.TryGetValue(modId, out ModContext? latest) && latest != null)
			{
				for (int i = latest.AppliedPatches.Count - 1; i >= 0; i--)
				{
					if (removedKeys.Contains(latest.AppliedPatches[i].InjectionDescriptorKey))
					{
						latest.AppliedPatches.RemoveAt(i);
					}
				}

				MixinLifecycleState endState = latest.AppliedPatches.Count == 0
					? MixinLifecycleState.Unloaded
					: MixinLifecycleState.Failed;
				latest.State = endState;
				latest.Counters = latest.Counters with { UnpatchFailures = unpatchFailures };
				latest.Message =
					$"Unload completed. modId='{modId}', removed={removedCount}, unpatchFailures={unpatchFailures}, remaining={latest.AppliedPatches.Count}.";
				latest.UpdatedAtUtc = DateTimeOffset.UtcNow;

				return new MixinLifecycleUnloadResult(
					modId,
					endState,
					NoOp: false,
					RemovedCount: removedCount,
					UnpatchFailures: unpatchFailures,
					latest.Message,
					latest.UpdatedAtUtc
				);
			}
		}

		return new MixinLifecycleUnloadResult(
			modId,
			MixinLifecycleState.Failed,
			NoOp: false,
			RemovedCount: removedCount,
			UnpatchFailures: unpatchFailures,
			$"Unload finished but mod context was missing. modId='{modId}'.",
			DateTimeOffset.UtcNow
		);
	}

	public MixinModLifecycleStatus? GetStatus(string modId)
	{
		ValidateRequiredKey(modId, nameof(modId));
		lock (_syncRoot)
		{
			if (!_contexts.TryGetValue(modId, out ModContext? context) || context == null)
			{
				return null;
			}

			return context.ToStatus();
		}
	}

	public MixinLifecycleSnapshot Snapshot()
	{
		Dictionary<string, MixinModLifecycleStatus> snapshot = new(StringComparer.Ordinal);
		lock (_syncRoot)
		{
			foreach (KeyValuePair<string, ModContext> pair in _contexts)
			{
				snapshot[pair.Key] = pair.Value.ToStatus();
			}
		}

		return new MixinLifecycleSnapshot(new ReadOnlyDictionary<string, MixinModLifecycleStatus>(snapshot));
	}

	public IReadOnlyList<MixinAppliedEntry> GetAppliedEntries()
	{
		return _binder.GetAppliedEntries();
	}

	private static Dictionary<string, MethodInfo> BuildInjectionHandlerMap(MixinDescriptor descriptor)
	{
		Dictionary<string, MethodInfo> map = new(StringComparer.Ordinal);
		for (int i = 0; i < descriptor.Injections.Count; i++)
		{
			InjectionDescriptor injection = descriptor.Injections[i];
			map[injection.DescriptorKey] = injection.HandlerMethod;
		}

		return map;
	}

	private static int CountSeverity(IReadOnlyList<MixinScanDiagnostic> diagnostics, MixinScanDiagnosticSeverity severity)
	{
		int count = 0;
		for (int i = 0; i < diagnostics.Count; i++)
		{
			if (diagnostics[i].Severity == severity)
			{
				count++;
			}
		}

		return count;
	}

	private void UpdateInstallFinalState(
		string modId,
		MixinLifecycleState state,
		MixinLifecycleCounters counters,
		string message,
		List<AppliedPatchHandle>? appliedHandles,
		DateTimeOffset timestamp)
	{
		lock (_syncRoot)
		{
			if (!_contexts.TryGetValue(modId, out ModContext? context) || context == null)
			{
				return;
			}

			context.State = state;
			context.Counters = counters;
			context.Message = message;
			context.UpdatedAtUtc = timestamp;
			if (appliedHandles != null)
			{
				context.AppliedPatches = appliedHandles;
			}
		}
	}

	private static string BuildAssemblyKey(Assembly assembly)
	{
		ArgumentNullException.ThrowIfNull(assembly);
		return string.Concat(assembly.FullName ?? assembly.GetName().Name ?? "Unknown", ":", assembly.ManifestModule.ModuleVersionId);
	}

	private static void ValidateRequiredKey(string value, string paramName)
	{
		ArgumentNullException.ThrowIfNull(value);
		if (string.IsNullOrWhiteSpace(value))
		{
			throw new ArgumentException("Value cannot be empty.", paramName);
		}
	}

	private sealed class ModContext
	{
		public ModContext(string modId)
		{
			ModId = modId;
			State = MixinLifecycleState.NotInstalled;
			Counters = new MixinLifecycleCounters(0, 0, 0, 0, 0, 0);
			Message = "Not installed.";
			HarmonyId = string.Empty;
			AssemblyName = string.Empty;
			AssemblyKey = string.Empty;
			UpdatedAtUtc = DateTimeOffset.UtcNow;
			AppliedPatches = new List<AppliedPatchHandle>();
		}

		public string ModId { get; }

		public MixinLifecycleState State { get; set; }

		public bool StrictMode { get; set; }

		public string HarmonyId { get; set; }

		public string AssemblyName { get; set; }

		public string AssemblyKey { get; set; }

		public MixinLifecycleCounters Counters { get; set; }

		public string Message { get; set; }

		public DateTimeOffset UpdatedAtUtc { get; set; }

		public List<AppliedPatchHandle> AppliedPatches { get; set; }

		public MixinModLifecycleStatus ToStatus()
		{
			return new MixinModLifecycleStatus(
				ModId,
				State,
				StrictMode,
				HarmonyId,
				AssemblyName,
				Counters,
				Message,
				UpdatedAtUtc
			);
		}
	}

	private sealed record AppliedPatchHandle(
		string InjectionDescriptorKey,
		MethodBase TargetMethod,
		MethodInfo HandlerMethod
	);
}
