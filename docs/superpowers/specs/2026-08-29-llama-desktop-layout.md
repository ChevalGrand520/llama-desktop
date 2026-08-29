# Llama Desktop 布局重构设计

日期：2026-08-29
状态：待用户确认（架构策划，不落地实现）
前置：`2026-08-28-llama-desktop-architecture.md`（总架构）、`2026-08-28-llama-desktop-mvp.md`（MVP 计划）

## 1. 摘要

当前 MVP 采用「左侧固定控制面板 + 右侧 WebView2 聊天区 + 底部日志」三段布局。用户反馈：对话区被挤在右侧、日志区过大、模型/启动/日志等控制元素占据不当位置。

本次重构将布局改为「对话区最大化 + 右侧可收纳侧边栏」：WebView2 聊天区占据窗口全部剩余空间，模型选择、启动/停止、API 地址、实时日志全部收入右侧 320px 侧边栏，侧边栏支持动画平滑收纳/展开并跨启动记住状态。

本设计只描述架构与改动方案，不包含实现代码，不修改任何 `.cs` / `.xaml` 文件。

## 2. 现状问题（来自 `ShellWindow.xaml` 与运行截图）

1. 左侧控制面板列宽 `Width="Auto" MinWidth="280"`，宽度随模型路径长度变化，长路径撑宽面板、挤压右侧聊天区。
2. 底部日志 `Expander` 默认展开且 `TextBox Height="160"` 固定，占据窗口约 1/4 高度，聊天区偏矮。
3. 模型下拉直接显示完整路径，加剧面板撑宽。
4. 启动/停止为上下两个 40px 按钮，占用侧向垂直空间。
5. 顶部标题、状态、两个按钮挤在一行，宽窗口留白、窄窗口挤压。

## 3. 已确认决策（grill-me 结论）

| 决策点 | 结论 |
|---|---|
| 收纳交互 | 动画平滑过渡（约 150ms，320px ↔ 0） |
| 收纳按钮位置 | 顶部工具栏最右，常驻可见 |
| 按钮样式 | harness 同款面板图标（Lucide `PanelLeft`：圆角矩形 + 内部竖线，16px 描边）+ ToolTip，展开/收起态镜像或淡化区分 |
| 日志默认状态 | 默认收起（Expander 折叠） |
| 启动/停止 | 合并为单个切换按钮，中间态禁用 |
| 模型显示 | 文件名 + 悬停 ToolTip 显示完整路径 |
| 状态持久化 | 收纳状态写入配置，跨启动记住 |
| 最小窗口宽度 | 1000px（侧栏展开时聊天区 ≥ 630px） |

## 4. 目标与非目标

### 4.1 目标

1. WebView2 聊天区占据窗口全部剩余空间。
2. 模型选择、启动/停止、API 地址、实时日志统一收入右侧可收纳侧边栏。
3. 侧边栏收纳/展开带动画，且可跨启动恢复。
4. 不改变 Core 校验/参数构造、Infrastructure 进程层、WebView2 导航策略的既有行为。

### 4.2 非目标

- 不重做聊天系统，不改官方 Web UI。
- 不新增模型下载/转换/更新。
- 不改服务生命周期状态机语义（只改启停按钮的 UI 表达）。
- 不引入 GridSplitter 拖拽调宽（本次仅动画收纳，拖拽调宽留待后续）。
- 不改动 `LlamaDesktop.Core` 与 `LlamaDesktop.Infrastructure` 的公共接口，除非配置模型字段调整所必需。

## 5. 目标布局

```text
┌────────────────────────────────────────────────────────────────┐
│ Llama Desktop   状态文本        [复制 API] [打开聊天]  [▣|收纳] │
├──────────────────────────────────────────────┬─────────────────┤
│                                              │ GGUF 模型        │
│                                              │ [文件名▾] [浏览] │
│        WebView2 聊天区（* 列，占据全部剩余）    │ ─────────────── │
│                                              │ [启动/停止切换]  │
│                                              │ API 地址         │
│                                              │ ▾ 实时日志(默认收起)│
│                                              │   (展开填充剩余) │
│                                              │ 侧栏 320px 可收纳 │
└──────────────────────────────────────────────┴─────────────────┘
```

## 6. 现状代码盘点

