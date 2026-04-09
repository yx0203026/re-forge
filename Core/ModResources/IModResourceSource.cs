#nullable enable

using ReForgeFramework.ModLoading;

namespace ReForgeFramework.ModResources;

public interface IModResourceSource
{
	string Name { get; }

	bool CanHandle(ReForgeModContext mod);

	bool Prepare(ReForgeModContext mod, ReForgeModDiagnostics diagnostics);

	bool Exists(ReForgeModContext mod, string resourcePath);

	byte[]? ReadAllBytes(ReForgeModContext mod, string resourcePath);
}
