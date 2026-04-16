# EventWheel 开发文档（快速上手）

本文面向 ReForge 与第三方模组开发者，帮助你在 5~10 分钟内上手“事件轮”能力：
- 定义事件初始选项
- 通过规则增删改锁事件选项
- 在需要时挂接自定义选项行为
- 处理联机同步与诊断排错

## 1. EventWheel 是什么

EventWheel 是一个事件选项变更流水线，核心分为 3 个阶段：

1. Register：注册事件定义与规则
2. Plan：根据定义 + 规则，生成最终选项计划
3. Execute：把计划应用到运行时 EventModel

运行时入口在 `ReForge.EventWheel`，会在 ReForge 初始化阶段自动启动。

## 2. 运行机制（你需要知道的关键点）

### 2.1 自动发现与注册

EventWheel 初始化时会扫描：
- ReForge 核心程序集
- 已加载模组程序集

凡是“可实例化 + 无参构造 + 实现了接口”的类型，会被自动注册：
- `IEventDefinition`
- `IEventMutationRule`

你也可以手动注册：
- `ReForge.EventWheel.RegisterDefinition(...)`
- `ReForge.EventWheel.RegisterMutationRule(...)`

### 2.2 选项变更是如何执行的

对某个事件（`EventId`）命中定义后，流程如下：

1. 从 `InitialOptions` 构建初始集合
2. 读取该事件的所有规则并按稳定顺序排序
3. 依次执行规则操作：
   - `Add`
   - `Replace`
   - `InsertBefore`
   - `InsertAfter`
   - `Lock`
   - `Remove`
4. 应用到事件模型当前选项
5. 如失败，尽量回滚到原始快照

### 2.3 事件类型

- `EventKind.Normal`：普通事件
- `EventKind.Ancient`：远古事件（如 Neow）

## 3. 最小可运行示例

下面是一个普通事件最小示例：

```csharp
#nullable enable

using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Models;
using ReForgeFramework.Api.Events;

namespace YourMod.EventWheel;

public static class DemoEventWheelBootstrap
{
    public const string EventId = "YOUR_EVENT_ID";
    public const string SourceModId = "yourmod.eventwheel";

    // 你可以在 ModMain.Initialize() 调用该方法，进行手动注册。
    public static void Configure()
    {
        _ = ReForge.EventWheel.RegisterDefinition(new DemoDefinition());
        _ = ReForge.EventWheel.RegisterMutationRule(new DemoAddRule());
    }

    private sealed class DemoDefinition : IEventDefinition
    {
        private static readonly IReadOnlyList<IEventOptionDefinition> InitialOptionsValue = new IEventOptionDefinition[]
        {
            new DemoOption("YOUR_EVENT.pages.INITIAL.options.A", order: 0),
            new DemoOption("YOUR_EVENT.pages.INITIAL.options.B", order: 1)
        };

        public string EventId => DemoEventWheelBootstrap.EventId;
        public EventKind Kind => EventKind.Normal;
        public bool IsApplicable(EventModel? eventModel) => true;
        public string SourceModId => DemoEventWheelBootstrap.SourceModId;
        public int Priority => 100;
        public IReadOnlyList<IEventOptionDefinition> InitialOptions => InitialOptionsValue;
        public IReadOnlyList<IEventMutationRule> MutationRules => Array.Empty<IEventMutationRule>();
        public IReadOnlyDictionary<string, string> Metadata => new Dictionary<string, string>
        {
            ["purpose"] = "demo"
        };
    }

    private sealed class DemoAddRule : IEventMutationRule
    {
        public string RuleId => "yourmod.eventwheel.demo.add";
        public string EventId => DemoEventWheelBootstrap.EventId;
        public string SourceModId => DemoEventWheelBootstrap.SourceModId;
        public EventMutationOperation Operation => EventMutationOperation.Add;
        public bool IsApplicable(EventModel? eventModel) => true;
        public string? TargetOptionKey => null;
        public IEventOptionDefinition? Option => new DemoOption("YOUR_EVENT.pages.INITIAL.options.C", order: 99);
        public int Order => 100;
        public bool StopOnFailure => false;
    }

    private sealed class DemoOption : IEventOptionDefinition
    {
        public DemoOption(string key, int order)
        {
            OptionKey = key;
            ActionKey = null;
            TitleKey = key;
            DescriptionKey = key;
            Order = order;
            IsLocked = false;
            IsProceed = false;
            TagKeys = Array.Empty<string>();
        }

        public string OptionKey { get; }
        public string? ActionKey { get; }
        public string TitleKey { get; }
        public string DescriptionKey { get; }
        public int Order { get; }
        public bool IsLocked { get; }
        public bool IsProceed { get; }
        public IReadOnlyList<string> TagKeys { get; }
    }
}
```

