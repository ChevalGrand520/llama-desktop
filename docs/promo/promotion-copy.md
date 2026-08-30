# Llama Desktop 宣传文案（v1.1.0）

> 发帖时请把 `（此处插入截图）` 替换为上传后的截图。
> 截图文件：`docs/screenshots/llama-desktop-v1.1.png`

---

## 一、中文版（V2EX / 掘金）

### 标题

开源了一个零依赖便携的 llama.cpp 桌面壳：开包即用、内嵌聊天、自动调参

### 正文

给手里已经有 llama.cpp 便携包、或者一堆 GGUF 模型的朋友：

**Llama Desktop** —— 一个 Windows 绿色便携的 llama.cpp 桌面管理壳，解决「双击 → 选模型 → 聊天」这件事。

#### 为什么做

市面上跑本地 LLM 的工具不少（LM Studio、Ollama、Jan），但要么闭源、要么自带引擎、要么要装 Python/Node。我只想用现成的 llama-server.exe 和 GGUF 文件，双击就能聊，不想装一堆东西。

#### 特点

- **开包即用**：self-contained .NET 8 单文件夹，无需 Python / Node / .NET SDK，解压双击即用
- **内嵌聊天**：WebView2 直接内嵌 llama-server 官方聊天页，不用切浏览器
- **推理参数面板**：GPU 层数、上下文、线程、Flash Attention、Fit 自动显存适配、KV 量化、Thinking 开关，所见即所得
- **多模态支持**：自动识别同目录的 mmproj 视觉投影器，选中主模型即自动配对、启动时自动加载
- **自动调参**：检测 GPU/显存，`--fit` 自动分层，显存不够时降级 CPU
- **安全生命周期**：PID 身份校验 + 三阶段停止（优雅→进程树→强制），绝不误杀其他 llama 实例
- **中文界面** + 实时日志尾读
- 27 项自动化测试 + 独立代码审查

#### 技术栈

C# 12 / .NET 8 WPF / WebView2，零第三方运行时依赖。

#### 链接

- GitHub：https://github.com/ChevalGrand520/llama-desktop
- Release（含 CUDA 版 llama-server）：https://github.com/ChevalGrand520/llama-desktop/releases

（此处插入截图）

欢迎 star 和反馈。模型需自备（放入 `models\` 目录即可，便携包不含模型权重）。

---

## 二、英文版（r/LocalLLaMA / Show HN）

### 标题

Show HN: Llama Desktop — a portable zero-dependency llama.cpp GUI shell (embed the official Web UI)

### 正文

I built **Llama Desktop**, a Windows green/portable desktop shell for llama.cpp. If you already have a `llama-server.exe` and GGUF weights, this gives you double-click → pick model → chat, with no Python/Node/.NET SDK installs.

#### Why

LM Studio is closed-source and bundles its own engine; Ollama/Jan use their own runtimes; most llama.cpp GUIs need Python. I just wanted a portable shell around the llama-server I already had.

#### Highlights

- **Zero dependency**: self-contained .NET 8 single-folder build — unzip and run
- **Embedded chat**: WebView2 hosts the official llama-server Web UI in-app, no browser tab
- **Inference panel**: GPU layers, context, threads, Flash Attention, `--fit` auto-offload, KV cache quantization, reasoning mode — WYSIWYG
- **Multimodal**: auto-detects same-directory `mmproj` projector, pairs and loads it when you pick the base model
- **Safe lifecycle**: PID identity check + three-phase stop (graceful → process tree → forced), never kills by process name
- **Chinese + English UI**, live log tailing, 27 automated tests

#### Stack

C# 12 / .NET 8 WPF / WebView2. No third-party runtime.

#### Links

- GitHub: https://github.com/ChevalGrand520/llama-desktop
- Release (bundles CUDA llama-server): https://github.com/ChevalGrand520/llama-desktop/releases

（此处插入截图）

Stars and feedback welcome. Weights are self-hosted (drop them in `models\`); the portable zip intentionally excludes them.
