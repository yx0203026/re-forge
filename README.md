# ReForge

ReForge 是面向 Slay the Spire 2 的 C# 模组基础库，核心目标是：

1. 提供稳定、可复用的运行时能力
2. 降低模组接入成本与样板代码
3. 让功能模块化、可诊断、可迭代

## 快速概览

当前版本重点提供：

1. 统一初始化基类与分层注册管线
2. Mixin 注解补丁系统（Prefix/Postfix/Finalizer）
3. EventBus 事件总线
4. UI 注入与本地化桥接
5. 模组加载器与资源系统（支持无 PCK，DLL 内嵌资源）
6. 模型纹理与通用命名纹理注册能力

## 30 秒上手

1. 新建模组并生成 ModMain 模板（已内置分层骨架）
2. 在分层注册里填充你的内容（卡牌/遗物/事件/纹理等）
3. 构建 DLL
4. 放入游戏 mods 目录并启动观察日志

关键日志前缀：

1. [ReForge]
2. [ReForge.ModLoader]
3. [ReForge.Mixins]

## 统一初始化与分层注册

ReForgeModBase 现在支持按语义分层注册，避免把所有逻辑塞进一个初始化函数。

### 功能分层

1. AscensionRegistrations
2. CardRegistrations
3. PowerRegistrations
4. RelicRegistrations
5. PotionRegistrations（占位）
6. MonsterRegistrations（占位）
7. BossRegistrations（占位）
8. CardPoolRegistrations
9. EventRegistrations
10. AncientEventRegistrations

### 纹理分层

1. CardPortraitRegistrations
2. RelicIconRegistrations
3. RelicIconOutlineRegistrations
4. RelicBigIconRegistrations
5. PowerIconRegistrations
6. PowerBigIconRegistrations
7. PotionPortraitRegistrations（命名纹理）
8. MonsterPortraitRegistrations（命名纹理）
9. BossPortraitRegistrations（命名纹理）
10. UiTextureRegistrations（命名纹理）
11. BackgroundTextureRegistrations（命名纹理）
12. MiscTextureRegistrations（命名纹理）

### 生命周期层

1. PreInitializationRegistrations
2. InitializationRegistrations
3. ModelInjectionRegistrations
4. ModelPoolRegistrations
5. PostInitializationRegistrations
6. RunStartedRegistrations
7. OnInitialize / OnRunStarted（兼容入口）

## 纹理系统说明

ReForge.ModelTextures 提供两类入口：

1. 模型纹理注册
2. 命名纹理注册（适用于 UI、背景、药水、怪物、Boss 等）

常用 API：

1. TryRegisterCardPortraitFromModResource
2. TryRegisterRelicIconFromModResource
3. TryRegisterRelicIconOutlineFromModResource
4. TryRegisterRelicBigIconFromModResource
5. TryRegisterPowerIconFromModResource
6. TryRegisterPowerBigIconFromModResource
7. TryRegisterNamedTextureFromModResource
8. TryGetNamedTexture

说明：

1. 资源加载走 ReForge 统一资源链路（PCK + Embedded）
2. 内置懒加载与缓存
3. 注册失败会记录错误日志，便于排障

## 无 PCK（DLL 内嵌资源）模式

默认清单支持无 PCK 形态：

1. has_pck: false
2. has_dll: true
3. has_embedded_resources: true

可在 build/reforge.json 查看。

实现要点：

1. 在 csproj 中使用 EmbeddedResource 打包图片与本地化资源
2. 运行时通过 ReForgeModManager 与 EmbeddedResourceSource 读取
3. 本地化通过 LocalizationResourceBridge 与 UiLocalization 补链

## 目录结构

1. Core/
2. Core/ReForge.cs：框架入口
3. Core/ReForge.ModBase.cs：统一基类与分层注册
4. Core/Mixins/：Mixin 注解与运行时
5. Core/EventBus/：事件总线
6. Core/ModLoading/：模组生命周期、模板生成、诊断
7. Core/ModResources/：资源源抽象、纹理注册、回退补丁
8. Core/Settings/：系统 UI、配置面板、本地化桥接
9. Core/EventWheel/：事件轮盘能力与 UI
10. reforge/：静态资源与本地化
11. build/：构建产物与清单

## 环境要求

1. .NET SDK 9.0
2. Godot .NET SDK 4.5.1
3. Slay the Spire 2 安装目录中的 sts2.dll 与 0Harmony.dll

请根据本机路径调整项目中的 HintPath。

## 构建

在 re-forge 目录执行：

```powershell
dotnet build ReForge.csproj -c Release
```

输出：

1. build/ReForge.dll

## 常见问题

### 只发 DLL，不发 PCK 可以吗

可以。前提是资源已嵌入 DLL，且清单与加载链路配置正确。

### 启动时报 DuplicateModelException

通常是模型注入时机过早导致。请优先使用 ReForge 的分层注册与延迟注入机制，不要在不安全时机直接 new 模型。

### 本地化键未生效

检查顺序：

1. 资源是否被正确打包
2. 清单是否声明 has_embedded_resources
3. 对应语言文件是否包含目标 key

## 项目状态

0. 上行加载：ReForge 提供上行加载模式，什么是上行加载？简单来说，就是 ReForge 在游戏启动时直接插入到游戏的底层初始化流程中（通常比SteamFramework更早），以实现对游戏的全局控制和修改，以上行模式启动的 ReForge 将彻底脱离模组的范畴，因此您将不会在游戏的模组列表中看到 ReForge 的存在，但它确实在幕后运行着，提供着强大的功能和支持。
值得注意的是，虽然上行加载模式提供了更早的控制权，但它也可能带来一些兼容性问题，特别是与其他模组的兼容性，因为它可能会修改游戏的核心行为。因此，在使用上行加载模式时，请务必确保您的模组与 ReForge 的兼容性或模组开发者将 ReForge 作为一个基础库来构建他们的模组，以避免潜在的冲突和问题。
1. 版本：dev0.0.1
2. 状态：开发中，适合功能验证与模块化迭代
3. 计划：持续迭代核心功能，优化性能与稳定性，扩展更多实用工具与示例模组，欢迎社区参与测试与贡献！