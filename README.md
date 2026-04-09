# ReForge

ReForge 是一个面向 Slay the Spire 2 的 C# 模组基础库，目标是提供可复用、可扩展的开发能力，减少重复造轮子成本。

当前版本重点能力：
- 统一初始化入口（Harmony + Mixins + EventBus + UI + ModLoader）
- Mixin 注解补丁系统
- 事件总线系统
- UI 注入与本地化桥接
- 自定义模组加载器，支持无 PCK 的 DLL 内嵌资源模式

## 核心特性

### 1. 统一初始化
- 入口在 `Core/ReForge.cs`
- 在模组初始化阶段完成关键子系统装配

### 2. Mixin 注解补丁
- 支持 Prefix、Postfix、Finalizer 与扩展注入语义
- 提供注册结果、生命周期状态与诊断信息

### 3. EventBus 事件总线
- 支持显式监听器注册与特性扫描注册
- 支持按 busId 注销与统一发布

### 4. FrameworkUI
- 主菜单、设置页等系统区域注入
- UI 文本查询与语言切换桥接

### 5. ModLoading + ModResources
- `Core/ModLoading`：发现清单、依赖校验、加载状态机、初始化执行、诊断输出
- `Core/ModResources`：PCK 资源源与 Embedded 资源源统一抽象

## 无 PCK（DLL 内嵌资源）模式

ReForge 当前默认清单已切换到无 PCK 形态：
- `has_pck: false`
- `has_dll: true`
- `has_embedded_resources: true`

可见于 `build/reforge.json`。

实现要点：
- 在 `ReForge.csproj` 中通过 `EmbeddedResource` 将图片、本地化 JSON 等资源打入 DLL
- 运行时通过 `ReForgeModManager` + `EmbeddedResourceSource` 读取内嵌资源
- 本地化通过 `LocalizationResourceBridge` 与 `UiLocalization` 补充官方表查询链路

注意：第三方模组是否可无 PCK，取决于其是否接入 ReForge 这套加载/资源协议。

## 目录结构

- `Core/`
  - `ReForge.cs`：模组初始化入口
  - `EventBus/`：事件总线
  - `FrameworkUI/`：UI 门面、控件、系统区域与本地化
  - `Mixins/`：Mixin 注解与运行时
  - `ModLoading/`：模组生命周期与诊断
  - `ModResources/`：资源路径解析与资源源实现
- `reforge/`
  - `image/`：图片资源
  - `localization/eng`、`localization/zhs`：本地化资源
- `build/`
  - 构建输出与模组清单（`reforge.json`）

## 环境要求

- .NET SDK 9.0
- Godot .NET SDK 4.5.1
- Slay the Spire 2 游戏文件（用于引用 `sts2.dll` 与 `0Harmony.dll`）

请按你的本机安装路径修改 `ReForge.csproj` 中 `HintPath`。

## 构建

在项目根目录执行：

```powershell
dotnet build ReForge.csproj
```

默认输出：
- `build/ReForge.dll`

## 快速开始

1. 查看入口初始化流程：`Core/ReForge.cs`
2. 在 `Core/Mixins/Examples` 添加你的 Mixin 示例或实际补丁
3. 通过 `ReForge.Mixins.Register(...)` 注册 Mixin（入口已示例）
4. 运行游戏并观察日志：
   - `[ReForge] ...`
   - `[ReForge.ModLoader] ...`

## 常见问题

### Q1：只发 DLL，不发 PCK 可以吗？
可以。前提是资源已嵌入 DLL，并且清单配置与加载器链路匹配（见上文无 PCK 模式）。

### Q2：本地化键找不到怎么办？
优先检查：
- 资源是否被 `EmbeddedResource` 正确打包
- `build/reforge.json` 是否声明 `has_embedded_resources: true`
- 对应语言文件是否包含目标 key

## 项目状态

- 版本：`dev0.0.1`
- 状态：开发中，适合功能验证与模块化迭代