- `LauncherConfig.UiState`（`src/LlamaDesktop.Core/Models/LauncherConfig.cs`）已有 `LogDrawerOpen`、`LeftPanelWidth`（默认 300），但 `CompositionRoot` 只取 `Load()?.Settings`，`Ui` 从未被读取应用。
- `ShellViewModel` 构造只接收 `ServerSettings`，未接收 `UiState`；未暴露侧栏/日志状态；模型集合为 `ObservableCollection<string>`（全路径）。
- `ShellWindow.xaml` 三段布局 + 两个独立启停按钮 + 固定 160px 日志。
- `ShellWindow.xaml.cs` 只负责 WebView2 挂载与日志 `CollectionChanged` 联动。
- `CompositionRoot` 手动组装，`config.json` 位于 `%LOCALAPPDATA%\LlamaDesktop\config.json`，由 `JsonConfigStore` 原子写入。
- 服务生命周期状态枚举 `ServerLifecycleState`（Core），`ShellViewModel` 以 `_state` 驱动 `CanStart` / `CanStop`。

## 7. 改动设计（按文件）

### 7.1 配置模型 `LauncherConfig.cs`（Core）

调整 `UiState` 字段语义，保持 `SchemaVersion` 不变（理由见 7.1.1）：

```csharp
public sealed record UiState
{
    public bool SidebarOpen { get; init; } = true;    // 侧栏是否展开
    public double SidebarWidth { get; init; } = 320;  // 侧栏展开宽度（px）
    public bool LogDrawerOpen { get; init; } = false; // 日志抽屉默认收起
}
```

**7.1.1 版本与迁移决策**

- 不提升 `SchemaVersion`（仍为 2）。理由：`Ui` 字段此前从未被读取应用，旧 `config.json` 中即使存在 `LeftPanelWidth`，System.Text.Json 反序列化会忽略未知字段、缺失字段用 `init` 默认值补齐，无行为回归。
- 删除 `LeftPanelWidth`，新增 `SidebarOpen` 与 `SidebarWidth`；`LogDrawerOpen` 语义不变（默认值由 `true` 改为 `false`，对应「日志默认收起」）。
- 不做专门的旧字段迁移代码；首次运行以默认 UI 状态（侧栏展开、日志收起）呈现。

### 7.2 `ShellViewModel.cs`（App）

新增与调整（不改变服务生命周期核心逻辑）：

- 构造函数增加 `UiState ui` 参数，保存初始 `IsSidebarOpen`、`SidebarWidth`、日志抽屉初始态。
- 新增可绑定属性：`IsSidebarOpen`、`SidebarWidth`（double）、`IsLogDrawerOpen`、`IsSidebarAnimating`。
- 新增 `ToggleSidebarCommand`（`RelayCommand`，`CanExecute = !IsSidebarAnimating`）。
- 新增 `ToggleServerCommand`（唯一对外启停入口）；`StartAsync`/`StopAsync` 保持为私有方法，`StartCommand`/`StopCommand` 不再对外暴露。
- 新增 `StartStopLabel`（"启动服务"/"停止服务"）与 `CanToggleServer`（中间态 false）。
- 新增 `FileNameConverter` 需要的显示名支持不在此处，见 7.4。
- 新增 `SaveUiState()`：切换侧栏/日志抽屉后即时 `_configStore.Save(new LauncherConfig { Settings = _settings, Ui = current })`。
- 状态变化时统一刷新 `StartStopLabel`、`CanToggleServer`、`CanStart`、`CanStop` 的 `OnPropertyChanged`。

**关键点**：`ToggleServerCommand` 内部根据 `_state` 分发到 `StartAsync()` 或 `StopAsync()`；`StartStopLabel` 由 `CanStop`（运行态）决定文案；中间态（启动中/停止中）`CanToggleServer=false`。

### 7.3 `ShellWindow.xaml`（App）

- 窗口 `MinWidth="1000"`，`Width="1180"` 保持不变（高度维持 780）。
- 顶栏（Row 0）：标题、状态、`[复制 API]`、`[打开聊天]`、最右 `[收纳]` 图标按钮（`Command=ToggleSidebarCommand`，图标为 harness 同款 `PanelLeft` 面板轮廓，见 8.6）。
- 主区（Row 1）改为两列：`*`（聊天区）+ `Auto`（侧栏容器，实际宽度由容器 `Grid.Width` 绑定 `SidebarWidth` 控制）。
- 聊天区列：仅 `Border` + `WebHostGrid`，不再放任何其他控件。
- 侧栏列：内嵌 `Grid`（绑定 `Width`），行结构：
  - 模型区（Auto）：标签 + `ComboBox`（`ItemTemplate` 显示文件名，`ToolTip` 显示全路径）+ 浏览按钮。
  - 主操作（Auto）：单个切换按钮，`Content=StartStopLabel`，`Command=ToggleServerCommand`，`IsEnabled=CanToggleServer`。
  - API 地址（Auto）：`TextBlock` 绑定 `ApiBaseUrl`。
  - 实时日志（`*`）：`Expander IsExpanded=IsLogDrawerOpen`，内部 `TextBox` 填满剩余高度（不再固定 160px）。

