#nullable enable

using System;
using System.Collections.Generic;

namespace ReForgeFramework.Api.Events;

/// <summary>
/// 事件轮子诊断 API。
/// </summary>
public interface IEventWheelDiagnosticsApi
{
	/// <summary>
	/// 记录一条结构化诊断事件。
	/// </summary>
	void Track(EventWheelDiagnosticEvent diagnosticEvent);

	/// <summary>
	/// 记录一条结构化诊断事件（便捷重载）。
	/// </summary>
	void Track(
		EventWheelStage stage,
		EventWheelSeverity severity,
		string eventId,
		string sourceModId,
		string message,
		string? exceptionSummary = null,
		IReadOnlyDictionary<string, string>? context = null);

	/// <summary>
	/// 按条件查询诊断事件。
	/// </summary>
	IReadOnlyList<EventWheelDiagnosticEvent> Query(EventWheelDiagnosticQuery? query = null);

	/// <summary>
	/// 获取关键阶段统计。
	/// </summary>
	EventWheelDiagnosticsSummary GetSummary(EventWheelDiagnosticQuery? query = null);

	/// <summary>
	/// 获取诊断快照（事件列表 + 汇总统计）。
	/// </summary>
	EventWheelDiagnosticsSnapshot BuildSnapshot(EventWheelDiagnosticQuery? query = null);

	/// <summary>
	/// 注册诊断监听器（同 busId 会覆盖）。
	/// </summary>
	bool RegisterListener(string busId, Action<EventWheelDiagnosticEvent> listener);

	/// <summary>
	/// 按 busId 注销诊断监听器。
	/// </summary>
	int UnregisterListener(string busId);
}
