# ReForge

ReForge 是一个面向 Slay the Spire 2 的 C# 模组基础库，目标是提供可复用、可扩展的开发能力，减少每个模组重复造轮子的成本。

当前项目基于 Godot C# 与 Harmony，内置了事件总线、UI 注入能力、Mixin 注解补丁系统等核心模块。

## 项目特点

- 统一入口初始化
  - 在模组初始化阶段完成 Harmony、Mixin、EventBus、UI 的统一装配。
- Mixin 注解补丁系统
  - 通过注解声明补丁目标，支持 Prefix、Postfix、Finalizer 与扩展注入语义。
  - 提供注册状态、生命周期和诊断快照。
- 事件总线
  - 支持属性扫描注册与手动注册监听器。
  - 提供按 busId 注销和统一发布机制。
- UI 框架封装
  - 提供主菜单区域和设置页区域注入能力。
  - 支持基础本地化能力。

## 目录结构

- Core/
  - ReForge.cs
    - 模组初始化入口。
  - EventBus/
    - 事件总线与生命周期补丁。
  - FrameworkUI/
    - UI 门面、控件抽象、系统区域挂载与本地化。
  - Mixins/
    - 注解定义、扫描、绑定、冲突策略、生命周期管理、诊断。
- reforge/localization/
  - 本地化资源目录。
- build/
  - 构建输出目录（DLL、PCK、运行时文件等）。

## 环境要求

- .NET SDK 9.0
- Godot .NET SDK 4.5.1
- Slay the Spire 2 游戏文件（用于引用 sts2.dll 与 0Harmony.dll）

注意：项目文件 ReForge.csproj 中的 HintPath 使用了本机绝对路径，请按你的安装位置修改。

## 构建

在项目根目录执行：

```powershell
dotnet build --no-incremental
```

默认输出到：

- build/ReForge.dll

## 快速开始

1. 打开 Core/ReForge.cs，确认初始化流程。
2. 在 Core/Mixins/Examples 中添加你的 Mixin 类。
3. 使用 ReForge.Mixins.Register(...) 进行显式注册（入口已示例）。
4. 运行并通过日志或诊断快照验证补丁安装状态。

## 示例

- 卡牌奖励数量扩展示例：
  - Core/Mixins/Examples/CardRewardCountMixin.cs

该示例展示了如何使用 Mixin Postfix 对奖励结果进行扩展处理。

## 项目状态

当前版本：dev0.0.1

ReForge 目前定位为开发中基础库，适合做功能验证与模块化迭代。