### 7.4 `ShellWindow.xaml.cs`（App）

- 新增 `SidebarToggle` 动画逻辑（code-behind 纯视图职责）：
  - 展开：先设 `Visibility=Visible`，再对侧栏容器 `Width` 做 `DoubleAnimation`（0 → `SidebarWidth`，150ms，`FillBehavior=Stop`），完成后显式写回最终值并清除动画。
  - 收起：`Width` 动画（当前 → 0，150ms），完成后设 `Visibility=Collapsed`。
  - 动画期间置 `IsSidebarAnimating=true`（驱动 `ToggleSidebarCommand` 禁用），完成后复位。
- 保留日志 `CollectionChanged` 联动与 `RebuildLogText`。
- 新增 `Presentation/Converters/FileNameConverter.cs`（`IValueConverter`，取 `Path.GetFileName`；供模型下拉 `ItemTemplate` 显示文件名，`ToolTip` 仍绑定全路径）。

### 7.5 `CompositionRoot.cs`（App）

- `Load()` 后同时读取 `saved.Ui`（非空时），与 `ServerSettings` 一并传入 `ShellViewModel`。
- 保证旧配置缺失 `Ui` 时使用 `UiState` 默认值（侧栏展开、日志收起）。

## 8. 关键实现决策

### 8.1 动画与重入

- 用标准 `DoubleAnimation` 作用于侧栏容器 `Grid.Width`（double），避免 `ColumnDefinition.Width` 的 `GridLength` 动画复杂度。
- 侧栏列 `ColumnDefinition.Width="Auto"`，聊天区列 `Width="*"`；侧栏宽度的变化由容器 `Width` 驱动，聊天区自动重排。
- `FillBehavior=Stop` + `Completed` 中写回最终值并 `BeginAnimation(..., null)`，防止动画持有效应干扰后续手动设置。
- 动画期间 `IsSidebarAnimating=true` 使切换命令不可用，防止重入。

### 8.2 持久化时机

- 侧栏展开/收起、日志抽屉开合状态变化后即时 `SaveUiState()`。
- 窗口关闭兜底：在 `ShellWindow.xaml.cs` 的 `Closing` 事件中调用 `SaveUiState()`；若切换已即时落盘，则该调用幂等，主要覆盖「动画中关闭窗口」等未及保存的边界。
- 不改变 `StartAsync` 中现有的启动前 `Save` 行为；`SaveUiState` 与它共用同一 `JsonConfigStore` 与原子写路径。

### 8.3 合并启停按钮

- UI 单一按钮 + `ToggleServerCommand`；`StartCommand`/`StopCommand` 不再直接绑定到按钮。
- 文案与可用性由 `StartStopLabel` / `CanToggleServer` 驱动，二者在 `_state` 变化点同步 `OnPropertyChanged`。
- `CanToggleServer = CanStart || CanStop`：`Stopped`/`Failed`/`StopFailed` 可启动，`Running`/`UiReady`/`WaitingForUi` 可停止（`WaitingForUi` 允许中止加载中的模型），`StartingProcess`（进程对象刚创建）禁用，避免重复启停。

### 8.4 模型显示

- `Models` 仍为全路径字符串集合，`SelectedModel` 仍存全路径——只改显示层。
- `ComboBox.ItemTemplate` 内 `TextBlock.Text` 经 `FileNameConverter` 显示文件名，`TextBlock.ToolTip` 绑定全路径原文。
- 不引入包装模型对象，最小化对 `BrowseModel` / `SelectedModel` / 校验链路的改动。

### 8.5 日志区

- 从底部全局区移入侧栏内，`Expander` 默认收起（`IsExpanded` 绑定 `IsLogDrawerOpen`，默认 `false`）。
- 展开时 `TextBox` 填充侧栏剩余高度（侧栏内 `*` 行），不再与聊天区争高度。

### 8.6 收纳按钮图标（harness 同款）

- 参考 DeepSeek Harness Web 前端图标 `IconPanelLeftOutline16`，即 Lucide `PanelLeft` 面板轮廓：一个圆角矩形 + 内部一条竖直分隔线，16px、描边（stroke）风格、圆角线帽。
- 源 SVG（Lucide `panel-left`）：
  ```svg
  <rect width="18" height="18" x="3" y="3" rx="2"/>
  <path d="M9 3v18"/>
  ```
  （24×24 viewBox，`stroke="currentColor"`，`stroke-width="2"`，`stroke-linejoin="round"`。）
