# Llama Desktop 最终 Brainstorm 审视

## 总体评价

三层划分总体成立，删除未接入生产路径的状态机也比保留“看似完整”的死抽象更诚实；`CompositionRoot` 仍可读，25 个测试对 Core/Infrastructure 基础行为提供了不错的起点。当前主要风险不是文件行数，而是新参数的命令行语义、跨异步任务的生命周期所有权，以及 WebView2 初始化实际上分成了两条互不协调的路径；这些应在下一版发布前处理。

## 高优先级

1. **先修正参数与实际 CLI 语义不一致**：问题 → `Threads` 未被使用，构建器始终把 `Environment.ProcessorCount` 写入 `-t`；Flash Attention/Fit/Thinking 选 `off` 时反而省略参数，而当前 llama-server 默认分别是 `auto/on/auto`；`ModelsMax` 又没有配套 `--models-dir`，且错误地用 `caps.ModelsDir` 做能力门控。具体改法 → 让构建器使用 `s.Threads`，对三组选项显式发出所选值；在“单模型”和“Router 目录”之间建立互斥模式，Router 模式同时发出 `--models-dir` 与 `--models-max`，或在实现前先隐藏“最大模型数”。预期收益 → UI 所见即所得，避免用户以为关闭/限流已生效而实际仍走默认值。
2. **把一次服务运行收束成有代号、可取消的 session**：问题 → 停止期间 `_state` 仍是 Running/WaitingForUi，按钮可重复触发 `_ = StopAsync()`；旧 `PollHealthAsync` 读取共享 `_controller/_serviceUri`，可能在停止或下一次启动后继续写状态；旧进程退出回调也没有来源校验。具体改法 → 引入一个轻量 `ServerSessionCoordinator`，每次启动捕获 controller、URI、generation 和 CTS；增加真实的 Stopping/Busy 状态，所有健康、退出、停止结果只允许更新自己的 generation，并由可观察的 async command 接住异常。预期收益 → 消除重复停止、Running/Ready 短暂复活和旧任务污染新会话的竞态，同时让 `ShellViewModel` 明显减重。
3. **合并 WebView2 的初始化所有权**：问题 → `WebViewHost.InitializeAsync` 创建并丢弃 `CoreWebView2Environment`，实际控件随后又用默认环境初始化；若健康检查先成功，`NavigateToService` 会抛错且没有重试；异常只写入局部 `logs` 列表，界面不可见。具体改法 → 不要把当前薄封装直接内联进 VM；让一个真正的 ChatSurface/WebView coordinator 持有控件，用同一个 environment 调用 `EnsureCoreWebView2Async(environment)`，缓存最新待导航 URI，并把错误写入共享日志 sink。预期收益 → 自定义 user-data 目录真正生效，初始化顺序不再决定聊天页能否出现，故障也可诊断。
4. **让“实时日志”在 Running 后继续工作**：问题 → `ReadLog()` 只在健康轮询循环中调用，首次健康成功后 `break`，日志抽屉随即停止更新。具体改法 → 把日志 pump 与健康探测拆成同一 session 下的两个可取消任务，退出/停止时统一结束并做最后一次 drain。预期收益 → 日志名副其实，也避免为了看新日志而重启服务。
5. **先补行为测试再动生命周期结构**：问题 → 当前 11 个 Core、12 个 Infrastructure、2 个 App 测试中，App 只测导航策略；没有覆盖参数表单解析、StopAsync 三阶段、身份拒停、重复停止、旧轮询隔离和 WebView 初始化乱序。具体改法 → 给 coordinator/ChatSurface 注入最小接口与可控 fake，优先写上述时序测试，并给每个新参数添加“设置值 → 精确 argv”表驱动测试。预期收益 → 架构拆分由行为护栏驱动，而不是仅把 430 行搬到多个文件。

## 中优先级

