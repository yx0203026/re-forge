#nullable enable

using System;

namespace ReForgeFramework.Mixins;

/// <summary>
/// 可复制的 Mixin 使用片段，避免额外 markdown 文档。
/// </summary>
public static class MixinReadmeSnippets
{
	public const string MainInitializerSnippet =
		"var result = ReForge.Mixins.Register(typeof(ReForge).Assembly, \"reforge.mod\", harmony, strictMode: true);\n" +
		"GD.Print($\"Mixins: installed={result.Summary.Installed}, failed={result.Summary.Failed}, skipped={result.Summary.Skipped}\");";

	public const string DiagnosticsSnippet =
		"var snapshot = ReForge.Mixins.GetDiagnosticsSnapshot();\n" +
		"var json = ReForge.Mixins.GetDiagnosticsJson(indented: true);\n" +
		"GD.Print(json);";

	public const string UnloadSnippet =
		"var unload = ReForge.Mixins.UnregisterAll(\"reforge.mod\");\n" +
		"GD.Print($\"Unload removed={unload.RemovedInstalledCount}, failed={unload.RemovedFailedCount}\");";

	public static string[] GetAll()
	{
		return
		[
			MainInitializerSnippet,
			DiagnosticsSnippet,
			UnloadSnippet,
		];
	}
}