- WPF 落地：以 `Path` 呈现（`Fill=null`，`Stroke` 绑定前景色，`StrokeThickness≈1.5`，`StrokeLineJoin=Round`），`Data` 用等价几何：
  - 圆角矩形：`M5,3 H19 A2,2 0 0 1 21,5 V19 A2,2 0 0 1 19,21 H5 A2,2 0 0 1 3,19 V5 A2,2 0 0 1 5,3 Z`
  - 竖线：`M9,3 L9,21`
- 侧栏在右侧，故展开态可选用 `PanelLeft` 的水平镜像（竖线在图标右侧）或直接沿用 `PanelLeft` 并依赖 ToolTip 区分；本设计采用**沿用 harness 原图标 + ToolTip「收起侧栏/展开侧栏」区分状态**，不额外做镜像动画。
- 图标随主题前景色（`Foreground`）继承，深/浅色均可用；不引入位图资源，用几何路径保证任意 DPI 清晰。

## 9. 状态与行为

### 9.1 切换按钮状态映射

| 生命周期状态 | `StartStopLabel` | `CanToggleServer` |
|---|---|---|
| `Stopped` / `Failed` / `StopFailed` | 启动服务 | true |
| `Running` / `UiReady` / `WaitingForUi` | 停止服务 | true |
| `StartingProcess`（进程对象刚创建） | —（禁用） | false |

映射公式：`CanToggleServer = CanStart || CanStop`，与现有 `CanStart`/`CanStop` 语义完全一致，不改变生命周期状态机行为；`WaitingForUi` 阶段保留「可停止」以允许用户中止加载中的模型。

### 9.2 侧栏动画时序

1. 点击收纳按钮（命令可用前提：非动画中）。
2. 置 `IsSidebarAnimating=true`。
3. 展开路径：`Visibility=Visible` → 动画 0→320 → 完成写回 + 清除动画 → `IsSidebarAnimating=false` → `SaveUiState()`。
4. 收起路径：动画 320→0 → 完成 `Visibility=Collapsed` → 写回 + 清除动画 → `IsSidebarAnimating=false` → `SaveUiState()`。

## 10. 边界与风险

- **WebView2 重排**：侧栏收起/展开会触发聊天区尺寸变化，WebView2 自适应；不强制重载页面。
- **DPI 缩放**：`SidebarWidth=320` 为 DIP，动画与宽度均用 DIP，缩放正确。
- **动画中断**：窗口在动画中关闭，需在 `Closing` 中停止 Storyboard/动画引用，避免对象已销毁仍回调（沿用现有 `ProcessExited` 的 Dispatcher 归队思路）。
- **配置写失败**：`SaveUiState` 异常仅记日志，不阻断 UI（与现有 `Save` 一致）。
- **最小宽度**：`MinWidth=1000` 保证侧栏展开时聊天区 ≥ 630px；用户手动缩到 1000 以下仍受 `MinWidth` 约束。
- **旧配置兼容**：`Ui` 缺失/字段未知时回退默认值，无迁移失败风险（见 7.1.1）。

## 11. 验证方案（落地后执行，本次不执行）

- `dotnet build LlamaDesktop.sln -c Release` 0 错误。
- 全量 `dotnet test` 仍通过（Core/Infrastructure 不应受影响；App 测试如涉及 ViewModel 构造需同步更新构造签名）。
- 手动验收：
  - 启动后聊天区占满、侧栏 320px 展开、日志默认收起。
  - 点击收纳 → 侧栏平滑收起、聊天区扩展；再次点击 → 恢复。
  - 收纳状态跨启动记住。
  - 启动/停止按钮随状态切换文案与可用性，中间态禁用。
  - 模型下拉显示文件名，悬停可见完整路径。
  - 窗口缩至 1000 宽时聊天区仍可正常使用。

## 12. 变更范围汇总

| 文件 | 变更类型 |
|---|---|
| `src/LlamaDesktop.Core/Models/LauncherConfig.cs` | 修改 `UiState` 字段 |
| `src/LlamaDesktop.App/Presentation/ViewModels/ShellViewModel.cs` | 新增侧栏/切换/持久化逻辑 |
| `src/LlamaDesktop.App/ShellWindow.xaml` | 布局重排 |
| `src/LlamaDesktop.App/ShellWindow.xaml.cs` | 新增动画 + 保留日志联动 |
| `src/LlamaDesktop.App/Presentation/Converters/FileNameConverter.cs` | 新增 |
| `src/LlamaDesktop.App/CompositionRoot.cs` | 传递 `UiState` |

不修改：`LlamaDesktop.Core`（除配置模型外）、`LlamaDesktop.Infrastructure`、`WebViewHost`/`NavigationPolicy`、测试项目（除因构造函数签名变化所需的同步）。