1. **保留字符串编辑态，但移出 ShellViewModel**：问题 → 直接把 TextBox 绑定为 `int` 会破坏空值、半输入等正常编辑态，而当前逐行 `TryPositiveInt` 虽不优雅却清楚。具体改法 → 建立纯 `ServerSettingsDraft`/解析结果并实现 `INotifyDataErrorInfo`；不要用反射或泛型循环压缩六行校验。预期收益 → 可单测、可逐字段提示，同时避免“代码更短但更难读”。
2. **收紧选项与额外参数输入**：问题 → 配置文件可带入任意 Flash/Fit/KV/Thinking 字符串，额外参数还在 VM 与 Builder 两处用空格 `Split`，带引号或空格的值会损坏。具体改法 → 选项改为 enum/受限值对象；只保留一个遵循 Windows 引号规则的 tokenizer，验证与构建复用同一 token 列表。预期收益 → 消除静默降级和验证、执行不一致。
3. **重排侧栏滚动边界**：问题 → 参数、主按钮、API 与日志都位于同一个外层 `ScrollViewer`，日志又有内层滚动，长侧栏会让核心操作和日志入口沉到底部。具体改法 → 固定模型/启停/状态区，只滚动参数区；日志改为有上限高度的底部抽屉或主区可调整面板，并在字段旁显示校验错误。预期收益 → 常用路径始终可见，避免嵌套滚动争抢。
4. **让发布脚本形成单向流水线**：问题 → `publish.ps1`、`verify-publish.ps1`、`package-release.ps1` 彼此独立，打包脚本可能压入旧 `dist`，默认版本仍是 1.0.0，且未验证压缩包内容。具体改法 → 增加单一入口按 publish → verify → package → verify-archive 执行，版本只从一个参数/标签传入，并输出文件清单与哈希。预期收益 → 降低发布陈旧或缺 DLL 包的概率，同时保留三个脚本作为内部步骤。
5. **模型列表先做低成本元数据**：问题 → 同名文件难区分，完整 GGUF 解析又会拖慢启动。具体改法 → 用 `ModelItem(Path, FileName, Size, QuantHint)` 取代裸字符串，先异步显示大小和文件名量化提示；需要精确值时再缓存读取 GGUF header。预期收益 → 选择模型更可靠，且不阻塞首屏。

## 可暂缓

1. **800MB 级体积**：当前约 865MB 的 `dist` 中三项 CUDA 文件约 624MB；在“零依赖 CUDA 便携包”目标下这不是代码膨胀。先做依赖闭包实验，再考虑 CPU/CUDA 双包，勿凭文件名删 DLL。
2. **UIA 脚本去重**：`verify-layout.ps1` 与 `verify-toggle.ps1` 的窗口查找/树遍历确有重复，但规模小且职责清楚；发布流水线稳定后再抽公共模块。
3. **模型向上三处探测与 WebViewHost 名称整理**：当前实现足够直观；只需未来把模型枚举移入可容错的 catalog，并在 WebView 所有权重构后决定删除还是深化类名，没必要先做纯命名重构。

## 已确认的 Bug 或隐患

- `ServerArgumentBuilder.cs:14` 忽略 `ServerSettings.Threads`；Flash/Fit/Reasoning 的 `off` 与当前二进制默认值不等价。
- `ServerArgumentBuilder.cs:85` 未发出 `--models-dir`，`ModelsDirectory` 全程未使用，“最大模型数”当前没有完整 Router 语义。
- `ShellViewModel.cs:179,384` 的 fire-and-forget 停止无异常边界、无 Stopping 状态；`HandleProcessExit` 在 `_stopRequested` 分支也未把 `_state` 设为 Stopped。
- `ShellViewModel.cs:348-365` 的轮询无 CancellationToken/会话隔离，并在健康成功后停止读取日志。
- `WebViewHost.cs:9-18` 与 `CompositionRoot.cs:102-128` 各自初始化 WebView2；前者创建的 environment 未用于控件，且初始化错误日志不会进入 `LogViewModel`。
- 现有 25 测试的分布本身不失衡，但覆盖重点仍停留在纯函数和 happy path，尚不能证明新 UI 参数或生命周期时序正确。
