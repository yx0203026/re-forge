#nullable enable

using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Runs;

namespace ReForgeFramework.Api.Events;

/// <summary>
/// 战斗及时事件阶段。
/// </summary>
public enum CombatTimelyEventPhase
{
	Enter = 0,
	Trigger = 1,
	Leave = 2,
	Network = 3
}

/// <summary>
/// 战斗及时事件上下文。
/// </summary>
public readonly record struct CombatTimelyEventContext(
	IRunState? RunState,
	CombatState? CombatState,
	long UtcNowMs,
	bool IsAuthority,
	bool IsMultiplayer,
	CombatTimelyEventPhase Phase
);

/// <summary>
/// 战斗及时事件注册结果。
/// </summary>
public readonly record struct CombatTimelyEventRegistrationResult(
	bool Success,
	bool Replaced,
	string EventId,
	string Message
);
