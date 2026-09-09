# Llama Desktop 社区发布包（Wave 1 预置 · 2026-09-02）

> 定位：Show HN / r/LocalLLaMA / V2EX / 掘金 首轮发布的「单一事实来源」。
> 与 `promotion-copy.md`（旧 v1.1.0 草稿）的关系：本文为修订定稿，发布一律以此为准；旧文件保留作历史。
> 状态：1★ / v1.1.0 下载 18 / 0 fork；winget #428122 校验全绿待人工 merge；Scoop bucket 已上线；awesome-ml #77 待审。

---

## 0. 发布前提 Gate（全部打勾才发 Wave 1）

- [ ] **工程线：README「Router 多模型」声明降级为 Roadmap 或实现 `--models-dir`**（唯一会被当场打脸的点，硬门槛）
- [ ] 截图/录屏：v1.1.0 真实截图 2–3 张（①聊天窗内嵌官方 Web UI ②侧栏推理参数面板 ③多模态 mmproj 配对）或 15–30s 录屏 GIF（16:9）
- [ ] 英文 README 首屏通读一遍（当前双语但中文优先）
- [ ] release 正文双语 + SHA-256（低优先、不阻塞）
- [ ] 口径统一：任何帖子出现「Router/多模型热切换」前，先确认 README 已同步

## 1. 核心叙事（一句话 + 三支柱 + 诚实边界）

**一句话**：给 llama.cpp 便携党一个零安装的「双击 → 选模型 → 聊天」桌面壳，直接复用官方 llama-server 与其官方 Web UI。

三支柱：
1. **复用官方运行时与官方 UI**——不魔改 llama.cpp 语义、不自造聊天界面（区别于 koboldcpp 等 fork UI）
2. **绿色单文件夹 + 安全进程生命周期**——PID 身份校验 + 三阶段停止，绝不按进程名误杀其他 llama 实例
3. **硬件自动档位 + mmproj 多模态自动配对**——检测 GPU/显存给默认参数，`--fit` 自动分层；视觉模型一键配对投影器

诚实边界（主动声明，避免第一波评论翻车）：
- 仅 Windows 10/11 x64 + NVIDIA CUDA（纯 CPU 可用但慢）；无 AMD/ARM/macOS/Linux
- 模型权重自备（`models\` 目录）；zip 约 609 MB（含 CUDA 版 llama-server 运行时）
- 当前单模型启动；多模型 Router 在路线图（若 Gate 0 未实现，文案一律不提）

## 2. 渠道矩阵与排期

| 渠道 | 形式 | 要点 | 建议时点 |
|---|---|---|---|
| **V2EX**（中文主场） | 「分享创造」节点文本帖 | 标题 ≤ 50 字；正文用 §3 中文版 + 截图；放出 GitHub/Release 链接；中文社区竞品空位最大 | Wave 1 Day 1（周二~周四，避开周末） |
| **Show HN** | `Show HN:` 前缀标题 ≤ 80 字符 | 正文放链接与截图即可，克制；HN 对「新仓库低分」不设门槛但评论会审 Router 之类声明 | Day 1 或 Day 2（美东 9–11am） |
| **r/LocalLLaMA** | 文本帖（勿用 Show HN 前缀） | 自带 zip 直链 + 截图 + 诚实边界；英文 README 就绪后发 | Day 2 |
| **掘金/知乎**（长文，传播杠杆最高） | 技术向专栏 | 题目建议：《怎么把官方 llama-server 变成可管理的 Windows 桌面壳——参数透传与三阶段停止协议》 | Day 3+ |
| X/Bluesky（可选） | 短帖 + 3 连截图 | @llamacpp 生态账号 | 随 Wave 1 |

## 3. 文案定稿 v2

### 3.1 中文（V2EX）
标题：开源一个零依赖便携的 llama.cpp 桌面壳：内嵌官方 Web UI、自动调参、安全停止

正文：
> 给手里已经有 llama.cpp 便携包、或者一堆 GGUF 模型的朋友：**Llama Desktop** —— Windows 绿色便携的 llama.cpp 桌面管理壳，解决「双击 → 选模型 → 聊天」。
>
> 市面工具不是闭源（LM Studio）、就是自带引擎（Ollama/Jan），或者要装 Python/Node。我只想管理我已有的 `llama-server.exe` 和 GGUF，不想再装一套东西——所以做了这个壳。
>
> **特点**
> - 开包即用：.NET 8 self-contained 单文件夹，无 Python/Node/.NET SDK，解压双击即用
> - 内嵌聊天：WebView2 直接内嵌 llama-server **官方 Web UI**，不切浏览器、不重复造聊天界面
> - 推理参数面板：GPU 层数/上下文/线程/Flash Attention/Fit 自动显存适配/KV 量化/Thinking，所见即所得
> - 多模态：自动配对同目录 mmproj，选中模型即带视觉
> - 硬件检测自动给默认档位；`--fit` 显存不够自动降级
> - 安全生命周期：PID 身份校验 + 三阶段停止（优雅→进程树→强制），绝不误杀别的 llama 进程
> - 中文界面 + 实时日志；40+ 自动化测试
>
> **安装**：Releases 下 `llama-desktop-win-x64.zip`；或
> `scoop bucket add llama-desktop https://github.com/ChevalGrand520/scoop-llama-desktop && scoop install llama-desktop`（winget `ChevalGrand520.LlamaDesktop` 审核中）
>
> **边界**：仅 Windows x64 + NVIDIA CUDA；模型需自备放入 `models\`；zip 约 609 MB 含 CUDA 运行时。
>
> 链接：https://github.com/ChevalGrand520/llama-desktop （截图见评论区）
>
> 欢迎 star / issue / PR。多模型 Router、CPU-only 小包在路线图上。

### 3.2 英文（Show HN 变体）
标题（≤80 字符，供选）：
- `Show HN: Llama Desktop – portable zero-dependency shell for llama.cpp (Windows)`
- `Show HN: A portable Windows shell for llama.cpp's official llama-server + Web UI`

