#nullable enable

namespace ReForgeFramework.Networking;

/// <summary>
/// 本地模型目录哈希快照。
/// </summary>
public readonly record struct ReForgeModelCatalogHashSnapshot(
	uint Hash,
	int CategoryCount,
	int EntryCount
);