## 4. 自定义选项行为（核心）

默认情况下，EventWheel 会尝试复用已有选项；无法复用时会创建占位选项（通常无点击行为）。

如果你要让新选项“可点击并执行逻辑”，请使用：

1. 在 `IEventOptionDefinition.ActionKey` 填入动作键
2. 注册工厂：`ReForge.EventWheel.RegisterOptionFactory(actionKey, factory)`

示例：

```csharp
using MegaCrit.Sts2.Core.Localization;

// 注册一次即可，建议在模组初始化时执行。
_ = ReForge.EventWheel.RegisterOptionFactory(
    actionKey: "yourmod.action.give_relic",
    factory: (eventModel, definition) =>
    {
        // 返回 EventOption，内部可绑定奖励逻辑。
        // 你也可以使用 EventOption.FromRelic(...) 这种官方友好路径。
        return new EventOption(
            eventModel,
            onChosen: null,
            title: new LocString("events", definition.TitleKey),
            description: new LocString("events", definition.DescriptionKey),
            textKey: definition.OptionKey,
            hoverTips: null!);
    });
```

建议：
- `ActionKey` 保持全局唯一（可用 `modId.feature.action` 命名）
- `OptionKey` 与本地化 key 保持一致，便于定位
- 需要替换官方已有选项时，`OptionKey` 要对齐该选项的 `TextKey`

## 5. UI 分页（选项很多时）

EventWheel 提供了事件选项分页适配层：

- 普通事件默认每页 4 项
- 远古事件默认每页 4 项

可通过代码配置：

```csharp
ReForgeFramework.EventWheel.UI.EventOptionUiAdapter.ConfigurePaging(
    normalOptionsPerPage: 4,
    ancientOptionsPerPage: 4);
```

也可通过配置覆盖：
- ProjectSettings:
  - `reforge/eventwheel/ui/options_per_page`
  - `reforge/eventwheel/ui/ancient_options_per_page`
- 环境变量：
  - `REFORGE_EVENTWHEEL_OPTIONS_PER_PAGE`
  - `REFORGE_EVENTWHEEL_ANCIENT_OPTIONS_PER_PAGE`

## 6. 联机安全注意事项（非常重要）

联机模式下，选项会经过网络归一化：
- 稳定排序
- 去重
- 限制最大选项数（默认 16）

这是为了对齐 STS2 选项索引传输约束（4-bit）。

开发建议：
1. 保证跨端规则结果完全一致（不要依赖本地随机/本地状态差异）
2. 事件可选项总数控制在 16 以内
3. 避免“某端跳过规则、某端不跳过”导致索引错位

## 7. 诊断与排错

你可以查询 EventWheel 诊断：

```csharp
var diagnostics = ReForge.EventWheel.QueryDiagnostics(
    new EventWheelDiagnosticQuery(
        Stage: EventWheelStage.Execute,
        MinSeverity: EventWheelSeverity.Warning,
        EventId: "YOUR_EVENT_ID",
        SourceModId: "yourmod.eventwheel",
        Limit: 50));
```

推荐先看这些阶段：
- `Register`：定义/规则是否注册成功
- `Plan`：规则是否命中、是否有目标缺失
- `Execute`：是否应用成功、是否回滚
- `Layout`：UI 分页布局是否正常

## 8. 与 RewardRefresh Neow 实践对齐

Neow 属于 `EventKind.Ancient`，常见做法：

1. 用定义提供完整候选选项集
2. 用规则做动态锁定（`Lock`）
3. 用 `RegisterOptionFactory` 为每个选项绑定真实奖励逻辑
4. 对联机保持稳定顺序与同构结果

这套方式既能扩展选项数量，又能保持事件行为可控。

## 9. 开发清单（Checklist）

- 已实现 `IEventDefinition`，并确认 `EventId/Kind/SourceModId` 正确
- 已实现必要的 `IEventMutationRule`
- 新增可点击选项已注册 `ActionKey -> OptionFactory`
- 联机下选项数 <= 16
- 使用 `QueryDiagnostics` 验证 Register/Plan/Execute 阶段

---

如果你要在现有事件上做“尽量不侵入”的改造，推荐策略是：
- 优先使用 `InsertBefore/InsertAfter/Lock`
- 少用 `Replace`（除非你确定要完全接管某个选项）
- 在失败路径上保持可回滚（避免空选项集）
