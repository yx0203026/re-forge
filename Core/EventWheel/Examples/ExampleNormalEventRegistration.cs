#nullable enable

using System;
using System.Collections.Generic;
using Godot;
using ReForgeFramework.Api.Events;

namespace ReForgeFramework.EventWheel.Examples;

public static class ExampleNormalEventRegistration
{
	public const string DemoEventId = "REFORGE_EXAMPLE_NORMAL";
	public const string DemoSourceModId = "reforge.examples.eventwheel.normal";
	private const string DemoRuleId = "reforge.examples.eventwheel.normal.add_example_choice";
	private const string BaseOptionA = "REFORGE_EXAMPLE_NORMAL.pages.INITIAL.options.BASE_A";
	private const string BaseOptionB = "REFORGE_EXAMPLE_NORMAL.pages.INITIAL.options.BASE_B";
	private const string AddedOption = "REFORGE_EXAMPLE_NORMAL.pages.INITIAL.options.ADDED_BY_RULE";

	private static bool _configured;

	// 最小可运行路径：定义 -> 注册 -> 变更规则 -> 读取诊断。
	public static void Configure(bool enableDemo = true)
	{
		if (!enableDemo || _configured)
		{
			return;
		}

		_configured = true;

		EventRegistrationResult definitionResult = ReForge.EventWheel.RegisterDefinition(new DemoNormalDefinition());
		EventWheelResult mutationResult = ReForge.EventWheel.RegisterMutationRule(new DemoNormalMutationRule());

		GD.Print(
			$"[ReForge.EventWheel.Example.Normal] definition success={definitionResult.Success}, eventId='{definitionResult.EventId}', message='{definitionResult.Message}'.");
		GD.Print(
			$"[ReForge.EventWheel.Example.Normal] mutation success={mutationResult.Success}, code='{mutationResult.Code}', message='{mutationResult.Message}'.");

		PrintRegisterDiagnostics(limit: 8);
	}

	public static IReadOnlyList<EventWheelDiagnosticEvent> ReadRegisterDiagnostics(int limit = 20)
	{
		int normalizedLimit = limit <= 0 ? 20 : limit;
		return ReForge.EventWheel.QueryDiagnostics(
			new EventWheelDiagnosticQuery(
				Stage: EventWheelStage.Register,
				MinSeverity: EventWheelSeverity.Info,
				EventId: DemoEventId,
				SourceModId: DemoSourceModId,
				Limit: normalizedLimit));
	}

	private static void PrintRegisterDiagnostics(int limit)
	{
		IReadOnlyList<EventWheelDiagnosticEvent> diagnostics = ReadRegisterDiagnostics(limit);
		for (int i = 0; i < diagnostics.Count; i++)
		{
			EventWheelDiagnosticEvent item = diagnostics[i];
			GD.Print(
				$"[ReForge.EventWheel.Example.Normal] diag[{i}] stage={item.Stage}, severity={item.Severity}, message='{item.Message}'.");
		}
	}

	private sealed class DemoNormalDefinition : IEventDefinition
	{
		private static readonly IReadOnlyList<IEventOptionDefinition> InitialOptionsValue = new IEventOptionDefinition[]
		{
			new DemoEventOptionDefinition(BaseOptionA, BaseOptionA, BaseOptionA, order: 0),
			new DemoEventOptionDefinition(BaseOptionB, BaseOptionB, BaseOptionB, order: 1)
		};

		private static readonly IReadOnlyList<IEventMutationRule> MutationRulesValue = Array.Empty<IEventMutationRule>();
		private static readonly IReadOnlyDictionary<string, string> MetadataValue = new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["example"] = "normal",
			["purpose"] = "minimal-registration-path"
		};

		public string EventId => DemoEventId;
		public EventKind Kind => EventKind.Normal;
		public bool IsApplicable(MegaCrit.Sts2.Core.Models.EventModel? eventModel) => true;
		public string SourceModId => DemoSourceModId;
		public int Priority => 100;
		public IReadOnlyList<IEventOptionDefinition> InitialOptions => InitialOptionsValue;
		public IReadOnlyList<IEventMutationRule> MutationRules => MutationRulesValue;
		public IReadOnlyDictionary<string, string> Metadata => MetadataValue;
	}

	private sealed class DemoNormalMutationRule : IEventMutationRule
	{
		private static readonly IEventOptionDefinition AddedOptionDefinition =
			new DemoEventOptionDefinition(AddedOption, AddedOption, AddedOption, order: 99);

		public string RuleId => DemoRuleId;
		public string EventId => DemoEventId;
		public string SourceModId => DemoSourceModId;
		public EventMutationOperation Operation => EventMutationOperation.Add;
		public bool IsApplicable(MegaCrit.Sts2.Core.Models.EventModel? eventModel) => true;
		public string? TargetOptionKey => null;
		public IEventOptionDefinition? Option => AddedOptionDefinition;
		public int Order => 100;
		public bool StopOnFailure => false;
	}

	private sealed class DemoEventOptionDefinition : IEventOptionDefinition
	{
		public DemoEventOptionDefinition(
			string optionKey,
			string titleKey,
			string descriptionKey,
			int order,
			bool isLocked = false,
			bool isProceed = false,
			IReadOnlyList<string>? tagKeys = null)
		{
			OptionKey = optionKey;
			TitleKey = titleKey;
			DescriptionKey = descriptionKey;
			Order = order;
			IsLocked = isLocked;
			IsProceed = isProceed;
			TagKeys = tagKeys ?? Array.Empty<string>();
		}

		public string OptionKey { get; }
		public string? ActionKey => null;
		public string TitleKey { get; }
		public string DescriptionKey { get; }
		public int Order { get; }
		public bool IsLocked { get; }
		public bool IsProceed { get; }
		public IReadOnlyList<string> TagKeys { get; }
	}
}