正文：
> Llama Desktop is a green/portable Windows shell around the **official** llama.cpp `llama-server.exe` and its official Web UI — double-click → pick a GGUF model → chat, no Python/Node/.NET installs, no second engine.
>
> Why I built it: LM Studio is closed-source, Ollama/Jan bundle their own runtimes, and most llama.cpp GUIs need Python or fork the engine. I wanted a managed shell over the llama-server I already keep, with llama.cpp's own semantics and UI preserved.
>
> Highlights
> - Zero dependency: self-contained .NET 8 single-folder build (unzip and run)
> - Embedded official Web UI via WebView2 — no browser tab, no re-implemented chat
> - Inference panel: GPU layers / context / threads / FA / KV-cache quantization / reasoning, WYSIWYG
> - Hardware-aware defaults + llama.cpp `--fit` auto offload; mmproj multimodal auto-pairing
> - PID-identity-checked 3-phase stop (graceful → tree → force); never kills unrelated llama processes
> - EN/中文 UI, live log tailing, 40+ automated tests
>
> Honest scope: Windows 10/11 x64, NVIDIA CUDA build (~609 MB zip); CPU-only works but is slow; bring your own GGUF (drop into `models\`).
>
> GitHub: https://github.com/ChevalGrand520/llama-desktop — Releases zip, or `scoop install llama-desktop` via our bucket. Feedback/issues welcome.

### 3.3 r/LocalLLaMA 变体
改用「I made a …」口吻，标题如：`Llama Desktop: a portable Windows shell that embeds llama.cpp's official server Web UI`；正文同 3.2，首行补「For people who already run llama-server/llama.cpp portable builds and keep GGUF files.」

## 4. 常见问题弹药（发布后直接复用）

- **vs LM Studio**：它们闭源且自带引擎；这里直接管理你已有的官方 llama-server/GGUF，全部参数透传、行为与上游一致。
- **vs Ollama/Jan**：不引入第二套运行时；忠于 llama.cpp 命令行语义。
- **vs koboldcpp**：它是 llama.cpp 的魔改单文件 + 自带 UI；本壳用**官方** server 与**官方** Web UI。
- **为什么 609 MB**：主体是 CUDA 运行库（约 624 MB 中的大头）。未来做 CPU-only 小包。
- **会不会误杀我的进程**：进程启动时记录 PID，停止前校验 PID+命令行身份一致才动手；从不按进程名杀。
- **多模型？**：当前每次启动单模型；Router/`--models-dir` 在路线图（与 README 同步口径）。
- **模型哪里下**：Hugging Face GGUF（如 Qwen/Llama 量化版）放入 `models\`。
- **为什么还要 model 自备**：权重 400MB~几十 GB，不适合随 zip 分发。

## 5. 指标与跟踪

- Wave 1 后 2 周目标：star ≥ 30（现 1）、v1.1.0 下载 ≥ 100（现 18）、≥3 外部 issue/讨论、收录 ≥2（winget 合入 + awesome-ml 合入各算 1）
- 4 周未达标 → 停社区投入，转向功能补强 + v1.2 再发一轮（见主计划「止损」节）

## 6. 下一步待办（跨会话交接）

1. 工程线：Gate 0（Router 表述）、CI/徽章、新截图 → 回填 Gate 清单
2. 本宣传线：Gate 打勾后按 §2 排期发 Wave 1（发帖属公开动作，发布前由用户最终确认）
3. 定时复查：winget #428122（待 MS 维护者 merge）、awesome-ml #77（待审）、2026-09-28 后 awesome-ai PR、成熟后 Scoop Package Request
