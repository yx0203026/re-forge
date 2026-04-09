# ReForge Mixin 系统文档

## 目录
1. [核心概念](#核心概念)
2. [快速开始](#快速开始)
3. [核心属性详解](#核心属性详解)
4. [实际示例](#实际示例)
5. [高级特性](#高级特性)
6. [最佳实践](#最佳实践)
7. [故障排除](#故障排除)

---

## 核心概念

### Mixin 是什么？

**Mixin** 是一种将功能性代码注入到现有类型中的设计模式。在 ReForge 中，Mixin 系统基于 **Harmony** 库（一个用于 .NET 方法补丁的框架），提供了结构化、类型安全的方式来修改 STS2 游戏代码的运行时行为。

### 核心原理

- **Mixin 类**：一个标记了 `[ReForge.Mixin(...)]` 的静态类，包含静态方法和字段
- **目标类型**：想要修改行为的原始游戏类（如 `CombatState`、`CardReward` 等）
- **注入方法**：通过属性（如 `[ReForge.Prefix]`、`[ReForge.Postfix]` 等）声明如何在目标方法前后执行代码
- **生命周期管理**：Mixin 系统在模组启动时自动注册、扫描并安装所有 Mixin

### 工作流程

```
Mixin 类定义 
    ↓
类型扫描与验证 
    ↓
Harmony 补丁绑定 
    ↓
运行时执行拦截
```

---

## 快速开始

### 1. 创建你的第一个 Mixin

最简单的 Mixin 包含以下部分：

```csharp
#nullable enable

using MegaCrit.Sts2.Core.Combat;  // 目标类型的命名空间
using MegaCrit.Sts2.Core.Entities.Creatures;

namespace YourModNamespace.Mixins;

/// <summary>
/// 在怪物创建后增加其生命值。
/// </summary>
[global::ReForge.Mixin(typeof(CombatState), Id = "yourmod.increase-monster-hp")]
public static class MonsterHpBoostMixin
{
    private const int HpBonus = 10;

    // 在 CreateCreature 方法执行后调用
    [global::ReForge.Postfix("CreateCreature")]
    private static void CreateCreaturePostfix(CombatSide side, Creature __result)
    {
        if (__result == null || side != CombatSide.Enemy)
            return;

        __result.SetMaxHpInternal(__result.MaxHp + HpBonus);
        __result.SetCurrentHpInternal(__result.CurrentHp + HpBonus);
    }
}
```

### 2. 注册 Mixin

在你的模组初始化代码中注册 Mixin：

```csharp
using HarmonyLib;

[ModInitializer]
public static void Initialize()
{
    var harmony = new Harmony("yourmod.unique-id");
    
    // 注册程序集中所有 Mixin
    var result = ReForge.Mixins.Register(
        typeof(ReForge).Assembly,  // 或你的模组程序集
        "yourmod.mod-id",
        harmony,
        strictMode: true  // true 表示失败时中止，false 表示忽略失败
    );
    
    GD.Print(
        $"Mixins registered: installed={result.Summary.Installed}, " +
        $"failed={result.Summary.Failed}, skipped={result.Summary.Skipped}"
    );
}
```

---

## 核心属性详解

### [Mixin(...)]

声明一个类为 Mixin 类，指定其目标类型。

```csharp
[ReForge.Mixin(targetType: typeof(CombatState))]
[ReForge.Mixin(targetType: typeof(CardReward))]  // 支持多个目标
public static class MyMixin { }
```

**参数：**
| 参数 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `targetType` | `Type` | ✓ | 目标类型  |
| `Id` | `string` | ✗ | 唯一标识符，用于诊断和查询 |
| `Priority` | `int` | ✗ | 优先级（值越大越后执行），默认 400 |
| `StrictMode` | `bool` | ✗ | 失败时是否抛出异常，默认 true |

**示例：**
```csharp
[ReForge.Mixin(
    typeof(CardReward),
    Id = "reforge.card-reward-boost",
    Priority = 200,  // 优先级更高（更早执行）
    StrictMode = false  // 允许部分失败
)]
public static class CardRewardMixin { }
```

### [Prefix(...)] / [ReForge.PrefixAttribute]

在目标方法**执行之前**插入代码。

```csharp
[ReForge.Prefix("CreateCreature")]
private static void CreateCreaturePrefix(CombatSide side)
{
    GD.Print($"Creating creature on side: {side}");
}
```

**何时使用：**
- 方法执行前的验证或初始化
- 参数的修改或检查
- 执行前的日志记录

**返回值处理：**
- 返回 `true`：继续执行原始方法
- 返回 `false`：跳过原始方法（需返回类型为 `bool`）

```csharp
[ReForge.Prefix("TakeDamage")]
private static bool TakeDamagePrefix(Creature __instance, int damage)
{
    if (damage < 0)
    {
        GD.PrintErr("Invalid damage value, skipping method!");
        return false;  // 跳过原始方法
    }
    return true;  // 继续执行原始方法
}
```

### [Postfix(...)] / [ReForge.PostfixAttribute]

在目标方法**执行之后**插入代码。

```csharp
[ReForge.Postfix("CreateCreature")]
private static void CreateCreaturePostfix(Creature __result)
{
    GD.Print($"Created creature with {__result?.MaxHp} HP");
}
```

**何时使用：**
- 结果的修改或增强
- 方法执行后的清理或后处理
- 触发外部事件或副作用

**特殊参数 `__result`：**
包含原始方法的返回值（仅在返回值非 void 时传入）。

### [Finalizer(...)] / [ReForge.FinalizerAttribute]

在目标方法执行**完成后**插入代码，即使发生异常也会执行。

```csharp
[ReForge.Finalizer("TakeDamage")]
private static void TakeDamageFinalizer(Creature __instance, Exception? __exception)
{
    if (__exception != null)
    {
        GD.PrintErr($"TakeDamage raised exception: {__exception.Message}");
    }
}
```

**何时使用：**
- 资源清理
- 异常处理
- 日志记录（确保一定会执行）

### [Shadow(...)]

将 Mixin 类的字段绑定到目标类型的私有字段。

```csharp
[ReForge.Shadow(targetName: "_cards", aliases: new[] { "cards", "_cardsList" })]
private static FieldInfo shadow_cards = null!;

[ReForge.Postfix("Populate")]
private static void PopulatePostfix(CardReward __instance)
{
    // 通过 shadow_cards 访问目标类的私有字段
    var cards = shadow_cards.GetValue(__instance) as List<CardCreationResult>;
    if (cards != null)
    {
        GD.Print($"Found {cards.Count} cards");
    }
}
```

**参数：**
| 参数 | 类型 | 说明 |
|------|------|------|
| `targetName` | `string` | 目标字段名称（如 `"_cards"`） |
| `aliases` | `string[]` | 候选别名，用于兼容不同版本 |
| `Optional` | `bool` | 为 true 时字段缺失不触发错误 |

---

## 实际示例

### 示例 1：增加所有敌方怪物的生命值

**文件：** `MonsterHpPlusFiveMixin.cs`

```csharp
#nullable enable

using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;

namespace ReForgeFramework.Mixins.Examples;

/// <summary>
/// 在怪物创建流程结束后，统一为敌方怪物增加 5 点生命值。
/// </summary>
[global::ReForge.Mixin(
    typeof(CombatState),
    Id = "reforge.monster-hp-plus-five",
    Priority = 400
)]
public static class MonsterHpPlusFiveMixin
{
    private const int BonusHp = 5;

    /// <summary>
    /// Postfix 在 CreateCreature 返回前执行，修改新创建的生物。
    /// </summary>
    [global::ReForge.Postfix("CreateCreature")]
    private static void CreateCreaturePostfix(CombatSide side, Creature __result)
    {
        // 仅修改敌方怪物
        if (__result == null || side != CombatSide.Enemy)
            return;

        // 先修改上限，再补充当前 HP，确保表现为"总 HP +5"
        __result.SetMaxHpInternal(__result.MaxHp + BonusHp);
        __result.SetCurrentHpInternal(__result.CurrentHp + BonusHp);
    }
}
```

**工作原理：**
1. `CombatState.CreateCreature()` 创建怪物
2. Mixin 的 Postfix 立即执行
3. 如果是敌方怪物，增加其最大生命值和当前生命值
4. 结果：所有敏怪物多了 5 HP

### 示例 2：使用 Shadow 访问私有字段

**文件：** `CardRewardCountMixin.cs`

```csharp
#nullable enable

using System.Collections.Generic;
using System.Reflection;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Rewards;

namespace ReForgeFramework.Mixins.Examples;

/// <summary>
/// 在卡牌奖励补全后，确保候选卡牌数至少为 3。
/// 演示了 Shadow 用于访问私有字段的用法。
/// </summary>
[global::ReForge.Mixin(
    typeof(CardReward),
    Id = "reforge.card-reward-count",
    Priority = 300
)]
public static class CardRewardCountMixin
{
    private const int TargetCount = 3;

    /// <summary>
    /// Shadow 绑定：映射目标类的私有字段 _cards。
    /// aliases 用作备用名称，支持多版本兼容。
    /// </summary>
    [global::ReForge.Shadow(
        targetName: "_cards",
        aliases: new[] { "cards", "_cardOptions" }
    )]
    private static FieldInfo shadow_cards = null!;

    /// <summary>
    /// 在 Populate 方法执行后修改卡牌列表。
    /// </summary>
    [global::ReForge.Postfix("Populate")]
    private static void PopulatePostfix(CardReward __instance)
    {
        // 通过反射获取私有字段的值
        List<CardCreationResult>? cards = shadow_cards.GetValue(__instance) as List<CardCreationResult>;
        if (cards == null || cards.Count == 0)
            return;

        // 补全到 3 张卡
        while (cards.Count < TargetCount)
        {
            cards.Add(new CardCreationResult(cards[0].Card));
        }
    }
}
```

**关键点：**
- `shadow_cards` 的类型为 `FieldInfo`，它在运行时指向目标类的 `_cards` 字段
- 使用 `GetValue()` 获取字段值
- `aliases` 允许在不同版本间自动适配

### 示例 3：修改方法参数

```csharp
#nullable enable

using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;

namespace YourNamespace.Mixins;

/// <summary>
/// 在伤害计算前对伤害值进行修正。
/// </summary>
[global::ReForge.Mixin(typeof(Creature), Id = "yourmod.damage-modifier")]
public static class DamageModifierMixin
{
    private const float DamageMultiplier = 1.2f;  // 增加 20% 伤害

    [global::ReForge.Prefix("TakeDamage")]
    private static void TakeDamagePrefix(ref int damage)
    {
        // ref 参数允许修改传入的值
        damage = (int)(damage * DamageMultiplier);
        GD.Print($"Damage modified to: {damage}");
    }
}
```

**注意：** 使用 `ref` 参数可以修改原始方法的参数值。

### 示例 4：条件性跳过方法执行

```csharp
#nullable enable

using MegaCrit.Sts2.Core.Combat;

namespace YourNamespace.Mixins;

/// <summary>
/// 在特定条件下跳过某些操作。
/// </summary>
[global::ReForge.Mixin(typeof(CombatState), Id = "yourmod.skip-condition")]
public static class SkipConditionMixin
{
    [global::ReForge.Prefix("EndTurn")]
    private static bool EndTurnPrefix(CombatState __instance)
    {
        // 如果满足某条件，跳过原始方法
        if (__instance.IsTesting)
        {
            GD.Print("Skipping EndTurn in test mode");
            return false;  // false 表示跳过原始方法
        }
        
        return true;  // true 表示继续执行
    }
}
```

---

## 高级特性

### 多目标 Mixin

一个 Mixin 类可以修改多个目标类型：

```csharp
[global::ReForge.Mixin(typeof(Creature), Id = "multi.damage")]
[global::ReForge.Mixin(typeof(CardReward), Id = "multi.reward")]
public static class MultiTargetMixin
{
    [global::ReForge.Postfix("TakeDamage")]
    private static void CreatureTakeDamagePostfix(Creature __instance) { }

    [global::ReForge.Postfix("Populate")]
    private static void CardRewardPopulatePostfix(CardReward __instance) { }
}
```

### 诊断与监控

查询 Mixin 安装状态：

```csharp
// 获取诊断快照
var snapshot = ReForge.Mixins.GetDiagnosticsSnapshot();

// 获取 JSON 格式诊断信息
var json = ReForge.Mixins.GetDiagnosticsJson(indented: true);
GD.Print(json);

// 输出内容示例：
// {
//   "modId": "reforge.mod",
//   "installedCount": 15,
//   "failedCount": 0,
//   "skippedCount": 2,
//   "mixins": [
//     {
//       "id": "reforge.monster-hp-plus-five",
//       "targetType": "MegaCrit.Sts2.Core.Combat.CombatState",
//       "state": "Installed",
//       "message": ""
//     },
//     ...
//   ]
// }
```

### 卸载 Mixin

```csharp
// 在模组卸载时
var result = ReForge.Mixins.UnregisterAll("yourmod.mod-id");
GD.Print(
    $"Unloaded: removed={result.RemovedInstalledCount}, " +
    $"failures={result.RemovedFailedCount}"
);
```

### 优先级控制

```csharp
[global::ReForge.Mixin(typeof(Combat), Priority = 200)]  // 更早执行
public static class HighPriorityMixin { }

[global::ReForge.Mixin(typeof(Combat), Priority = 600)]  // 更晚执行
public static class LowPriorityMixin { }
```

- 值越小，执行越早
- 默认值：400
- 通常范围：0-1000

---

## 最佳实践

### ✓ 做这些事

1. **使用清晰的命名约定**
   ```csharp
   [ReForge.Mixin(typeof(CombatState), Id = "mymod.combat-boost")]
   public static class CombatBoostMixin { }
   ```

2. **为 Mixin 类添加 XML 文档注释**
   ```csharp
   /// <summary>
   /// 为所有生物增加 10% 最大生命值。
   /// 优先级 300 确保在其他生命值修改器之后执行。
   /// </summary>
   [global::ReForge.Mixin(...)]
   public static class MaxHpBoostMixin { }
   ```

3. **验证参数和返回值**
   ```csharp
   [global::ReForge.Postfix("CreateCreature")]
   private static void CreateCreaturePostfix(Creature __result)
   {
       if (__result == null)  // 检查 null
           return;
       
       // 执行修改
   }
   ```

4. **使用 `const` 定义配置值**
   ```csharp
   private const int HealthBonus = 10;
   private const float DamageMultiplier = 1.2f;
   ```

5. **在多版本支持中使用 Shadow 别名**
   ```csharp
   [global::ReForge.Shadow(
       targetName: "_cards",
       aliases: new[] { "cards", "_cardOptions", "selectedCards" }
   )]
   private static FieldInfo shadow_cards = null!;
   ```

### ✗ 避免这些事

1. **不要在 Mixin 中使用非静态方法**
   ```csharp
   // ❌ 错误
   [global::ReForge.Postfix("Method")]
   private void InstanceMethod() { }  // 必须是 static

   // ✓ 正确
   [global::ReForge.Postfix("Method")]
   private static void StaticMethod() { }
   ```

2. **不要创建 Mixin 类的实例**
   ```csharp
   // ❌ 错误
   var mixin = new MonsterHpPlusFiveMixin();

   // ✓ 正确
   // Mixin 类只用于类型标记，运行时由框架自动处理
   ```

3. **不要在生产环境中频繁启用严格模式调试**
   ```csharp
   // 开发时可用 strictMode: true 快速发现问题
   // 发布时建议 strictMode: false 以提高容错性
   var result = ReForge.Mixins.Register(..., strictMode: false);
   ```

4. **不要忘记处理 null 返回值**
   ```csharp
   // ❌ 不安全
   __result.SetMaxHpInternal(99);  // 如果 __result == null 会崩溃

   // ✓ 安全
   if (__result != null)
       __result.SetMaxHpInternal(99);
   ```

5. **不要创建循环依赖**
   ```csharp
   // ❌  避免
   Mixin A 修改 MethodX
   Mixin B 的 MethodX Postfix 又触发 Mixin A 的逻辑
   // 可能导致无限循环
   ```

### 测试建议

```csharp
#if DEBUG
[GlobalDefine] public const bool MixinDebugEnabled = true;
#endif

[global::ReForge.Mixin(typeof(CombatState), Id = "testmod.debug")]
public static class DebugMixin
{
    [global::ReForge.Postfix("CreateCreature")]
    private static void DebugCreateCreaturePostfix(Creature __result)
    {
        #if DEBUG
        if (MixinDebugEnabled)
            GD.Print($"[DEBUG] Created creature: {__result?.Name} HP={__result?.MaxHp}");
        #endif
    }
}
```

---

## 故障排除

### 问题 1: Mixin 没有被安装

**症状：**
- 安装日志显示 `installed=0`
- Mixin 代码没有被执行

**检查清单：**

1. 确保 Mixin 类标记了 `[ReForge.Mixin(...)]`
   ```csharp
   [global::ReForge.Mixin(typeof(TargetType))]  // ✓ 必需
   public static class MyMixin { }
   ```

2. 源类型是否存在且可访问
   ```csharp
   using MegaCrit.Sts2.Core.Combat;  // ✓ 确保命名空间正确
   [global::ReForge.Mixin(typeof(CombatState))]
   ```

3. 检查注入方法名称是否正确
   ```csharp
   [global::ReForge.Postfix("CreateCreature")]  // ✓ 方法名必须精确匹配
   ```

4. 诊断日志
   ```csharp
   var json = ReForge.Mixins.GetDiagnosticsJson(indented: true);
   GD.Print(json);  // 查看详细状态
   ```

### 问题 2: Mixin 导致游戏崩溃

**症状：**
- 应用 Mixin 后游戏立即崩溃
- 日志显示空引用异常

**常见原因：**

1. 访问 null 值
   ```csharp
   // ❌ 不安全
   [global::ReForge.Postfix("Method")]
   private static void BadPostfix(MyClass __result)
   {
       __result.Property = 123;  // 如果 __result 为 null，会崩溃
   }

   // ✓ 安全
   [global::ReForge.Postfix("Method")]
   private static void SafePostfix(MyClass __result)
   {
       if (__result != null)
           __result.Property = 123;
   }
   ```

2. Shadow 字段绑定失败
   ```csharp
   // 确保 Optional = true
   [global::ReForge.Shadow(targetName: "_cards", aliases: new[] { "cards" })]
   private static FieldInfo shadow_cards = null!;  // 运行时会被正确初始化

   // 使用时要检查
   if (shadow_cards != null)
   {
       var value = shadow_cards.GetValue(instance);
   }
   ```

3. 参数匹配错误
   ```csharp
   // Harmony 根据参数类型和顺序匹配方法
   // 确保你的参数列表与目标方法相同
   
   // 目标方法签名：
   // public void SetMaxHpInternal(int value) { ... }
   
   // ✓ 正确的 Prefix
   [global::ReForge.Prefix("SetMaxHpInternal")]
   private static void Prefix(ref int value) { }
   
   // ❌ 错误的 Prefix（参数类型不匹配）
   [global::ReForge.Prefix("SetMaxHpInternal")]
   private static void Prefix(string value) { }  // string 而不是 int
   ```

### 问题 3: Shadow 字段绑定失败

**调试步骤：**

```csharp
[global::ReForge.Shadow(targetName: "_cards", aliases: new[] { "cards", "_cardList" })]
private static FieldInfo shadow_cards = null!;

[global::ReForge.Postfix("Populate")]
private static void PopulatePostfix(CardReward __instance)
{
    // 检查 shadow_cards 是否被正确初始化
    if (shadow_cards == null)
    {
        GD.PrintErr("[ERROR] shadow_cards field not initialized");
        return;
    }

    try
    {
        var cards = shadow_cards.GetValue(__instance) as List<CardCreationResult>;
        // 处理 cards
    }
    catch (Exception ex)
    {
        GD.PrintErr($"[ERROR] Shadow field access failed: {ex.Message}");
    }
}
```

### 问题 4: 多个 Mixin 冲突

**表现：**
- 不同 Mixin 的效果互相覆盖
- 执行顺序不符合预期

**解决方案：**

1. 使用优先级控制执行顺序
   ```csharp
   // Mixin A：更早执行（初始化）
   [global::ReForge.Mixin(typeof(Combat), Priority = 200)]
   public static class InitialMixin { }

   // Mixin B：标准优先级
   [global::ReForge.Mixin(typeof(Combat), Priority = 400)]
   public static class StandardMixin { }

   // Mixin C：稍后执行（清理）
   [global::ReForge.Mixin(typeof(Combat), Priority = 600)]
   public static class CleanupMixin { }
   ```

2. 避免在同一方法注入多个冲突的 Prefix/Postfix
   ```csharp
   // ❌ 可能冲突
   // Mixin A 将 HP 设置为 100
   // Mixin B 将 HP 设置为 50
   // 结果取决于执行顺序

   // ✓ 更好的做法
   // Mixin A 和 B 协商共同的修改策略
   // 或使用单一 Mixin 聚合所有逻辑
   ```

### 问题 5: 诊断信息不清楚

**获取详细诊断：**

```csharp
// 1. 获取注册结果
var result = ReForge.Mixins.Register(assembly, "mymod", harmony, strictMode: true);
GD.Print($"Result: installed={result.Summary.Installed}, failed={result.Summary.Failed}");

// 2. 获取 JSON 诊断
var json = ReForge.Mixins.GetDiagnosticsJson(indented: true);
GD.Print(json);

// 3. 输出包含状态和错误消息：
// {
//   "mixins": [
//     {
//       "id": "mixin-id",
//       "state": "Failed",  // 或 "Installed", "Skipped"
//       "message": "Target method not found: CreateCreature(...)"
//     }
//   ]
// }

// 4. 根据返回的状态和消息诊断问题
```

---

## 总结

ReForge Mixin 系统提供了一种**声明式、类型安全、生命周期管理完善**的方式来定制 STS2 的行为。通过理解核心概念和最佳实践，你可以编写高效、可维护的 Mixin 代码。
