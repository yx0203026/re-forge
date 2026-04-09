# ReForge FrameworkUI 开发人员文档

## 目录
1. [架构概览](#架构概览)
2. [核心概念](#核心概念)
3. [快速开始](#快速开始)
4. [UI 元素详解](#ui-元素详解)
5. [布局系统](#布局系统)
6. [交互系统](#交互系统)
7. [本地化集成](#本地化集成)
8. [系统区域](#系统区域)
9. [运行时管理](#运行时管理)
10. [高级主题](#高级主题)
11. [最佳实践](#最佳实践)
12. [常见问题](#常见问题)

---

## 架构概览

### 分层架构

```
┌─────────────────────────────────────────┐
│         ReForgeUiFacade                 │  <- 统一入口
├─────────────────────────────────────────┤
│    Controls     │    Panels    │ Others  │  <- 具体控件
│  (Button,Label) │ (Panel,Stack)│ (etc.)  │
├─────────────────────────────────────────┤
│         UiElement (基类)                │  <- 链式 API
│    + 布局配置 + 交互绑定 + 视觉配置       │
├─────────────────────────────────────────┤
│         Runtime (生命周期)               │
│  - UiRuntimeNode: 挂载、生命周期管理    │
│  - UiLayoutApplier: 布局应用            │
│  - UiInteractionApplier: 交互绑定      │
├─────────────────────────────────────────┤
│      SystemAreas (系统预览区域)         │
│  - MainMenuScreen, SettingsScreen      │
│  - MainMenuButtonPanel, SettingTab     │
├─────────────────────────────────────────┤
│        Godot (底层渲染引擎)             │
└─────────────────────────────────────────┘
```

### 工作流程

```
创建 UiElement（如 Button）
    ↓
链式配置属性（WithHeight、OnClick 等）
    ↓
调用 Build() 生成 Godot Control
    ↓
添加到父容器或系统区域
    ↓
UiRuntimeNode 应用布局和交互
    ↓
用户交互 → 触发回调
```

---

## 核心概念

### 1. UiElement 基类

所有 UI 组件都继承自 `UiElement`，它提供：
- **链式 API**：支持流式配置
- **配置容器**：三个内部容器管理状态
  - `UiLayoutOptions`：尺寸、锚点、间距
  - `UiInteractionOptions`：事件处理器
  - `UiVisualOptions`：缩放、颜色、旋转等

```csharp
var button = new Button("Click Me", () => GD.Print("Clicked!"))
    .WithHeight(48f)
    .WithPadding(10f)
    .WithAnchor(UiAnchorPreset.Center)
    .OnHoverEnter(control => control.Modulate = Colors.Yellow);
```

### 2. Control 与 IUiElement

- `IUiElement` 接口：定义 `Build()` 方法，返回 Godot `Control`
- `UiElement` 抽象类：扩展接口，添加链式方法和配置
- 具体控件（`Button`、`Label` 等）：实现具体的 `CreateControl()` 逻辑

### 3. Facade 模式

`ReForgeUiFacade` 是进入点，提供：
- 屏幕访问：`GetMainMenuScreen()`、`GetSettingsScreen()`
- 区域访问：`GetMainMenuButtonPanel()`、`GetSettingTabPanel()`
- 本地化接口：`T(key)`、`SetLocale(locale)`
- 生命周期管理：`Initialize()`、`ReinjectSystemAreas()`

---

## 快速开始

### 1. 初始化 UI 系统

```csharp
// 在模组启动时调用一次
[ModInitializer]
public static void InitializeUI()
{
    ReForge.UI.Initialize();
}
```

### 2. 创建简单按钮

```csharp
var button = new Button(
    text: "点击我",
    onClick: () => GD.Print("按钮被点击了！"),
    stylePreset: UiButtonStylePreset.OfficialConfirm
)
    .WithHeight(48f)
    .WithAnchor(UiAnchorPreset.Center);

// 添加到主菜单屏幕
ReForge.UI.GetMainMenuScreen().AddChild(button);
```

### 3. 创建标签

```csharp
var label = new Label("欢迎来到 ReForge")
    .WithHeight(36f)
    .WithAnchor(UiAnchorPreset.TopCenter)
    .WithPositionOffset(0, 20f);

ReForge.UI.GetMainMenuScreen().AddChild(label);
```

### 4. 创建面板

```csharp
// 竖直面板，8px 间距
var panel = new Panel(spacing: 8)
    .WithAnchor(UiAnchorPreset.TopLeft)
    .WithPadding(10f);

// 添加子元素
panel.AddChild(new Label("第一项"));
panel.AddChild(new Button("选项 1", () => GD.Print("选项 1")));
panel.AddChild(new Button("选项 2", () => GD.Print("选项 2")));

ReForge.UI.GetMainMenuScreen().AddChild(panel);
```

---

## UI 元素详解

### Button（按钮）

```csharp
public class Button : UiElement
{
    public Button(
        string text = "Button",
        Action? onClick = null,
        string? textKey = null,              // 本地化 key
        string? locTable = null,             // 本地化表名
        string? locEntryKey = null,          // 本地化条目 key
        UiButtonStylePreset stylePreset = UiButtonStylePreset.GodotDefault,
        Action<Godot.Button>? customStyler = null  // 自定义样式器
    )
}
```

**样式预设：**
- `GodotDefault`：Godot 默认样式
- `OfficialConfirm`：游戏官方确认按钮样式
- `OfficialCancel`：游戏官方取消按钮样式

**示例：**
```csharp
new Button(
    "保存设置",
    () => GD.Print("设置已保存"),
    stylePreset: UiButtonStylePreset.OfficialConfirm
)
    .WithHeight(50f)
    .WithAnchor(UiAnchorPreset.BottomCenter)
    .WithMargin(0, 10, 0, 20)
    .OnHoverEnter(control => 
    {
        // 鼠标悬停时放大
        UiTweenAnimation.TweenScale(control, new Vector2(1.1f, 1.1f), 0.2f);
    });
```

### Label（标签）

```csharp
public class Label : UiElement
{
    public Label(
        string text,
        string? textKey = null,           // 本地化 key
        string? locTable = null,          // 本地化表名
        string? locEntryKey = null        // 本地化条目 key
    )
}
```

**特点：**
- 自动换行（`AutowrapMode.WordSmart`）
- 官方样式集成（Kreon 字体、轮廓）
- 本地化文本刷新

**示例：**
```csharp
new Label(
    "这是一个可能很长的描述文本，会自动换行。",
    locTable: "gameplay_ui",
    locEntryKey: "DESCRIPTION"
)
    .WithHeight(60f)
    .WithAnchor(UiAnchorPreset.TopCenter)
    .WithPadding(5f);
```

### Image（图像）

```csharp
public class Image : UiElement
{
    public Image(string texturePath)
}
```

**方法：**
- `WithScale(uniformScale)` 或 `WithScale(scaleVector)`
- `WithCenterPivot()` - 以中心为原点
- `WithTexturePath(path, stretchMode)` - 更改纹理

**示例：**
```csharp
new Image("res://icon.svg")
    .WithScale(2.0f)
    .WithCenterPivot()
    .WithAnchor(UiAnchorPreset.Center)
    .OnHoverEnter(control => 
    {
        control.Modulate = Colors.Yellow;
    });
```

### RichText（富文本）

```csharp
public class RichText : UiElement
{
    public RichText(string richTextMarkup)
}
```

**支持的标签：**
- 颜色：`[gold]黄金文本[/gold]`
- 加粗：`[b]加粗[/b]`
- 斜体：`[i]斜体[/i]`
- 下标：`[sub]下标[/sub]`

**示例：**
```csharp
new RichText("[gold]重要通知：[/gold] [green]系统已就绪[/green]")
    .WithHeight(30f)
    .WithAnchor(UiAnchorPreset.TopCenter);
```

### TickBox（复选框）

```csharp
public class TickBox : UiElement
{
    public TickBox(
        string labelText,
        bool initialValue = false,
        Action<bool>? onToggled = null
    )
}
```

**示例：**
```csharp
new TickBox("启用高级选项", initialValue: false, onToggled: isOn =>
{
    GD.Print($"高级选项：{isOn}");
})
    .WithHeight(32f);
```

---

## 布局系统

### UiLayoutOptions 容器

包含以下属性：
| 属性 | 类型 | 说明 |
|------|------|------|
| `Height` | `float?` | 显式高度 |
| `MinHeight` | `float?` | 最小高度 |
| `MaxHeight` | `float?` | 最大高度 |
| `AnchorPreset` | `UiAnchorPreset?` | 锚点预设 |
| `PositionOffset` | `Vector2?` | 位置偏移 |
| `Padding` | `UiSpacing?` | 内边距 |
| `Margin` | `UiSpacing?` | 外边距 |

### 锚点预设（UiAnchorPreset）

```csharp
public enum UiAnchorPreset
{
    TopLeft,      // 左上角
    TopCenter,    // 上中
    TopRight,     // 右上角
    MiddleLeft,   // 左中
    Center,       // 中心
    MiddleRight,  // 右中
    BottomLeft,   // 左下角
    BottomCenter, // 下中
    BottomRight,  // 右下角
    Stretch       // 填充父容器
}
```

### 间距（UiSpacing）

```csharp
// 所有边相同
WithPadding(10f)

// 水平和竖直
WithPadding(horizontal: 10f, vertical: 15f)

// 四边独立
WithPadding(left: 5f, top: 10f, right: 5f, bottom: 10f)

// 使用 UiSpacing 对象
var spacing = new UiSpacing(left: 5f, top: 10f, right: 5f, bottom: 10f);
WithPadding(spacing)

// 快速工厂方法
UiSpacing.All(10f)           // 所有边 10
UiSpacing.Axis(10f, 15f)     // 水平 10，竖直 15
```

### 应用链式配置

```csharp
new Button("应用我的配置", () => { })
    .WithHeight(48f)                    // 高度 48
    .WithMinHeight(40f)                 // 最小高度 40
    .WithMaxHeight(60f)                 // 最大高度 60
    .WithAnchor(UiAnchorPreset.Center)  // 居中
    .WithPositionOffset(0, 50f)         // 向下偏移 50
    .WithPadding(10f)                   // 内边距 10
    .WithMargin(5f, 10f, 5f, 10f)       // 外边距
```

### Panel 布局

**Panel（竖直面板）**
```csharp
var vPanel = new Panel(spacing: 8)  // 子元素间间距 8px
    .WithAnchor(UiAnchorPreset.TopLeft)
    .WithPadding(10f);
```

**StackPanel（灵活面板）**
```csharp
var hPanel = new StackPanel(
    horizontal: true,   // 水平排列
    spacing: 12
);

var vPanel = new StackPanel(
    horizontal: false,  // 竖直排列
    spacing: 8
);
```

**GridPanel（网格面板）**
```csharp
var grid = new GridPanel(
    columns: 3,        // 3 列
    spacing: 10
);

for (int i = 0; i < 9; i++)
{
    grid.AddChild(new Button($"${i}", () => GD.Print($"按钮 {i}")));
}
```

---

## 交互系统

### UiInteractionOptions

包含以下事件处理器：
| 方法 | 参数 | 说明 |
|------|------|------|
| `OnHoverEnter` | `Action<Control>` | 鼠标进入 |
| `OnHoverExit` | `Action<Control>` | 鼠标离开 |
| `OnLeftMouseDown` | `Action<Control, InputEventMouseButton>` | 左键按下 |
| `OnRightMouseDown` | `Action<Control, InputEventMouseButton>` | 右键按下 |
| `OnDrag` | `Action<Control, InputEventMouseMotion>` | 拖拽 |

### 交互示例

#### 悬停效果

```csharp
new Label("悬停试试")
    .WithHeight(30f)
    .OnHoverEnter(control => 
    {
        control.Modulate = Colors.Yellow;
    })
    .OnHoverExit(control => 
    {
        control.Modulate = Colors.White;
    })
```

#### 点击效果

```csharp
new Button("按下测试", () => GD.Print("Released!"))
    .WithHeight(48f)
    .OnLeftMouseDown((control, eventData) =>
    {
        GD.Print($"按下在位置: {eventData.Position}");
        // 按下时缩小
        UiTweenAnimation.TweenScale(control, new Vector2(0.95f, 0.95f), 0.1f);
    })
    .OnHoverExit(control =>
    {
        // 松开时恢复
        UiTweenAnimation.TweenScale(control, Vector2.One, 0.1f);
    })
```

#### 拖拽效果

```csharp
new Label("拖我")
    .WithHeight(40f)
    .WithCenterPivot()
    .OnDrag((control, motion) =>
    {
        // 拖拽时跟随鼠标位置
        control.Position += motion.Relative;
    })
```

#### 右键菜单

```csharp
new Label("右键点击我")
    .WithHeight(30f)
    .OnRightMouseDown((control, eventData) =>
    {
        GD.Print($"右键点击在: {eventData.Position}");
        // 弹出菜单
        ShowContextMenu(eventData.Position);
    })
```

### 动画辅助

```csharp
// 缩放动画
UiTweenAnimation.TweenScale(
    control,
    targetScale: new Vector2(1.2f, 1.2f),
    duration: 0.3f,
    transitionType: Tween.TransitionType.Cubic,
    easeType: Tween.EaseType.Out
);

// 颜色动画
UiTweenAnimation.TweenColor(
    control,
    targetColor: Colors.Red,
    duration: 0.5f
);

// 位置动画
UiTweenAnimation.TweenPosition(
    control,
    targetPos: new Vector2(100, 200),
    duration: 0.4f
);
```

---

## 本地化集成

### 注册语言包

```csharp
// 注册中文简体
var zhsEntries = new Dictionary<string, string>
{
    { "button.confirm", "确认" },
    { "button.cancel", "取消" },
    { "message.welcome", "欢迎来到游戏" },
    { "menu.settings", "设置" },
};

ReForge.UI.RegisterLocale("zhs", zhsEntries);

// 或使用官方名称别名
ReForge.UI.RegisterLocale("zh-cn", zhsEntries);  // 自动转换为 zhs
```

### 使用本地化文本

#### 方式 1：使用文本 Key

```csharp
// 配置时提供 key，UI 会自动从注册的语言包查询
new Button(
    text: "确认",  // 回退文本
    textKey: "button.confirm"
)
```

#### 方式 2：使用表和条目

```csharp
new Label(
    text: "欢迎",  // 回退文本
    locTable: "menu",           // 表名
    locEntryKey: "welcome"      // 条目名
)
// 查询逻辑：menu.welcome
```

#### 方式 3：使用 T() 方法查询

```csharp
// 直接查询文本
string confirmText = ReForge.UI.T("button.confirm", fallback: "Confirm");

// 使用表和 key
string subtitle = ReForge.UI.T("menu", "subtitle", "默认副标题");
```

### 切换语言

```csharp
// 切换到中文简体
ReForge.UI.SetLocale("zhs");

// 或使用别名
ReForge.UI.SetLocale("zh-cn");

// 或使用方法别名
ReForge.UI.SetLanguage("zhs");

// 获取当前语言
GD.Print($"当前语言: {ReForge.UI.CurrentLocale}");
```

### 文本刷新事件

所有包含本地化文本的控件会在语言切换时自动刷新：

```csharp
// 当调用 SetLocale 时，以下 UI 会自动更新
var label = new Label("原文本", textKey: "key");

// 用户切换语言
ReForge.UI.SetLocale("zhs");

// label 的文本自动变更（无需手动刷新）
```

---

## 系统区域

### SystemUiArea 枚举

```csharp
public enum SystemUiArea
{
    MainMenuButtonPanel = 0,    // 主菜单按钮面板
    SettingTabPanel = 1,        // 设置选项卡面板
    MainMenuScreen = 2,         // 整个主菜单屏幕
    SettingScreen = 3           // 整个设置屏幕
}
```

### 主菜单区域

#### 添加按钮到主菜单按钮面板

```csharp
var mainMenuPanel = ReForge.UI.GetMainMenuScreen().GetMainMenuButtonPanel();

mainMenuPanel.AddChild(
    new MainMenuButton(
        "我的按钮",
        () => GD.Print("点击了我的按钮"),
        locTable: "gameplay_ui",
        locEntryKey: "CUSTOM_BUTTON"
    )
        .WithHeight(46f)
        .WithAnchor(UiAnchorPreset.Stretch)
);
```

#### 添加元素到主菜单屏幕

```csharp
var mainMenuScreen = ReForge.UI.GetMainMenuScreen();

mainMenuScreen.AddChild(
    new Image("res://my_logo.png")
        .WithScale(2.0f)
        .WithCenterPivot()
        .WithAnchor(UiAnchorPreset.TopCenter)
        .WithPositionOffset(0, 50f)
);
```

### 设置区域

#### 添加选项卡

```csharp
var settingTabs = ReForge.UI.GetSettingsScreen().GetSettingTabPanel();

settingTabs.AddChild(
    new SettingTab(
        "我的设置",
        () => GD.Print("选项卡被点击"),
        selected: true  // 是否默认选中
    )
        .WithMinHeight(72f)
);
```

#### 获取并修改选项卡

```csharp
var settingTabs = ReForge.UI.GetSettingsScreen().GetSettingTabPanel();

// 获取已创建的选项卡
var myTab = settingTabs.GetSettingTab("我的设置");
if (myTab != null)
{
    // 添加设置选项到选项卡
    myTab.Add(
        SettingOptionItem.Toggle(
            "启用特性",
            initialValue: true,
            onToggled: isOn => GD.Print($"特性: {isOn}")
        )
    );
}
```

### 重新注入系统区域

```csharp
// 重新挂载所有系统区域（用于卸载/重新加载模组）
ReForge.UI.ReinjectSystemAreas();

// 或重新挂载特定区域
ReForge.UI.ReinjectArea(SystemUiArea.MainMenuButtonPanel);
ReForge.UI.ReinjectArea(SystemUiArea.SettingTabPanel);
```

---

## 运行时管理

### UiRuntimeNode 生命周期

`UiRuntimeNode` 是负责 UI 生命周期的单例节点，自动处理：
- UI 挂载和布局应用
- 交互绑定诡异
- 待挂载队列管理
- 可见性范围跟踪

### 全局 UI 挂载

```csharp
// 创建全局浮层按钮
var globalButton = new Button(
    "全局按钮",
    () => GD.Print("Global button clicked")
)
    .WithHeight(48f)
    .WithAnchor(UiAnchorPreset.TopRight)
    .WithPositionOffset(-20, 20);

// 添加到全局层（在所有屏幕上都可见）
var runtimeNode = UiRuntimeNode.Ensure();
runtimeNode.MountGlobal(globalButton.Build());
```

### 区域挂载

```csharp
// 挂载到特定系统区域
var runtimeNode = UiRuntimeNode.Ensure();
var element = new Label("区域内容");

runtimeNode.MountToArea(
    SystemUiArea.MainMenuButtonPanel,
    element.Build()
);
```

### 可见范围控制（Scoped Visibility）

某些 UI 只在特定屏幕可见：

```csharp
// 创建只在设置屏幕可见的 UI
var settingsOnlyUI = new Label("仅在设置屏幕显示")
    .WithHeight(30f);

// 标记为作用域 UI（自动在屏幕切换时隐藏/显示）
// 框架会自动处理可见性
```

---

## 高级主题

### 自定义控件

创建继承自 `UiElement` 的自定义控件：

```csharp
#nullable enable

using Godot;
using ReForgeFramework.UI.Abstractions;

namespace YourNamespace.UI;

public class CustomInput : UiElement
{
    private readonly string _placeholder;
    private readonly Action<string>? _onTextChanged;

    public CustomInput(string placeholder = "", Action<string>? onTextChanged = null)
    {
        _placeholder = placeholder;
        _onTextChanged = onTextChanged;
    }

    protected override Control CreateControl()
    {
        // 创建 Godot 原生控件
        LineEdit input = new()
        {
            PlaceholderText = _placeholder,
            CustomMinimumSize = new Vector2(200f, 40f)
        };

        // 绑定事件
        if (_onTextChanged != null)
        {
            input.TextChanged += _onTextChanged;
        }

        return input;
    }
}
```

**使用自定义控件：**
```csharp
new CustomInput(
    placeholder: "输入你的名字",
    onTextChanged: text => GD.Print($"输入: {text}")
)
    .WithHeight(40f)
    .WithAnchor(UiAnchorPreset.Center)
```

### 自定义样式器

为控件应用自定义样式：

```csharp
new Button(
    "自定义样式按钮",
    () => { },
    customStyler: button =>
    {
        // 直接修改 Godot Button 的属性
        button.CustomMinimumSize = new Vector2(250f, 50f);
        button.AddThemeColorOverride("font_color", Colors.Gold);
        button.AddThemeColorOverride("font_pressed_color", Colors.Orange);
        button.AddThemeStyleboxOverride(
            "normal",
            new StyleBoxFlat()
            {
                BgColor = new Color(0.2f, 0.2f, 0.2f)
            }
        );
    }
)
```

### 链式面板构建

```csharp
var panel = new StackPanel(horizontal: false, spacing: 8)
    .WithAnchor(UiAnchorPreset.Center)
    .WithPadding(20f);

// 链式添加子元素
panel
    .AddChild(new Label("标题").WithHeight(30f))
    .AddChild(new Label("描述文本").WithHeight(60f))
    .AddChild(
        new StackPanel(horizontal: true, spacing: 10)
            .AddChild(new Button("取消", () => { }).WithHeight(40f))
            .AddChild(new Button("确认", () => { }).WithHeight(40f))
    );
```

### 条件渲染

```csharp
var panel = new Panel();

if (ShouldShowAdvancedOptions())
{
    panel.AddChild(new Label("高级选项"));
    panel.AddChild(new TickBox("启用调试模式"));
}

// 添加常规选项
panel.AddChild(new Button("保存", () => { }));
```

---

## 最佳实践

### ✓ 做这些事

1. **使用链式 API 配置属性**
   ```csharp
   // ✓ 好
   new Button("OK", onClick)
       .WithHeight(48f)
       .WithAnchor(UiAnchorPreset.Center)
       .OnHoverEnter(c => c.Modulate = Colors.Yellow);
   ```

2. **为本地化 UI 提供 key 和回退文本**
   ```csharp
   // ✓ 好
   new Label(
       text: "保存",
       textKey: "button.save",
       fallback: "Save"
   )
   ```

3. **使用适当的锚点和间距**
   ```csharp
   // ✓ 好
   new Panel()
       .WithAnchor(UiAnchorPreset.TopLeft)
       .WithPadding(10f)
       .WithMargin(5f)
   ```

4. **创建可复用的 UI 工厂方法**
   ```csharp
   // ✓ 好
   private static Button CreateStandardButton(string text, Action onClick)
   {
       return new Button(text, onClick, stylePreset: UiButtonStylePreset.OfficialConfirm)
           .WithHeight(48f)
           .WithMinHeight(40f);
   }

   // 使用
   var btn = CreateStandardButton("确认", OnConfirm);
   ```

5. **在选项卡中组织相关设置**
   ```csharp
   // ✓ 好
   var settingTabs = ReForge.UI.GetSettingsScreen().GetSettingTabPanel();
   settingTabs.AddChild(
       new SettingTab("图形设置", () => { })
           .Add(SettingOptionItem.Slider("亮度", 0.5f))
           .Add(SettingOptionItem.Toggle("启用粒子"))
   );
   ```

### ✗ 避免这些事

1. **不要手动创建 Godot Control 而不使用 ReForge 包装**
   ```csharp
   // ❌ 不好
   var btn = new Godot.Button() { Text = "OK" };
   ReForge.UI.GetMainMenuScreen().AddChild(btn);

   // ✓ 好
   ReForge.UI.GetMainMenuScreen().AddChild(
       new Button("OK").WithHeight(48f)
   );
   ```

2. **不要忘记调用 Initialize()**
   ```csharp
   // ❌ 会导致问题
   // 模组启动时未调用 ReForge.UI.Initialize()

   // ✓ 必须做
   [ModInitializer]
   public static void Initialize()
   {
       ReForge.UI.Initialize();
   }
   ```

3. **不要在 _Process 中频繁重建 UI**
   ```csharp
   // ❌ 性能很差
   public override void _Process(double delta)
   {
       panel.Clear();
       for (int i = 0; i < 100; i++)
           panel.AddChild(new Label($"Item {i}"));
   }

   // ✓ 在需要时更新
   private bool _needsRebuild = true;
   public override void _Process(double delta)
   {
       if (_needsRebuild)
       {
           RebuildUI();
           _needsRebuild = false;
       }
   }
   ```

4. **不要混淆 Padding 和 Margin**
   ```csharp
   // Padding：内边距（元素内容到边界）
   // Margin：外边距（元素边界到父容器）

   // ✓ 正确使用
   .WithPadding(10f)    // 内容距边 10
   .WithMargin(5f)      // 距父容器 5
   ```

5. **不要在事件处理器中进行重操作**
   ```csharp
   // ❌ 会卡顿
   .OnLeftMouseDown((control, _) =>
   {
       for (int i = 0; i < 100000; i++)
           // 重操作
   })

   // ✓ 使用延迟或异步
   .OnLeftMouseDown((control, _) =>
   {
       control.CallDeferred("_method_name", "arg");
   })
   ```

---

## 常见问题

### 问题 1: UI 没有显示

**症状：**
- 创建了 UI 但看不见

**检查清单：**

1. 是否调用了 `Initialize()`？
   ```csharp
   [ModInitializer]
   public static void Initialize()
   {
       ReForge.UI.Initialize();  // ✓ 必须调用
   }
   ```

2. 是否正确挂载到了屏幕？
   ```csharp
   // ✓ 正确
   ReForge.UI.GetMainMenuScreen().AddChild(element);
   
   // ❌ 错误（没有添加）
   var element = new Button("OK");  // 只创建，没有添加
   ```

3. 是否设置了正确的锚点和位置？
   ```csharp
   // ✓ 明确指定锚点
   element.WithAnchor(UiAnchorPreset.Center)
   ```

4. 元素是否被设置为不可见？
   ```csharp
   // 检查 Godot 控件的 Visible 属性
   if (!control.Visible)
   {
       GD.PrintErr("Control is invisible!");
   }
   ```

### 问题 2: 布局不正确

**症状：**
- UI 元素位置错误或大小不对

**解决方案：**

1. 明确设置高度
   ```csharp
   .WithHeight(48f)  // 显式设置
   ```

2. 使用 MinHeight 确保最小可见性
   ```csharp
   .WithMinHeight(40f)
   ```

3. 检查 Padding 和 Margin
   ```csharp
   .WithPadding(10f)    // 内边距
   .WithMargin(5f)      // 外边距
   ```

4. 验证锚点设置
   ```csharp
   // 如果父容器很小，Stretch 可能无效
   .WithAnchor(UiAnchorPreset.Stretch)
   ```

### 问题 3: 本地化文本不更新

**症状：**
- 语言切换后，某些 UI 文本没有更新

**解决方案：**

1. 创建 UI 时提供 `textKey`
   ```csharp
   // ✓ 正确
   new Label("默认文本", textKey: "my.key")
   
   // ❌ 不会刷新
   new Label("静态文本")  // 没有 key
   ```

2. 注册本地化条目
   ```csharp
   var entries = new Dictionary<string, string>
   {
       { "my.key", "本地化文本" }
   };
   ReForge.UI.RegisterLocale("zhs", entries);
   ```

3. 检查本地化键名
   ```csharp
   // 确保 key 名称一致
   RegisterLocale("zhs", new() { { "key", "值" } });
   new Label(textKey: "key")  // 必须完全匹配
   ```

### 问题 4: 点击事件没有触发

**症状：**
- UI 控件可见但点击无响应

**检查清单：**

1. 是否绑定了事件处理器？
   ```csharp
   // ❌ 错误：没有回调
   new Button("Click");
   
   // ✓ 正确
   new Button("Click", () => GD.Print("Clicked!"));
   ```

2. ClickableControl 是否启用？
   ```csharp
   // ✓ 设置为可点击
   button.FocusMode = Control.FocusModeEnum.All
   ```

3. 事件处理器是否抛出异常？
   ```csharp
   .OnLeftMouseDown((control, _) =>
   {
       try
       {
           // 代码
       }
       catch (Exception ex)
       {
           GD.PrintErr($"异常: {ex.Message}");
       }
   })
   ```

### 问题 5: 内存泄漏

**症状：**
- 卸载模组后，UI 相关对象仍然存在

**解决方案：**

1. 调用 `ReinjectSystemAreas()` 重新初始化
   ```csharp
   ReForge.UI.ReinjectSystemAreas();
   ```

2. 移除事件处理器
   ```csharp
   panel.Clear();  // 清除所有子元素
   ```

3. 使用 `QueueFree()` 销毁控件
   ```csharp
   control.QueueFree();
   ```

### 问题 6: 样式预设不可用

**症状：**
- 按钮样式不生效

**解决方案：**

1. 检查样式预设是否存在
   ```csharp
   // ✓ 已定义的预设
   UiButtonStylePreset.OfficialConfirm
   UiButtonStylePreset.OfficialCancel
   UiButtonStylePreset.GodotDefault
   ```

2. 使用 `customStyler` 应用自定义样式
   ```csharp
   new Button(
       "自定义",
       () => { },
       customStyler: button =>
       {
           button.AddThemeColorOverride("font_color", Colors.Gold);
       }
   )
   ```

---

## 总结

ReForge FrameworkUI 提供了一个**声明式、链式、类型安全**的方式来构建 STS2 UI。通过理解其分层架构和核心概念，你可以快速创建美观、响应式的用户界面。

关键要点：
- ✅ 使用 `ReForgeUiFacade` 作为统一入口
- ✅ 链式配置属性以提高代码可读性
- ✅ 充分利用本地化系统
- ✅ 理解系统区域的作用
- ✅ 遵循最佳实践避免常见陷阱
