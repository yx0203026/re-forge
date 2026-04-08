#nullable enable

using System;

namespace ReForgeFramework.EventBus;

/// <summary>
/// 保存/读取生命周期事件 ID 定义。
/// </summary>
public static class SaveLoadLifecycleEventIds
{
	public const string SaveStarted = "reforge.save.started";
	public const string SaveCompleted = "reforge.save.completed";

	public const string LoadStarted = "reforge.load.started";
	public const string LoadCompleted = "reforge.load.completed";
}

/// <summary>
/// 保存开始事件。
/// </summary>
public readonly record struct SaveLifecycleStartedEvent(
	DateTimeOffset Timestamp,
	bool SaveProgress,
	bool IsMultiplayer,
	bool HasPreFinishedRoom
) : IEventArg;

/// <summary>
/// 保存结束事件。
/// </summary>
public readonly record struct SaveLifecycleCompletedEvent(
	DateTimeOffset Timestamp,
	bool SaveProgress,
	bool IsMultiplayer,
	bool HasPreFinishedRoom,
	bool Success,
	string? ErrorMessage,
	double DurationMs
) : IEventArg;

/// <summary>
/// 读取开始事件。
/// </summary>
public readonly record struct LoadLifecycleStartedEvent(
	DateTimeOffset Timestamp,
	bool IsMultiplayer
) : IEventArg;

/// <summary>
/// 读取结束事件。
/// </summary>
public readonly record struct LoadLifecycleCompletedEvent(
	DateTimeOffset Timestamp,
	bool IsMultiplayer,
	bool Success,
	string Status,
	bool HasSaveData,
	string? ErrorMessage,
	double DurationMs
) : IEventArg;
