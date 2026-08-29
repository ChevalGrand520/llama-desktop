# Llama Desktop MVP Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the Llama Desktop MVP — a Windows x64 C# WPF + WebView2 shell that manages the bundled `llama-server.exe` in single-model mode, embeds its official Web UI, and provides persisted settings, lifecycle state machine, health probing, log tailing, and clean stop, plus fix the legacy PowerShell launcher's model scan regression.

**Architecture:** Three-layer split. `LlamaDesktop.Core` holds pure models, validation, argument construction, and the lifecycle state machine (no WPF/IO). `LlamaDesktop.Infrastructure` owns Windows/IO details (process, ports, HTTP health, config persistence, UTF-8 log reading). `LlamaDesktop.App` is the WPF shell with a manually-assembled Composition Root, MVVM ViewModels, and a WebView2 adapter behind a navigation allowlist. The legacy PowerShell launcher stays functional and is fixed for recursive model scanning.

**Tech Stack:** C# 12, .NET 8 (`net8.0-windows`), WPF, Microsoft.Web.WebView2, xUnit for unit tests, self-contained `win-x64` folder publish. Build machine requires .NET 8 SDK (installed via winget); user machines require only WebView2 Evergreen Runtime.

## Global Constraints

- Target only Windows x64; framework `net8.0-windows`.
- Application root is always `Path.GetFullPath(AppContext.BaseDirectory)`; never use `Environment.CurrentDirectory` for locating `llama-server.exe`, models, or legacy config.
- Managed executable is exactly `llama-server.exe` in the application root; never recursively pick an arbitrary same-name executable.
- Model discovery is recursive over `models\**\*.gguf`; ignore `Modelfile.*`, `.json`, `.md`.
- Default model = saved model if it still exists, else first sorted recursively-found `.gguf`.
- Defaults: host `127.0.0.1`, port `8080`, `autoSelectPort=true`, gpuLayers `all` (capability-gated), context `8192`, threads = logical processor count, batch `2048`, parallel `1`, flashAttention `on` (capability-gated), fitMode `on` (capability-gated), cacheTypeK/V `q8_0`, reasoningMode `off`, maxPredict `8192`, modelsMax `1`.
- Health probe interval 1 s, per-attempt timeout 1 s, no overall model-loading deadline.
- Only the PID started by this instance may be stopped; never kill by process name.
- Stop escalation: graceful (CloseMainWindow) → 3 s wait → `taskkill /T` (soft) → 3 s wait → `taskkill /T /F` (hard) → stop-failed state; each taskkill helper has a 5 s watchdog.
- Config lives at `%LOCALAPPDATA%\LlamaDesktop\config.json` (v2 schema), atomic write via temp file + replace; contains no secrets.
- Sensitive/managed extra arguments (`--api-key`, `--api-key-file`, `--hf-token`, `--path`, `--log-disable`, plus all managed flags) are rejected in both `--flag value` and `--flag=value` forms before config save and before start.
- Non-loopback host → WebView2 does not embed; native placeholder + "open in browser" button.
- WebView2 navigation allowlist: loopback origins only (`127.0.0.1`, `localhost`, `[::1]`); external/new-window navigation goes to the system browser; DevTools disabled in release.
- `--log-file` used with incremental byte-offset UTF-8 tailing; if capability probe shows no `--log-file`, fall back to stdout/stderr capture.
- The workspace was not a Git repository at plan time; the user has git/gh available, so each task includes a Commit step after the initial `git init` (Task 1 Step 0).

---

## File Map

- Create `LlamaDesktop.sln`
- Create `src/LlamaDesktop.Core/LlamaDesktop.Core.csproj`
- Create `src/LlamaDesktop.Infrastructure/LlamaDesktop.Infrastructure.csproj`
- Create `src/LlamaDesktop.App/LlamaDesktop.App.csproj`
- Create `tests/LlamaDesktop.Core.Tests/LlamaDesktop.Core.Tests.csproj`
- Create `tests/LlamaDesktop.Infrastructure.Tests/LlamaDesktop.Infrastructure.Tests.csproj`
- Create `tests/LlamaDesktop.App.Tests/LlamaDesktop.App.Tests.csproj`
- Modify `LlamaLauncher.ps1` (Find-Models recursive fix)
- Modify `tests/Launcher.Smoke.Tests.ps1` (recursive scan assertion)
- Create `scripts/publish.ps1` and `scripts/verify-publish.ps1`
- Core source files listed in each task below.

### Task 1: Solution, Git, and Project Skeleton

**Files:**
- Create: `.gitignore`
- Create: `LlamaDesktop.sln`
- Create: `src/LlamaDesktop.Core/LlamaDesktop.Core.csproj`
- Create: `src/LlamaDesktop.Infrastructure/LlamaDesktop.Infrastructure.csproj`
- Create: `src/LlamaDesktop.App/LlamaDesktop.App.csproj`
- Create: `tests/LlamaDesktop.Core.Tests/LlamaDesktop.Core.Tests.csproj`
- Create: `tests/LlamaDesktop.Infrastructure.Tests/LlamaDesktop.Infrastructure.Tests.csproj`
- Create: `tests/LlamaDesktop.App.Tests/LlamaDesktop.App.Tests.csproj`

**Interfaces:**
- Consumes: nothing.
- Produces: a buildable solution and a Git repository. Later tasks add source files to these projects.

- [ ] **Step 0: Initialize the Git repository**

Run:
```powershell
git init
```

- [ ] **Step 1: Create `.gitignore`**

`.gitignore`:
```gitignore
bin/
obj/
dist/
.analysis/
.superpowers/
*.log
launcher-config.json
launcher-server.log
```
Commit: `git add .gitignore && git commit -m "chore: add gitignore"`

- [ ] **Step 2: Create the solution and project directories**

Run:
```powershell
New-Item -ItemType Directory -Force -Path src\LlamaDesktop.Core, src\LlamaDesktop.Infrastructure, src\LlamaDesktop.App, tests\LlamaDesktop.Core.Tests, tests\LlamaDesktop.Infrastructure.Tests, tests\LlamaDesktop.App.Tests | Out-Null
dotnet new sln -n LlamaDesktop
```

- [ ] **Step 3: Write the project files**

`src/LlamaDesktop.Core/LlamaDesktop.Core.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>12</LangVersion>
  </PropertyGroup>
</Project>
```

`src/LlamaDesktop.Infrastructure/LlamaDesktop.Infrastructure.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>12</LangVersion>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\LlamaDesktop.Core\LlamaDesktop.Core.csproj" />
  </ItemGroup>
</Project>
```

`src/LlamaDesktop.App/LlamaDesktop.App.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>12</LangVersion>
    <UseWPF>true</UseWPF>
    <EnableDefaultApplicationDefinition>false</EnableDefaultApplicationDefinition>
    <AssemblyName>LlamaDesktop</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Web.WebView2" Version="1.0.2903.40" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\LlamaDesktop.Core\LlamaDesktop.Core.csproj" />
    <ProjectReference Include="..\LlamaDesktop.Infrastructure\LlamaDesktop.Infrastructure.csproj" />
  </ItemGroup>
</Project>
```

Notes (discovered during Task 11): `EnableDefaultApplicationDefinition=false` is required because `UseWPF=true` would auto-treat `App.xaml` as an ApplicationDefinition and auto-generate a conflicting `Main()`; `AssemblyName=LlamaDesktop` keeps the published exe name aligned with the publish/verify scripts.

`tests/LlamaDesktop.Core.Tests/LlamaDesktop.Core.Tests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>12</LangVersion>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\LlamaDesktop.Core\LlamaDesktop.Core.csproj" />
  </ItemGroup>
</Project>
```

`tests/LlamaDesktop.Infrastructure.Tests/LlamaDesktop.Infrastructure.Tests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>12</LangVersion>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\LlamaDesktop.Infrastructure\LlamaDesktop.Infrastructure.csproj" />
  </ItemGroup>
</Project>
```

`tests/LlamaDesktop.App.Tests/LlamaDesktop.App.Tests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>12</LangVersion>
    <IsPackable>false</IsPackable>
    <UseWPF>true</UseWPF>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\LlamaDesktop.App\LlamaDesktop.App.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 4: Add projects to the solution and build**

Run:
```powershell
dotnet sln LlamaDesktop.sln add src\LlamaDesktop.Core\LlamaDesktop.Core.csproj src\LlamaDesktop.Infrastructure\LlamaDesktop.Infrastructure.csproj src\LlamaDesktop.App\LlamaDesktop.App.csproj tests\LlamaDesktop.Core.Tests\LlamaDesktop.Core.Tests.csproj tests\LlamaDesktop.Infrastructure.Tests\LlamaDesktop.Infrastructure.Tests.csproj tests\LlamaDesktop.App.Tests\LlamaDesktop.App.Tests.csproj
```

Create `src/LlamaDesktop.App/Program.cs`:
```csharp
namespace LlamaDesktop.App;
public static class Program
{
    [STAThread]
    public static void Main()
    {
    }
}
```

Run: `dotnet build LlamaDesktop.sln -c Release`
Expected: Build succeeded with 0 warnings/errors.

- [ ] **Step 5: Commit**

```bash
git add LlamaDesktop.sln src tests .gitignore
git commit -m "feat: scaffold Llama Desktop solution"
```

---

### Task 2: Fix Legacy Model Scan Regression

**Files:**
- Modify: `LlamaLauncher.ps1:78-82` (Find-Models)
- Modify: `tests/Launcher.Smoke.Tests.ps1`

**Interfaces:**
- Consumes: nothing.
- Produces: legacy launcher discovers `models\**\*.gguf` again.

- [ ] **Step 1: Write the failing smoke assertion**

Append to `tests/Launcher.Smoke.Tests.ps1`:
```powershell
$launcherSource = Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\LlamaLauncher.ps1') -Raw
if ($launcherSource -notmatch 'Get-ChildItem -LiteralPath \$directory -Filter ''\*\.gguf'' -Recurse') {
    throw 'Find-Models must scan recursively for .gguf under models\.'
}
if ($launcherSource -notmatch 'Modelfile') {
    throw 'Find-Models must exclude non-weight files such as Modelfile.'
}
```

- [ ] **Step 2: Run the smoke tests to verify they fail**

Run: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Launcher.Smoke.Tests.ps1`
Expected: FAIL with the recursive scan message.

- [ ] **Step 3: Fix Find-Models**

Replace the body of `Find-Models` in `LlamaLauncher.ps1` with:
```powershell
function Find-Models {
    $directory = Join-Path $PSScriptRoot 'models'
    if (-not (Test-Path -LiteralPath $directory -PathType Container)) { return @() }
    @(Get-ChildItem -LiteralPath $directory -Filter '*.gguf' -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -notlike 'Modelfile.*' } |
        Sort-Object Name | Select-Object -ExpandProperty FullName)
}
```

- [ ] **Step 4: Run the smoke tests to verify they pass**

Run: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Launcher.Smoke.Tests.ps1`
Expected: PASS.

- [ ] **Step 5: Run the core tests to confirm no regression**

Run: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Launcher.Core.Tests.ps1`
Expected: `Launcher.Core tests passed.`

- [ ] **Step 6: Commit**

```bash
git add LlamaLauncher.ps1 tests/Launcher.Smoke.Tests.ps1
git commit -m "fix: recursive model scan in legacy launcher"
```

---

### Task 3: Core Configuration Model and Validation

**Files:**
- Create: `src/LlamaDesktop.Core/Models/ServerSettings.cs`
- Create: `src/LlamaDesktop.Core/Models/LauncherConfig.cs`
- Create: `src/LlamaDesktop.Core/Validation/SettingsValidator.cs`
- Test: `tests/LlamaDesktop.Core.Tests/SettingsValidatorTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `ServerSettings` (record with all settings, `WithDefaults(int logicalProcessorCount, string defaultModel)` factory).
  - `LauncherConfig` (v2 schema record wrapping `ServerSettings` + `UiState`).
  - `SettingsValidator.Validate(ServerSettings) -> IReadOnlyList<ValidationIssue>` with `ValidationIssue { Code, Message }`.

- [ ] **Step 1: Write the failing tests**

`tests/LlamaDesktop.Core.Tests/SettingsValidatorTests.cs`:
```csharp
using LlamaDesktop.Core.Models;
using LlamaDesktop.Core.Validation;
using Xunit;

namespace LlamaDesktop.Core.Tests;

public class SettingsValidatorTests
{
    private static ServerSettings Defaults() =>
        ServerSettings.WithDefaults(logicalProcessorCount: 8, defaultModel: @"C:\models\a.gguf");

    [Fact]
    public void Defaults_Are_Valid()
    {
        var issues = SettingsValidator.Validate(Defaults());
        Assert.Empty(issues);
    }

    [Fact]
    public void Port_Out_Of_Range_Is_Rejected()
    {
        var s = Defaults() with { Port = 70000 };
        Assert.Contains(SettingsValidator.Validate(s), i => i.Code == "PortOutOfRange");
    }

    [Fact]
    public void Empty_Model_Is_Rejected()
    {
        var s = Defaults() with { ModelPath = "" };
        Assert.Contains(SettingsValidator.Validate(s), i => i.Code == "ModelRequired");
    }

    [Fact]
    public void Invalid_Host_Is_Rejected()
    {
        var s = Defaults() with { Host = "not a host!" };
        Assert.Contains(SettingsValidator.Validate(s), i => i.Code == "InvalidHost");
    }

    [Fact]
    public void Threads_Must_Be_Positive()
    {
        var s = Defaults() with { Threads = 0 };
        Assert.Contains(SettingsValidator.Validate(s), i => i.Code == "ThreadsOutOfRange");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests\LlamaDesktop.Core.Tests -c Release`
Expected: FAIL — `ServerSettings` and `SettingsValidator` do not exist.

- [ ] **Step 3: Implement the models and validator**

`src/LlamaDesktop.Core/Models/ServerSettings.cs`:
```csharp
namespace LlamaDesktop.Core.Models;

public sealed record ServerSettings
{
    public required string ModelPath { get; init; }
    public required string ModelsDirectory { get; init; }
    public required string Host { get; init; }
    public required int Port { get; init; }
    public required bool AutoSelectPort { get; init; }
    public required string GpuLayers { get; init; }
    public required int ContextSize { get; init; }
    public required int Threads { get; init; }
    public required int BatchSize { get; init; }
    public required int Parallel { get; init; }
    public required string FlashAttention { get; init; }
    public required string FitMode { get; init; }
    public required int FitTargetMiB { get; init; }
    public required string CacheTypeK { get; init; }
    public required string CacheTypeV { get; init; }
    public required string ReasoningMode { get; init; }
    public required int MaxPredict { get; init; }
    public required int ModelsMax { get; init; }
    public required string ExtraArguments { get; init; }

    public static ServerSettings WithDefaults(int logicalProcessorCount, string defaultModel) =>
        new()
        {
            ModelPath = defaultModel,
            ModelsDirectory = @"models",
            Host = "127.0.0.1",
            Port = 8080,
            AutoSelectPort = true,
            GpuLayers = "all",
            ContextSize = 8192,
            Threads = Math.Max(1, logicalProcessorCount),
            BatchSize = 2048,
            Parallel = 1,
            FlashAttention = "on",
            FitMode = "on",
            FitTargetMiB = 2048,
            CacheTypeK = "q8_0",
            CacheTypeV = "q8_0",
            ReasoningMode = "off",
            MaxPredict = 8192,
            ModelsMax = 1,
            ExtraArguments = "",
        };
}
```

`src/LlamaDesktop.Core/Models/LauncherConfig.cs`:
```csharp
namespace LlamaDesktop.Core.Models;

public sealed record UiState
{
    public bool LogDrawerOpen { get; init; }
    public double LeftPanelWidth { get; init; } = 300;
}

public sealed record LauncherConfig
{
    public const int SchemaVersion = 2;

    public int SchemaVersionValue { get; init; } = SchemaVersion;
    public required ServerSettings Settings { get; init; }
    public UiState Ui { get; init; } = new();
}
```

`src/LlamaDesktop.Core/Validation/SettingsValidator.cs`:
```csharp
using System.Net;
using LlamaDesktop.Core.Models;

namespace LlamaDesktop.Core.Validation;

public sealed record ValidationIssue(string Code, string Message);

public static class SettingsValidator
{
    public static IReadOnlyList<ValidationIssue> Validate(ServerSettings s)
    {
        var issues = new List<ValidationIssue>();

        if (string.IsNullOrWhiteSpace(s.ModelPath))
            issues.Add(new ValidationIssue("ModelRequired", "请选择有效的 GGUF 模型文件。"));

        if (s.Port is < 1 or > 65535)
            issues.Add(new ValidationIssue("PortOutOfRange", "端口必须在 1 到 65535 之间。"));

        if (string.IsNullOrWhiteSpace(s.Host) ||
            (!s.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) &&
             !IPAddress.TryParse(s.Host, out _)))
            issues.Add(new ValidationIssue("InvalidHost", "监听地址必须是 localhost 或有效的 IPv4/IPv6 地址。"));

        if (s.Threads < 1)
            issues.Add(new ValidationIssue("ThreadsOutOfRange", "CPU 线程数必须是大于 0 的整数。"));

        if (s.ContextSize < 1)
            issues.Add(new ValidationIssue("ContextOutOfRange", "上下文长度必须是大于 0 的整数。"));

        if (s.BatchSize < 1)
            issues.Add(new ValidationIssue("BatchOutOfRange", "批大小必须是大于 0 的整数。"));

        if (s.Parallel < 1)
            issues.Add(new ValidationIssue("ParallelOutOfRange", "并行请求数必须是大于 0 的整数。"));

        if (s.MaxPredict < 1)
            issues.Add(new ValidationIssue("MaxPredictOutOfRange", "生成上限必须是大于 0 的整数。"));

        if (s.ModelsMax < 1)
            issues.Add(new ValidationIssue("ModelsMaxOutOfRange", "最大模型数必须是大于 0 的整数。"));

        return issues;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests\LlamaDesktop.Core.Tests -c Release`
Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```bash
git add src/LlamaDesktop.Core tests/LlamaDesktop.Core.Tests
git commit -m "feat: core settings model and validator"
```

---

### Task 4: Core Argument Construction and Extra Argument Policy

**Files:**
- Create: `src/LlamaDesktop.Core/Arguments/WindowsArgumentQuoter.cs`
- Create: `src/LlamaDesktop.Core/Arguments/ServerArgumentBuilder.cs`
- Create: `src/LlamaDesktop.Core/Validation/ExtraArgumentPolicy.cs`
- Create: `src/LlamaDesktop.Core/Models/CapabilitySnapshot.cs`
- Test: `tests/LlamaDesktop.Core.Tests/ArgumentBuilderTests.cs`

**Interfaces:**
- Consumes: `ServerSettings` (Task 3).
- Produces:
  - `WindowsArgumentQuoter.Quote(IReadOnlyList<string> args) -> string`.
  - `CapabilitySnapshot` (record with bool flags; `Unknown` default via static `CapabilitySnapshot.Unknown`, `Full` for MVP).
  - `ServerArgumentBuilder.Build(ServerSettings, string logPath, CapabilitySnapshot caps, int logicalProcessors) -> string[]`.
  - `ExtraArgumentPolicy.Validate(string[] extra, out IReadOnlyList<string> errors)`.

- [ ] **Step 1: Write the failing tests**

`tests/LlamaDesktop.Core.Tests/ArgumentBuilderTests.cs`:
```csharp
using LlamaDesktop.Core.Arguments;
using LlamaDesktop.Core.Models;
using LlamaDesktop.Core.Validation;
using Xunit;

namespace LlamaDesktop.Core.Tests;

public class ArgumentBuilderTests
{
    private static ServerSettings Settings() =>
        ServerSettings.WithDefaults(logicalProcessorCount: 8, defaultModel: @"C:\model files\x.gguf") with
        {
            Port = 18080, BatchSize = 1024, Parallel = 2, ModelsMax = 1,
        };

    [Fact]
    public void Windows_Quoting_Handles_Spaces_Quotes_And_Empty()
    {
        var q = WindowsArgumentQuoter.Quote(new[] { "plain", @"C:\model files\x.gguf", "a\"b", "" });
        Assert.Equal("plain \"C:\\model files\\x.gguf\" \"a\\\"b\" \"\"", q);
    }

    [Fact]
    public void Build_Produces_Single_Model_Arguments()
    {
        var args = ServerArgumentBuilder.Build(Settings(), @"C:\tmp\server.log",
            CapabilitySnapshot.Full, logicalProcessors: 8);
        Assert.Contains("--model", args);
        Assert.Contains(@"C:\model files\x.gguf", args);
        Assert.Contains("--port", args);
        Assert.Contains("18080", args);
        Assert.Contains("--log-file", args);
        Assert.Contains("--ctx-size", args);
        Assert.Contains("8192", args);
    }

    [Fact]
    public void Build_Gates_Unsupported_Flags()
    {
        var caps = CapabilitySnapshot.Unknown with { LogFile = false, Fit = false, HealthEndpoint = true };
        var args = ServerArgumentBuilder.Build(Settings(), @"C:\tmp\server.log", caps, logicalProcessors: 8);
        Assert.DoesNotContain("--log-file", args);
        Assert.DoesNotContain("--fit", args);
    }

    [Fact]
    public void Extra_Arguments_Reject_Managed_And_Sensitive()
    {
        Assert.False(ExtraArgumentPolicy.Validate(new[] { "--port", "9999" }, out var e1) || e1.Count == 0);
        Assert.False(ExtraArgumentPolicy.Validate(new[] { "--api-key=secret" }, out var e2) || e2.Count == 0);
        Assert.False(ExtraArgumentPolicy.Validate(new[] { "--log-disable" }, out var e3) || e3.Count == 0);
        Assert.True(ExtraArgumentPolicy.Validate(new[] { "--verbose" }, out var e4) && e4.Count == 0);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests\LlamaDesktop.Core.Tests -c Release`
Expected: FAIL — types missing.

- [ ] **Step 3: Implement the argument layer**

`src/LlamaDesktop.Core/Arguments/WindowsArgumentQuoter.cs`:
```csharp
using System.Text;

namespace LlamaDesktop.Core.Arguments;

public static class WindowsArgumentQuoter
{
    public static string Quote(IReadOnlyList<string> args)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < args.Count; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append(QuoteOne(args[i]));
        }
        return sb.ToString();
    }

    private static string QuoteOne(string arg)
    {
        if (arg.Length == 0) return "\"\"";
        if (arg.All(c => !char.IsWhiteSpace(c) && c != '"')) return arg;
        var sb = new StringBuilder("\"");
        var backslashes = 0;
        foreach (var c in arg)
        {
            if (c == '\\') { backslashes++; continue; }
            if (c == '"')
            {
                sb.Append('\\', backslashes * 2 + 1);
                sb.Append('"');
                backslashes = 0;
                continue;
            }
            sb.Append('\\', backslashes);
            backslashes = 0;
            sb.Append(c);
        }
        sb.Append('\\', backslashes * 2);
        sb.Append('"');
        return sb.ToString();
    }
}
```

`src/LlamaDesktop.Core/Models/CapabilitySnapshot.cs`:
```csharp
namespace LlamaDesktop.Core.Models;

public sealed record CapabilitySnapshot
{
    public bool WebUi { get; init; }
    public bool ModelsDir { get; init; }
    public bool ModelsMax { get; init; }
    public bool ModelsPreset { get; init; }
    public bool Fit { get; init; }
    public bool FitTarget { get; init; }
    public bool FlashAttnValueSyntax { get; init; }
    public bool CacheTypeK { get; init; }
    public bool CacheTypeV { get; init; }
    public bool Jinja { get; init; }
    public bool Reasoning { get; init; }
    public bool Metrics { get; init; }
    public bool Slots { get; init; }
    public bool LoadMode { get; init; }
    public bool LogFile { get; init; }
    public bool HealthEndpoint { get; init; }
    public bool V1ModelsEndpoint { get; init; }
    public bool GpuLayersAllKeyword { get; init; }

    public static CapabilitySnapshot Unknown { get; } = new();
    public static CapabilitySnapshot Full { get; } = new()
    {
        WebUi = true, ModelsDir = true, ModelsMax = true, ModelsPreset = true, Fit = true,
        FitTarget = true, FlashAttnValueSyntax = true, CacheTypeK = true, CacheTypeV = true,
        Jinja = true, Reasoning = true, Metrics = true, Slots = true, LoadMode = true,
        LogFile = true, HealthEndpoint = true, V1ModelsEndpoint = true, GpuLayersAllKeyword = true,
    };
}
```

`src/LlamaDesktop.Core/Arguments/ServerArgumentBuilder.cs`:
```csharp
using LlamaDesktop.Core.Models;

namespace LlamaDesktop.Core.Arguments;

public static class ServerArgumentBuilder
{
    public static string[] Build(ServerSettings s, string logPath, CapabilitySnapshot caps, int logicalProcessors)
    {
        var args = new List<string>
        {
            "--host", s.Host,
            "--port", s.Port.ToString(),
            "-t", Math.Max(1, logicalProcessors).ToString(),
        };

        if (caps.LogFile)
        {
            args.Add("--log-file");
            args.Add(logPath);
        }

        args.Add("--model");
        args.Add(s.ModelPath);

        if (caps.WebUi)
        {
            // --ui is a boolean switch (no value); "--ui on" would fail with "invalid argument: on".
            args.Add("--ui");
        }

        args.Add("--ctx-size");
        args.Add(s.ContextSize.ToString());
        args.Add("--batch-size");
        args.Add(s.BatchSize.ToString());
        args.Add("--parallel");
        args.Add(s.Parallel.ToString());

        // When fit is active, omit --gpu-layers so llama-server can auto-partition layers
        // across devices; passing both makes fit abort ("n_gpu_layers already set by user").
        if (caps.Fit && s.FitMode == "on")
        {
            args.Add("--fit");
            args.Add("on");
            if (caps.FitTarget)
            {
                args.Add("--fit-target");
                args.Add(s.FitTargetMiB.ToString());
            }
        }
        else if (caps.GpuLayersAllKeyword && s.GpuLayers == "all")
        {
            args.Add("--gpu-layers");
            args.Add("all");
        }
        else if (int.TryParse(s.GpuLayers, out var layers))
        {
            args.Add("--gpu-layers");
            args.Add(layers.ToString());
        }

        if (caps.FlashAttnValueSyntax && !s.FlashAttention.Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            args.Add("--flash-attn");
            args.Add(s.FlashAttention.ToLowerInvariant());
        }

        if (caps.CacheTypeK && s.CacheTypeK != "f16")
        {
            args.Add("--cache-type-k");
            args.Add(s.CacheTypeK);
        }

        if (caps.CacheTypeV && s.CacheTypeV != "f16")
        {
            args.Add("--cache-type-v");
            args.Add(s.CacheTypeV);
        }

        if (caps.Reasoning && s.ReasoningMode == "on")
        {
            args.Add("--reasoning");
            args.Add("on");
        }

        if (caps.ModelsDir && s.ModelsMax > 1)
        {
            args.Add("--models-max");
            args.Add(s.ModelsMax.ToString());
        }

        args.Add("-n");
        args.Add(s.MaxPredict.ToString());

        if (!string.IsNullOrWhiteSpace(s.ExtraArguments))
        {
            foreach (var part in s.ExtraArguments.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                args.Add(part);
        }

        return args.ToArray();
    }
}
```

`src/LlamaDesktop.Core/Validation/ExtraArgumentPolicy.cs`:
```csharp
namespace LlamaDesktop.Core.Validation;

public static class ExtraArgumentPolicy
{
    private static readonly HashSet<string> Managed = new(StringComparer.OrdinalIgnoreCase)
    {
        "--model", "--models-dir", "--host", "--port", "--log-file", "--gpu-layers", "--fit",
        "--fit-target", "--ctx-size", "-c", "--threads", "-t", "--batch-size", "--parallel",
        "--ui", "--no-ui", "--models-preset", "--models-max", "--cache-type-k", "--cache-type-v",
        "--reasoning", "--n-predict", "-n",
    };

    private static readonly HashSet<string> SensitiveOrDangerous = new(StringComparer.OrdinalIgnoreCase)
    {
        "--api-key", "--api-key-file", "--hf-token", "--path", "--log-disable",
    };

    public static bool Validate(string[] extra, out IReadOnlyList<string> errors)
    {
        var list = new List<string>();
        foreach (var raw in extra)
        {
            var flag = raw.Split('=', 2)[0];
            if (SensitiveOrDangerous.Contains(flag))
                list.Add($"参数 {flag} 被禁止（敏感或危险选项）。");
            else if (Managed.Contains(flag))
                list.Add($"参数 {flag} 受启动器管理，不能在额外参数中重复指定。");
        }
        errors = list;
        return list.Count == 0;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests\LlamaDesktop.Core.Tests -c Release`
Expected: PASS (all tests).

- [ ] **Step 5: Commit**

```bash
git add src/LlamaDesktop.Core tests/LlamaDesktop.Core.Tests
git commit -m "feat: core argument builder and extra argument policy"
```

---

### Task 5: Core Lifecycle State Machine

**Files:**
- Create: `src/LlamaDesktop.Core/Models/ServerLifecycleState.cs`
- Create: `src/LlamaDesktop.Core/Services/ServerLifecycleStateMachine.cs`
- Test: `tests/LlamaDesktop.Core.Tests/StateMachineTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `ServerLifecycleState` enum and `ServerLifecycleStateMachine` with `CanTransition(from, to)` and `IsTerminal(state)`.

- [ ] **Step 1: Write the failing tests**

`tests/LlamaDesktop.Core.Tests/StateMachineTests.cs`:
```csharp
using LlamaDesktop.Core.Models;
using LlamaDesktop.Core.Services;
using Xunit;

namespace LlamaDesktop.Core.Tests;

public class StateMachineTests
{
    private readonly ServerLifecycleStateMachine _m = new();

    [Theory]
    [InlineData(ServerLifecycleState.Stopped, ServerLifecycleState.Inspecting)]
    [InlineData(ServerLifecycleState.Inspecting, ServerLifecycleState.ReadyToStart)]
    [InlineData(ServerLifecycleState.ReadyToStart, ServerLifecycleState.StartingProcess)]
    [InlineData(ServerLifecycleState.StartingProcess, ServerLifecycleState.WaitingForUi)]
    [InlineData(ServerLifecycleState.WaitingForUi, ServerLifecycleState.UiReady)]
    [InlineData(ServerLifecycleState.UiReady, ServerLifecycleState.Running)]
    [InlineData(ServerLifecycleState.Running, ServerLifecycleState.StoppingGraceful)]
    [InlineData(ServerLifecycleState.StoppingGraceful, ServerLifecycleState.StoppingSoft)]
    [InlineData(ServerLifecycleState.StoppingSoft, ServerLifecycleState.StoppingForced)]
    [InlineData(ServerLifecycleState.StoppingForced, ServerLifecycleState.StopFailed)]
    [InlineData(ServerLifecycleState.StoppingForced, ServerLifecycleState.Stopped)]
    [InlineData(ServerLifecycleState.Running, ServerLifecycleState.Failed)]
    [InlineData(ServerLifecycleState.Running, ServerLifecycleState.Detached)]
    public void Allowed_Transitions_Are_Valid(ServerLifecycleState from, ServerLifecycleState to)
    {
        Assert.True(_m.CanTransition(from, to));
    }

    [Theory]
    [InlineData(ServerLifecycleState.Stopped, ServerLifecycleState.Running)]
    [InlineData(ServerLifecycleState.ReadyToStart, ServerLifecycleState.Running)]
    [InlineData(ServerLifecycleState.Failed, ServerLifecycleState.Running)]
    public void Forbidden_Transitions_Are_Invalid(ServerLifecycleState from, ServerLifecycleState to)
    {
        Assert.False(_m.CanTransition(from, to));
    }

    [Fact]
    public void Terminal_States_Are_Recognized()
    {
        Assert.True(_m.IsTerminal(ServerLifecycleState.Stopped));
        Assert.True(_m.IsTerminal(ServerLifecycleState.Failed));
        Assert.True(_m.IsTerminal(ServerLifecycleState.StopFailed));
        Assert.True(_m.IsTerminal(ServerLifecycleState.Detached));
        Assert.False(_m.IsTerminal(ServerLifecycleState.Running));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests\LlamaDesktop.Core.Tests -c Release`
Expected: FAIL — types missing.

- [ ] **Step 3: Implement the state machine**

`src/LlamaDesktop.Core/Models/ServerLifecycleState.cs`:
```csharp
namespace LlamaDesktop.Core.Models;

public enum ServerLifecycleState
{
    Stopped,
    Inspecting,
    ConfigurationError,
    ReadyToStart,
    StartingProcess,
    WaitingForUi,
    UiReady,
    Running,
    StoppingGraceful,
    StoppingSoft,
    StoppingForced,
    StopFailed,
    Failed,
    Detached,
    ExternalServiceDetected,
    ExternalConnected,
    ExternalDisconnected,
}
```

`src/LlamaDesktop.Core/Services/ServerLifecycleStateMachine.cs`:
```csharp
using LlamaDesktop.Core.Models;

namespace LlamaDesktop.Core.Services;

public sealed class ServerLifecycleStateMachine
{
    private static readonly Dictionary<ServerLifecycleState, HashSet<ServerLifecycleState>> Allowed = new()
    {
        [ServerLifecycleState.Stopped] = new HashSet<ServerLifecycleState> { ServerLifecycleState.Inspecting },
        [ServerLifecycleState.Inspecting] = new HashSet<ServerLifecycleState>
        {
            ServerLifecycleState.ReadyToStart,
            ServerLifecycleState.ConfigurationError,
            ServerLifecycleState.ExternalServiceDetected,
        },
        [ServerLifecycleState.ConfigurationError] = new HashSet<ServerLifecycleState> { ServerLifecycleState.Stopped },
        [ServerLifecycleState.ExternalServiceDetected] = new HashSet<ServerLifecycleState>
        {
            ServerLifecycleState.ExternalConnected,
            ServerLifecycleState.ReadyToStart,
        },
        [ServerLifecycleState.ExternalConnected] = new HashSet<ServerLifecycleState>
        {
            ServerLifecycleState.ExternalDisconnected,
            ServerLifecycleState.Stopped,
        },
        [ServerLifecycleState.ExternalDisconnected] = new HashSet<ServerLifecycleState>
        {
            ServerLifecycleState.ExternalConnected,
            ServerLifecycleState.ReadyToStart,
        },
        [ServerLifecycleState.ReadyToStart] = new HashSet<ServerLifecycleState>
        {
            ServerLifecycleState.StartingProcess,
            ServerLifecycleState.Stopped,
        },
        [ServerLifecycleState.StartingProcess] = new HashSet<ServerLifecycleState>
        {
            ServerLifecycleState.WaitingForUi,
            ServerLifecycleState.Failed,
        },
        [ServerLifecycleState.WaitingForUi] = new HashSet<ServerLifecycleState>
        {
            ServerLifecycleState.UiReady,
            ServerLifecycleState.Failed,
        },
        [ServerLifecycleState.UiReady] = new HashSet<ServerLifecycleState>
        {
            ServerLifecycleState.Running,
            ServerLifecycleState.Failed,
            ServerLifecycleState.Detached,
        },
        [ServerLifecycleState.Running] = new HashSet<ServerLifecycleState>
        {
            ServerLifecycleState.StoppingGraceful,
            ServerLifecycleState.Failed,
            ServerLifecycleState.Detached,
        },
        [ServerLifecycleState.StoppingGraceful] = new HashSet<ServerLifecycleState>
        {
            ServerLifecycleState.Stopped,
            ServerLifecycleState.StoppingSoft,
            ServerLifecycleState.StopFailed,
        },
        [ServerLifecycleState.StoppingSoft] = new HashSet<ServerLifecycleState>
        {
            ServerLifecycleState.Stopped,
            ServerLifecycleState.StoppingForced,
            ServerLifecycleState.StopFailed,
        },
        [ServerLifecycleState.StoppingForced] = new HashSet<ServerLifecycleState>
        {
            ServerLifecycleState.Stopped,
            ServerLifecycleState.StopFailed,
        },
        [ServerLifecycleState.StopFailed] = new HashSet<ServerLifecycleState> { ServerLifecycleState.Stopped },
        [ServerLifecycleState.Failed] = new HashSet<ServerLifecycleState> { ServerLifecycleState.Stopped },
        [ServerLifecycleState.Detached] = new HashSet<ServerLifecycleState> { ServerLifecycleState.Stopped },
    };

    public bool CanTransition(ServerLifecycleState from, ServerLifecycleState to) =>
        Allowed.TryGetValue(from, out var set) && set.Contains(to);

    public bool IsTerminal(ServerLifecycleState state) =>
        state is ServerLifecycleState.Stopped or ServerLifecycleState.Failed
            or ServerLifecycleState.StopFailed or ServerLifecycleState.Detached;
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests\LlamaDesktop.Core.Tests -c Release`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/LlamaDesktop.Core tests/LlamaDesktop.Core.Tests
git commit -m "feat: server lifecycle state machine"
```

---

### Task 6: Infrastructure Config Persistence and Legacy Import

**Files:**
- Create: `src/LlamaDesktop.Infrastructure/Persistence/JsonConfigStore.cs`
- Create: `src/LlamaDesktop.Infrastructure/Persistence/LegacyConfigImporter.cs`
- Test: `tests/LlamaDesktop.Infrastructure.Tests/ConfigStoreTests.cs`

**Interfaces:**
- Consumes: `LauncherConfig`, `ServerSettings` (Task 3).
- Produces:
  - `JsonConfigStore.Load() -> LauncherConfig?`, `Save(LauncherConfig)`; atomic write; corrupt/missing → `null` with log callback.
  - `LegacyConfigImporter.TryImport(string legacyPath) -> ServerSettings?`.

- [ ] **Step 1: Write the failing tests**

`tests/LlamaDesktop.Infrastructure.Tests/ConfigStoreTests.cs`:
```csharp
using LlamaDesktop.Core.Models;
using LlamaDesktop.Infrastructure.Persistence;
using Xunit;

namespace LlamaDesktop.Infrastructure.Tests;

public class ConfigStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ld-tests-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _logs = new();

    public ConfigStoreTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private string ConfigPath => Path.Combine(_dir, "config.json");

    [Fact]
    public void Missing_Config_Returns_Null()
    {
        var store = new JsonConfigStore(ConfigPath, _logs.Add);
        Assert.Null(store.Load());
    }

    [Fact]
    public void Save_Then_Load_RoundTrips()
    {
        var store = new JsonConfigStore(ConfigPath, _logs.Add);
        var cfg = new LauncherConfig
        {
            Settings = ServerSettings.WithDefaults(8, @"C:\m\x.gguf") with { Port = 18080 },
        };
        store.Save(cfg);
        var loaded = store.Load();
        Assert.NotNull(loaded);
        Assert.Equal(18080, loaded!.Settings.Port);
        Assert.Equal(@"C:\m\x.gguf", loaded.Settings.ModelPath);
    }

    [Fact]
    public void Corrupt_Config_Returns_Null_And_Logs()
    {
        File.WriteAllText(ConfigPath, "not json {{{");
        var store = new JsonConfigStore(ConfigPath, _logs.Add);
        Assert.Null(store.Load());
        Assert.NotEmpty(_logs);
    }

    [Fact]
    public void Legacy_Import_Maps_Compatible_Fields()
    {
        var legacy = Path.Combine(_dir, "launcher-config.json");
        File.WriteAllText(legacy, """{"ModelPath":"C:\\m\\x.gguf","Port":9090,"ContextSize":"bad","Threads":8}""");
        var imported = LegacyConfigImporter.TryImport(legacy);
        Assert.NotNull(imported);
        Assert.Equal(@"C:\m\x.gguf", imported!.ModelPath);
        Assert.Equal(9090, imported.Port);
        Assert.Equal(8192, imported.ContextSize); // invalid falls back to default
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests\LlamaDesktop.Infrastructure.Tests -c Release`
Expected: FAIL — types missing.

- [ ] **Step 3: Implement persistence**

`src/LlamaDesktop.Infrastructure/Persistence/JsonConfigStore.cs`:
```csharp
using System.Text.Json;
using LlamaDesktop.Core.Models;

namespace LlamaDesktop.Infrastructure.Persistence;

public sealed class JsonConfigStore
{
    private readonly string _path;
    private readonly Action<string> _log;

    public JsonConfigStore(string path, Action<string> log)
    {
        _path = path;
        _log = log;
    }

    public LauncherConfig? Load()
    {
        if (!File.Exists(_path)) return null;
        try
        {
            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<LauncherConfig>(json);
        }
        catch (Exception ex)
        {
            _log($"配置读取失败，已使用默认值：{ex.Message}");
            return null;
        }
    }

    public void Save(LauncherConfig config)
    {
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var temp = _path + ".tmp";
        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(temp, json);
        File.Move(temp, _path, overwrite: true);
    }
}
```

`src/LlamaDesktop.Infrastructure/Persistence/LegacyConfigImporter.cs`:
```csharp
using System.Text.Json;
using LlamaDesktop.Core.Models;

namespace LlamaDesktop.Infrastructure.Persistence;

public static class LegacyConfigImporter
{
    public static ServerSettings? TryImport(string legacyPath)
    {
        if (!File.Exists(legacyPath)) return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(legacyPath));
            var root = doc.RootElement;
            var defaults = ServerSettings.WithDefaults(
                logicalProcessorCount: Math.Max(1, Environment.ProcessorCount),
                defaultModel: "");

            string Str(string name, string fallback) =>
                root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
                    ? el.GetString() ?? fallback
                    : fallback;

            int Int(string name, int fallback) =>
                root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.Number &&
                el.TryGetInt32(out var v)
                    ? v
                    : fallback;

            return defaults with
            {
                ModelPath = Str("ModelPath", defaults.ModelPath),
                Host = Str("Host", defaults.Host),
                Port = Int("Port", defaults.Port),
                ContextSize = Int("ContextSize", defaults.ContextSize),
                Threads = Int("Threads", defaults.Threads),
                BatchSize = Int("BatchSize", defaults.BatchSize),
                Parallel = Int("Parallel", defaults.Parallel),
                ExtraArguments = Str("ExtraArguments", ""),
            };
        }
        catch
        {
            return null;
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests\LlamaDesktop.Infrastructure.Tests -c Release`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/LlamaDesktop.Infrastructure tests/LlamaDesktop.Infrastructure.Tests
git commit -m "feat: config persistence and legacy import"
```

---

### Task 7: Infrastructure Process Management

**Files:**
- Create: `src/LlamaDesktop.Infrastructure/Processes/ProcessIdentity.cs`
- Create: `src/LlamaDesktop.Infrastructure/Processes/ProcessTreeTerminator.cs`
- Create: `src/LlamaDesktop.Infrastructure/Processes/LlamaServerController.cs`
- Test: `tests/LlamaDesktop.Infrastructure.Tests/ServerControllerTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `ProcessIdentity { int Pid; DateTime StartTimeUtc }` and `ProcessIdentity.Matches(Process) -> bool`.
  - `ProcessTreeTerminator.TerminateAsync(int pid, bool force, CancellationToken) -> int` with 5 s watchdog.
  - `LlamaServerController.Start(executablePath, arguments) -> Process`, `StopAsync(IProgress<StopPhase>, CancellationToken) -> StopResult`, `IsAlive`, `Identity`, `ProcessExited` event, `Detach()`.

- [ ] **Step 1: Write the failing tests**

`tests/LlamaDesktop.Infrastructure.Tests/ServerControllerTests.cs`:
```csharp
using System.Diagnostics;
using LlamaDesktop.Infrastructure.Processes;
using Xunit;

namespace LlamaDesktop.Infrastructure.Tests;

public class ProcessIdentityTests
{
    [Fact]
    public void Identity_Matches_Same_Process()
    {
        using var p = Process.Start(new ProcessStartInfo("powershell.exe", "-NoProfile -Command Start-Sleep 30")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        });
        Assert.NotNull(p);
        p!.Refresh();
        var identity = new ProcessIdentity(p.Id, p.StartTime.ToUniversalTime());
        p.Refresh();
        Assert.True(identity.Matches(p));
        p.Kill();
    }

    [Fact]
    public void Identity_Does_Not_Match_Different_Pid()
    {
        var identity = new ProcessIdentity(999999, DateTime.UtcNow.AddMinutes(-10));
        Assert.False(identity.Matches(new Process { Id = 999999 }));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests\LlamaDesktop.Infrastructure.Tests -c Release`
Expected: FAIL — types missing.

- [ ] **Step 3: Implement process management**

`src/LlamaDesktop.Infrastructure/Processes/ProcessIdentity.cs`:
```csharp
using System.Diagnostics;

namespace LlamaDesktop.Infrastructure.Processes;

public sealed record ProcessIdentity(int Pid, DateTime StartTimeUtc)
{
    public bool Matches(Process process)
    {
        try
        {
            if (process.Id != Pid) return false;
            process.Refresh();
            var start = process.StartTime.ToUniversalTime();
            return Math.Abs((start - StartTimeUtc).TotalSeconds) < 2;
        }
        catch
        {
            return false;
        }
    }
}
```

`src/LlamaDesktop.Infrastructure/Processes/ProcessTreeTerminator.cs`:
```csharp
using System.Diagnostics;

namespace LlamaDesktop.Infrastructure.Processes;

public sealed class ProcessTreeTerminator
{
    public async Task<int> TerminateAsync(int pid, bool force, CancellationToken ct)
    {
        var psi = new ProcessStartInfo("taskkill.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("/PID");
        psi.ArgumentList.Add(pid.ToString());
        psi.ArgumentList.Add("/T");
        if (force) psi.ArgumentList.Add("/F");

        using var p = Process.Start(psi);
        if (p is null) throw new InvalidOperationException("无法启动 taskkill.exe。");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            await p.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try { p.Kill(); } catch { }
            throw new TimeoutException("taskkill 未在限时内退出。");
        }
        return p.ExitCode;
    }
}
```

`src/LlamaDesktop.Infrastructure/Processes/LlamaServerController.cs`:
```csharp
using System.Diagnostics;

namespace LlamaDesktop.Infrastructure.Processes;

public enum StopPhase { Graceful, Soft, Hard, Completed, Failed }

public sealed class LlamaServerController
{
    private Process? _process;
    private ProcessIdentity? _identity;
    private readonly ProcessTreeTerminator _terminator = new();

    public event Action<int>? ProcessExited;
    public ProcessIdentity? Identity => _identity;
    public bool IsAlive => _process is { HasExited: false };

    public Process Start(string executablePath, string arguments)
    {
        var psi = new ProcessStartInfo(executablePath)
        {
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        var p = Process.Start(psi)
            ?? throw new InvalidOperationException("启动 llama-server 失败。");
        p.EnableRaisingEvents = true;
        _process = p;
        p.Refresh();
        _identity = new ProcessIdentity(p.Id, p.StartTime.ToUniversalTime());
        p.Exited += (_, _) => ProcessExited?.Invoke(p.ExitCode);
        return p;
    }

    public async Task<StopResult> StopAsync(
        IProgress<StopPhase> progress,
        CancellationToken ct)
    {
        if (_process is null) return StopResult.Failed("未跟踪进程。");
        progress.Report(StopPhase.Graceful);
        try { _process.CloseMainWindow(); } catch { }

        if (await WaitForExitAsync(TimeSpan.FromSeconds(3)))
        {
            progress.Report(StopPhase.Completed);
            return StopResult.Completed();
        }

        progress.Report(StopPhase.Soft);
        try
        {
            var code = await _terminator.TerminateAsync(_process.Id, force: false, ct);
            if (code == 0 && await WaitForExitAsync(TimeSpan.FromSeconds(3)))
            {
                progress.Report(StopPhase.Completed);
                return StopResult.Completed();
            }
        }
        catch { }

        progress.Report(StopPhase.Hard);
        try
        {
            var code = await _terminator.TerminateAsync(_process.Id, force: true, ct);
            if (code == 0 && await WaitForExitAsync(TimeSpan.FromSeconds(3)))
            {
                progress.Report(StopPhase.Completed);
                return StopResult.Completed();
            }
        }
        catch { }

        progress.Report(StopPhase.Failed);
        return StopResult.Failed($"服务未能停止，PID {_process.Id} 仍存活。");
    }

    private async Task<bool> WaitForExitAsync(TimeSpan timeout)
    {
        if (_process is null) return false;
        try
        {
            using var cts = new CancellationTokenSource(timeout);
            await _process.WaitForExitAsync(cts.Token);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Detach() => _process = null;
}

public sealed record StopResult(bool Succeeded, string Message)
{
    public static StopResult Completed() => new(true, "");
    public static StopResult Failed(string message) => new(false, message);
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests\LlamaDesktop.Infrastructure.Tests -c Release`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/LlamaDesktop.Infrastructure tests/LlamaDesktop.Infrastructure.Tests
git commit -m "feat: server process controller with bounded stop"
```

---

### Task 8: Infrastructure Port Allocation and Health Monitor

**Files:**
- Create: `src/LlamaDesktop.Infrastructure/Network/WindowsPortAllocator.cs`
- Create: `src/LlamaDesktop.Infrastructure/Network/LlamaHealthMonitor.cs`
- Test: `tests/LlamaDesktop.Infrastructure.Tests/PortAndHealthTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `WindowsPortAllocator.IsFree(int port) -> bool`, `PickFreePort(int preferred, IReadOnlyList<int>? candidates) -> int`.
  - `LlamaHealthMonitor.ProbeAsync(string baseUrl, string path, CancellationToken) -> bool` (1 s timeout).

- [ ] **Step 1: Write the failing tests**

`tests/LlamaDesktop.Infrastructure.Tests/PortAndHealthTests.cs`:
```csharp
using System.Net;
using System.Net.Sockets;
using LlamaDesktop.Infrastructure.Network;
using Xunit;

namespace LlamaDesktop.Infrastructure.Tests;

public class PortAndHealthTests
{
    [Fact]
    public void Occupied_Port_Is_Not_Free()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            Assert.False(WindowsPortAllocator.IsFree(port));
        }
        finally { listener.Stop(); }
    }

    [Fact]
    public void Free_Port_Is_Free()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        Assert.True(WindowsPortAllocator.IsFree(port));
    }

    [Fact]
    public async Task Health_Probe_Sees_Listening_Http()
    {
        using var http = new HttpListener();
        http.Prefixes.Add("http://127.0.0.1:18099/");
        http.Start();
        var serve = Task.Run(async () =>
        {
            var ctx = await http.GetContextAsync();
            ctx.Response.StatusCode = 200;
            ctx.Response.Close();
        });
        var monitor = new LlamaHealthMonitor();
        Assert.True(await monitor.ProbeAsync("http://127.0.0.1:18099", "health", CancellationToken.None));
        await serve;
        http.Stop();
    }

    [Fact]
    public async Task Health_Probe_Fails_On_Closed_Port()
    {
        var monitor = new LlamaHealthMonitor();
        Assert.False(await monitor.ProbeAsync("http://127.0.0.1:19999", "health", CancellationToken.None));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests\LlamaDesktop.Infrastructure.Tests -c Release`
Expected: FAIL — types missing.

- [ ] **Step 3: Implement networking**

`src/LlamaDesktop.Infrastructure/Network/WindowsPortAllocator.cs`:
```csharp
using System.Net;
using System.Net.Sockets;

namespace LlamaDesktop.Infrastructure.Network;

public static class WindowsPortAllocator
{
    public static readonly int[] Candidates = { 8080, 8090, 8081, 8188, 11434, 18080 };

    public static bool IsFree(int port)
    {
        try
        {
            var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static int PickFreePort(int preferred, IReadOnlyList<int>? candidates = null)
    {
        if (IsFree(preferred)) return preferred;
        foreach (var candidate in candidates ?? Candidates)
        {
            if (candidate == preferred) continue;
            if (IsFree(candidate)) return candidate;
        }
        return -1;
    }
}
```

`src/LlamaDesktop.Infrastructure/Network/LlamaHealthMonitor.cs`:
```csharp
using System.Net.Http;

namespace LlamaDesktop.Infrastructure.Network;

public sealed class LlamaHealthMonitor
{
    private static readonly HttpClient Client = new()
    {
        Timeout = TimeSpan.FromSeconds(1),
    };

    public async Task<bool> ProbeAsync(string baseUrl, string path, CancellationToken ct)
    {
        try
        {
            using var response = await Client.GetAsync($"{baseUrl.TrimEnd('/')}/{path.TrimStart('/')}", ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests\LlamaDesktop.Infrastructure.Tests -c Release`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/LlamaDesktop.Infrastructure tests/LlamaDesktop.Infrastructure.Tests
git commit -m "feat: port allocator and health monitor"
```

---

### Task 9: Infrastructure Incremental UTF-8 Log Reader

**Files:**
- Create: `src/LlamaDesktop.Infrastructure/Logging/IncrementalUtf8LogReader.cs`
- Test: `tests/LlamaDesktop.Infrastructure.Tests/LogReaderTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `IncrementalUtf8LogReader { string? ReadNew(); }` with persistent UTF-8 decoder and truncation reset.

- [ ] **Step 1: Write the failing tests**

`tests/LlamaDesktop.Infrastructure.Tests/LogReaderTests.cs`:
```csharp
using System.Text;
using LlamaDesktop.Infrastructure.Logging;
using Xunit;

namespace LlamaDesktop.Infrastructure.Tests;

public class LogReaderTests
{
    [Fact]
    public async Task Multibyte_Characters_Split_Across_Chunks_Are_Decoded()
    {
        var path = Path.Combine(Path.GetTempPath(), "ld-log-" + Guid.NewGuid().ToString("N") + ".log");
        try
        {
            var bytes = Encoding.UTF8.GetBytes("中文日志");
            var split = bytes.Length / 2;
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
            {
                fs.Write(bytes, 0, split);
            }
            var reader = new IncrementalUtf8LogReader(path);
            var first = reader.ReadNew();
            using (var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
            {
                fs.Write(bytes, split, bytes.Length - split);
            }
            var second = reader.ReadNew();
            Assert.Contains("中文日志", (first ?? "") + (second ?? ""));
        }
        finally { try { File.Delete(path); } catch { } }
    }

    [Fact]
    public void Truncation_Resets_Offset()
    {
        var path = Path.Combine(Path.GetTempPath(), "ld-log-" + Guid.NewGuid().ToString("N") + ".log");
        try
        {
            File.WriteAllText(path, "aaaa", new UTF8Encoding(false));
            var reader = new IncrementalUtf8LogReader(path);
            Assert.Equal("aaaa", reader.ReadNew());
            File.WriteAllText(path, "bb", new UTF8Encoding(false));
            Assert.Equal("bb", reader.ReadNew());
        }
        finally { try { File.Delete(path); } catch { } }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests\LlamaDesktop.Infrastructure.Tests -c Release`
Expected: FAIL — type missing.

- [ ] **Step 3: Implement the reader**

`src/LlamaDesktop.Infrastructure/Logging/IncrementalUtf8LogReader.cs`:
```csharp
using System.Text;

namespace LlamaDesktop.Infrastructure.Logging;

public sealed class IncrementalUtf8LogReader : IDisposable
{
    private readonly string _path;
    private FileStream? _stream;
    private long _offset;
    private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();

    public IncrementalUtf8LogReader(string path)
    {
        _path = path;
    }

    public string? ReadNew()
    {
        if (!File.Exists(_path)) return null;
        try
        {
            _stream ??= new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (_offset > _stream.Length)
            {
                _offset = 0;
                _decoder.Reset();
                _stream.Dispose();
                _stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            }
            _stream.Seek(_offset, SeekOrigin.Begin);
            var available = (int)Math.Min(65536, _stream.Length - _offset);
            if (available <= 0) return null;
            var buffer = new byte[available];
            var read = _stream.Read(buffer, 0, available);
            _offset += read;
            var charCount = _decoder.GetCharCount(buffer, 0, read, false);
            if (charCount == 0) return null;
            var chars = new char[charCount];
            _decoder.GetChars(buffer, 0, read, chars, 0, false);
            return new string(chars);
        }
        catch (IOException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        _stream?.Dispose();
        _stream = null;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests\LlamaDesktop.Infrastructure.Tests -c Release`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/LlamaDesktop.Infrastructure tests/LlamaDesktop.Infrastructure.Tests
git commit -m "feat: incremental UTF-8 log reader"
```

---

### Task 10: App WebView2 Host and Navigation Policy

**Files:**
- Create: `src/LlamaDesktop.App/Web/NavigationPolicy.cs`
- Create: `src/LlamaDesktop.App/Web/WebViewHost.cs`
- Test: `tests/LlamaDesktop.App.Tests/NavigationPolicyTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `NavigationPolicy.IsAllowed(Uri uri, Uri serviceBaseUri) -> bool` (loopback origin + service port only).
  - `WebViewHost` wrapper with `InitializeAsync(userDataFolder, ct)`, `NavigateToServiceAsync(baseUri, ct)`, `ShowNativePlaceholder(state, detail)`, `ClearBrowsingDataAsync()`, events `NavigationRequested` and `PlaceholderChanged`.

- [ ] **Step 1: Write the failing tests**

`tests/LlamaDesktop.App.Tests/NavigationPolicyTests.cs`:
```csharp
using LlamaDesktop.App.Web;
using Xunit;

namespace LlamaDesktop.App.Tests;

public class NavigationPolicyTests
{
    private static readonly Uri Service = new("http://127.0.0.1:8080/");

    [Fact]
    public void Loopback_Service_Origin_Is_Allowed()
    {
        Assert.True(NavigationPolicy.IsAllowed(new Uri("http://127.0.0.1:8080/"), Service));
        Assert.True(NavigationPolicy.IsAllowed(new Uri("http://localhost:8080/chat"), Service));
    }

    [Fact]
    public void Foreign_Origin_Is_Rejected()
    {
        Assert.False(NavigationPolicy.IsAllowed(new Uri("https://example.com/"), Service));
        Assert.False(NavigationPolicy.IsAllowed(new Uri("http://127.0.0.1:9999/"), Service));
        Assert.False(NavigationPolicy.IsAllowed(new Uri("file:///C:/x.html"), Service));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests\LlamaDesktop.App.Tests -c Release`
Expected: FAIL — type missing.

- [ ] **Step 3: Implement navigation policy and WebView host**

`src/LlamaDesktop.App/Web/NavigationPolicy.cs`:
```csharp
using System.Net;

namespace LlamaDesktop.App.Web;

public static class NavigationPolicy
{
    public static bool IsAllowed(Uri uri, Uri serviceBaseUri)
    {
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return false;
        if (!IsLoopbackHost(uri.Host))
            return false;
        return uri.Port == serviceBaseUri.Port;
    }

    private static bool IsLoopbackHost(string host)
    {
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;
        if (IPAddress.TryParse(host, out var address))
        {
            return IPAddress.IsLoopback(address);
        }
        return false;
    }
}
```

`src/LlamaDesktop.App/Web/WebViewHost.cs`:
```csharp
using Microsoft.Web.WebView2.Core;

namespace LlamaDesktop.App.Web;

public enum WebPlaceholderState { Loading, Error, NonLoopbackHost, Disconnected }

public sealed class WebViewHost : IDisposable
{
    private CoreWebView2Environment? _environment;
    private bool _initialized;

    public async Task InitializeAsync(string userDataFolder, CancellationToken ct)
    {
        _environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
        _initialized = true;
    }

    public Task NavigateToServiceAsync(Uri baseUri, CancellationToken ct)
    {
        if (!_initialized) throw new InvalidOperationException("WebView2 尚未初始化。");
        NavigationRequested?.Invoke(baseUri);
        return Task.CompletedTask;
    }

    public void ShowNativePlaceholder(WebPlaceholderState state, string? detail = null)
    {
        PlaceholderChanged?.Invoke(state, detail);
    }

    public async Task ClearBrowsingDataAsync()
    {
        // Note: CoreWebView2Environment has no CreateBrowserProfileAsync in any released
        // Microsoft.Web.WebView2 version; profiles are reachable only via CoreWebView2.Profile
        // after a WebView control exists (Task 11 shell). Kept as a documented no-op in MVP.
        await Task.CompletedTask;
    }

    public event Action<Uri>? NavigationRequested;
    public event Action<WebPlaceholderState, string?>? PlaceholderChanged;

    public void Dispose()
    {
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests\LlamaDesktop.App.Tests -c Release`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/LlamaDesktop.App tests/LlamaDesktop.App.Tests
git commit -m "feat: WebView host and navigation policy"
```

---

### Task 11: App Shell Window, ViewModels, and Composition Root

**Files:**
- Create: `src/LlamaDesktop.App/Presentation/ObservableObject.cs`
- Create: `src/LlamaDesktop.App/Presentation/ViewModels/LogViewModel.cs`
- Create: `src/LlamaDesktop.App/Presentation/ViewModels/ShellViewModel.cs`
- Create: `src/LlamaDesktop.App/ShellWindow.xaml`
- Create: `src/LlamaDesktop.App/ShellWindow.xaml.cs`
- Create: `src/LlamaDesktop.App/App.xaml`
- Create: `src/LlamaDesktop.App/App.xaml.cs`
- Create: `src/LlamaDesktop.App/CompositionRoot.cs`
- Modify: `src/LlamaDesktop.App/Program.cs`

**Interfaces:**
- Consumes: `LlamaServerController`, `WindowsPortAllocator`, `LlamaHealthMonitor`, `JsonConfigStore`, `IncrementalUtf8LogReader`, `ServerArgumentBuilder`, `SettingsValidator`, `CapabilitySnapshot` (all previous tasks).
- Produces: runnable WPF shell; `ShellViewModel` with `StartCommand`, `StopCommand`, `OpenBrowserCommand`, `CopyApiCommand`.

- [ ] **Step 1: Implement the MVVM plumbing**

`src/LlamaDesktop.App/Presentation/ObservableObject.cs`:
```csharp
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LlamaDesktop.App.Presentation;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}
```

`src/LlamaDesktop.App/Presentation/ViewModels/LogViewModel.cs`:
```csharp
using System.Collections.ObjectModel;
using LlamaDesktop.App.Presentation;

namespace LlamaDesktop.App.Presentation.ViewModels;

public sealed class LogViewModel : ObservableObject
{
    public ObservableCollection<string> Lines { get; } = new();

    public void Append(string text)
    {
        Lines.Add(text);
        while (Lines.Count > 2000) Lines.RemoveAt(0);
        OnPropertyChanged(nameof(Lines));
    }

    public void Clear() => Lines.Clear();
}
```

`src/LlamaDesktop.App/Presentation/ViewModels/ShellViewModel.cs`:
```csharp
using System.Diagnostics;
using System.Windows.Input;
using LlamaDesktop.App.Web;
using LlamaDesktop.App.Presentation;
using LlamaDesktop.Core.Arguments;
using LlamaDesktop.Core.Models;
using LlamaDesktop.Core.Validation;
using LlamaDesktop.Infrastructure.Logging;
using LlamaDesktop.Infrastructure.Network;
using LlamaDesktop.Infrastructure.Persistence;
using LlamaDesktop.Infrastructure.Processes;

namespace LlamaDesktop.App.Presentation.ViewModels;

public sealed class ShellViewModel : ObservableObject
{
    private readonly string _serverPath;
    private readonly string _logPath;
    private readonly JsonConfigStore _configStore;
    private readonly WebViewHost _webView;
    private readonly LlamaHealthMonitor _health = new();
    private LlamaServerController? _controller;
    private IncrementalUtf8LogReader? _logReader;
    private ServerSettings _settings;
    private ServerLifecycleState _state = ServerLifecycleState.Stopped;
    private string _statusText = "未运行";
    private Uri? _serviceUri;

    public ShellViewModel(
        string serverPath,
        string logPath,
        JsonConfigStore configStore,
        WebViewHost webView,
        ServerSettings initialSettings)
    {
        _serverPath = serverPath;
        _logPath = logPath;
        _configStore = configStore;
        _webView = webView;
        _settings = initialSettings;

        StartCommand = new RelayCommand(_ => StartAsync().ConfigureAwait(false), _ => CanStart);
        StopCommand = new RelayCommand(_ => StopAsync().ConfigureAwait(false), _ => CanStop);
        OpenBrowserCommand = new RelayCommand(_ => OpenBrowser(), _ => _serviceUri is not null);
        CopyApiCommand = new RelayCommand(_ => CopyApi(), _ => _serviceUri is not null);
    }

    public ICommand StartCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand OpenBrowserCommand { get; }
    public ICommand CopyApiCommand { get; }
    public LogViewModel Log { get; } = new();

    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }
    public bool CanStart => _state is ServerLifecycleState.Stopped or ServerLifecycleState.Failed or ServerLifecycleState.StopFailed;
    public bool CanStop => _state is ServerLifecycleState.Running or ServerLifecycleState.UiReady or ServerLifecycleState.WaitingForUi;
    public string ApiBaseUrl => _serviceUri?.ToString().TrimEnd('/') ?? "";

    private async Task StartAsync()
    {
        var issues = SettingsValidator.Validate(_settings);
        if (issues.Count > 0)
        {
            foreach (var issue in issues) Log.Append(issue.Message);
            return;
        }

        var port = _settings.AutoSelectPort
            ? WindowsPortAllocator.PickFreePort(_settings.Port)
            : _settings.Port;
        if (port < 0)
        {
            Log.Append($"端口 {_settings.Port} 已被占用，且没有可用候选端口。");
            return;
        }
        var effective = _settings with { Port = port };

        var config = new LauncherConfig { Settings = effective };
        try
        {
            _configStore.Save(config);
        }
        catch (Exception ex)
        {
            Log.Append($"保存配置失败：{ex.Message}");
        }

        var args = ServerArgumentBuilder.Build(effective, _logPath, CapabilitySnapshot.Full,
            logicalProcessors: Math.Max(1, Environment.ProcessorCount));
        _serviceUri = new Uri($"http://127.0.0.1:{port}");
        _state = ServerLifecycleState.StartingProcess;
        StatusText = "启动中";

        try
        {
            File.WriteAllText(_logPath, "");
            _logReader = new IncrementalUtf8LogReader(_logPath);
            _controller = new LlamaServerController();
            _controller.ProcessExited += code =>
            {
                Log.Append($"llama-server 已退出，退出码：{code}");
                _state = ServerLifecycleState.Failed;
                StatusText = $"启动失败（退出码 {code}）";
            };
            var proc = _controller.Start(_serverPath, WindowsArgumentQuoter.Quote(args));
            Log.Append($"已启动 llama-server，PID：{proc.Id}");
            _state = ServerLifecycleState.WaitingForUi;
            _ = PollHealthAsync(effective);
        }
        catch (Exception ex)
        {
            _state = ServerLifecycleState.Failed;
            StatusText = "启动失败";
            Log.Append($"启动服务失败：{ex.Message}");
        }
    }

    private async Task PollHealthAsync(ServerSettings effective)
    {
        while (_controller is { IsAlive: true })
        {
            ReadLog();
            if (await _health.ProbeAsync(_serviceUri!.ToString(), "health", CancellationToken.None))
            {
                _state = ServerLifecycleState.Running;
                StatusText = "运行中";
                _webView.NavigateToServiceAsync(_serviceUri, CancellationToken.None);
                break;
            }
            await Task.Delay(TimeSpan.FromSeconds(1));
        }
    }

    private void ReadLog()
    {
        if (_logReader is null) return;
        var text = _logReader.ReadNew();
        if (!string.IsNullOrEmpty(text))
        {
            foreach (var line in text.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries))
                Log.Append(line);
        }
    }

    private async Task StopAsync()
    {
        if (_controller is null) return;
        StatusText = "正在停止";
        var result = await _controller.StopAsync(new Progress<StopPhase>(phase =>
        {
            Log.Append($"停止阶段：{phase}");
        }), CancellationToken.None);
        if (result.Succeeded)
        {
            _state = ServerLifecycleState.Stopped;
            StatusText = "已停止";
        }
        else
        {
            _state = ServerLifecycleState.StopFailed;
            StatusText = result.Message;
        }
    }

    private void OpenBrowser()
    {
        if (_serviceUri is null) return;
        Process.Start(new ProcessStartInfo(_serviceUri.ToString()) { UseShellExecute = true });
    }

    private void CopyApi()
    {
        if (_serviceUri is null) return;
        System.Windows.Clipboard.SetText($"{_serviceUri.ToString().TrimEnd('/')}/v1");
        Log.Append("API 地址已复制到剪贴板。");
    }
}

public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Predicate<object?>? _canExecute;

    public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) => _execute(parameter);
    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }
}
```

- [ ] **Step 2: Implement the shell window XAML and code-behind**

`src/LlamaDesktop.App/ShellWindow.xaml`:
```xml
<Window x:Class="LlamaDesktop.App.ShellWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Llama Desktop" Width="1180" Height="780" MinWidth="900" MinHeight="600"
        WindowStartupLocation="CenterScreen" Background="#F5F6F8" FontFamily="Segoe UI">
  <Grid Margin="16">
    <Grid.RowDefinitions>
      <RowDefinition Height="Auto"/>
      <RowDefinition Height="*"/>
      <RowDefinition Height="Auto"/>
    </Grid.RowDefinitions>
    <Grid Grid.Row="0" Margin="0,0,0,12">
      <Grid.ColumnDefinitions>
        <ColumnDefinition Width="Auto"/><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/>
      </Grid.ColumnDefinitions>
      <TextBlock Text="Llama Desktop" FontSize="20" FontWeight="SemiBold" VerticalAlignment="Center"/>
      <TextBlock Grid.Column="1" Text="{Binding StatusText}" VerticalAlignment="Center" Margin="16,0,0,0" Foreground="#374151"/>
      <StackPanel Grid.Column="2" Orientation="Horizontal">
        <Button Content="复制 API 地址" Command="{Binding CopyApiCommand}" Margin="0,0,8,0" Padding="12,6"/>
        <Button Content="打开聊天页" Command="{Binding OpenBrowserCommand}" Padding="12,6"/>
      </StackPanel>
    </Grid>
    <Grid Grid.Row="1">
      <Grid.ColumnDefinitions>
        <ColumnDefinition Width="Auto" MinWidth="280"/>
        <ColumnDefinition Width="*"/>
      </Grid.ColumnDefinitions>
      <StackPanel Grid.Column="0" Margin="0,0,16,0">
        <Button Content="启动服务" Command="{Binding StartCommand}" Height="40" Margin="0,0,0,8"/>
        <Button Content="停止服务" Command="{Binding StopCommand}" Height="40" Margin="0,0,0,8"/>
        <TextBlock Text="{Binding ApiBaseUrl}" TextWrapping="Wrap" Foreground="#5F6368"/>
      </StackPanel>
      <Grid Grid.Column="1">
        <Grid.RowDefinitions>
          <RowDefinition Height="*"/>
          <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>
        <Border Grid.Row="0" Background="#FFFFFF" BorderBrush="#E0E0E0" BorderThickness="1">
          <Grid x:Name="WebHostGrid"/>
        </Border>
        <Expander Grid.Row="1" Header="实时日志" IsExpanded="True">
          <TextBox x:Name="LogText" IsReadOnly="True" AcceptsReturn="True" Height="160"
                   VerticalScrollBarVisibility="Auto" FontFamily="Consolas" FontSize="12"
                   Background="#111827" Foreground="#E5E7EB" BorderThickness="0" TextWrapping="NoWrap"/>
        </Expander>
      </Grid>
    </Grid>
  </Grid>
</Window>
```

`src/LlamaDesktop.App/ShellWindow.xaml.cs`:
```csharp
using System.Collections.Specialized;
using System.Windows;
using LlamaDesktop.App.Presentation.ViewModels;
using Microsoft.Web.WebView2.Wpf;

namespace LlamaDesktop.App;

public partial class ShellWindow : Window
{
    private readonly WebView2 _webView;

    public ShellWindow(ShellViewModel viewModel, WebView2 webView)
    {
        InitializeComponent();
        DataContext = viewModel;
        _webView = webView;
        WebHostGrid.Children.Add(webView);
        viewModel.Log.Lines.CollectionChanged += OnLogLinesChanged;
    }

    private void OnLogLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // ProcessExited can raise on a threadpool thread; marshal to the UI dispatcher.
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems is not null)
            {
                foreach (var item in e.NewItems)
                {
                    LogText.AppendText($"{item}\r\n");
                }
                LogText.ScrollToEnd();
            }
        }));
    }
}
```

Note: the `WebView2` control is populated by `CompositionRoot` before the window is shown; navigation is driven by `WebViewHost.NavigationRequested`. The log pane is wired here by subscribing to `LogViewModel.Lines` collection changes and marshalling to the UI dispatcher — this closes the brief's original gap where the log TextBox was never bound to the ViewModel.

- [ ] **Step 3: Implement App bootstrap and Composition Root**

`src/LlamaDesktop.App/App.xaml`:
```xml
<Application x:Class="LlamaDesktop.App.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Application.Resources/>
</Application>
```

`src/LlamaDesktop.App/App.xaml.cs`:
```csharp
using System.Windows;

namespace LlamaDesktop.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        CompositionRoot.Run(this);
    }
}
```

`src/LlamaDesktop.App/CompositionRoot.cs`:
```csharp
using System.Windows;
using Microsoft.Web.WebView2.Wpf;
using LlamaDesktop.App.Presentation.ViewModels;
using LlamaDesktop.App.Web;
using LlamaDesktop.Core.Models;
using LlamaDesktop.Infrastructure.Persistence;

namespace LlamaDesktop.App;

public static class CompositionRoot
{
    public static void Run(Application app)
    {
        var appRoot = Path.GetFullPath(AppContext.BaseDirectory);
        var serverPath = Path.Combine(appRoot, "llama-server.exe");
        var dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LlamaDesktop");
        var configPath = Path.Combine(dataDir, "config.json");
        var logPath = Path.Combine(dataDir, "logs", "launcher-server.log");
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        var webViewData = Path.Combine(dataDir, "webview2");

        var logs = new List<string>();
        var configStore = new JsonConfigStore(configPath, logs.Add);
        var legacyPath = Path.Combine(appRoot, "launcher-config.json");

        var saved = configStore.Load()?.Settings
            ?? LegacyConfigImporter.TryImport(legacyPath)
            ?? ServerSettings.WithDefaults(Math.Max(1, Environment.ProcessorCount), "");

        var modelsDir = Path.Combine(appRoot, "models");
        var firstModel = Directory.Exists(modelsDir)
            ? Directory.EnumerateFiles(modelsDir, "*.gguf", SearchOption.AllDirectories)
                .Where(f => !Path.GetFileName(f).StartsWith("Modelfile."))
                .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault()
            : null;
        var effective = string.IsNullOrWhiteSpace(saved.ModelPath)
            ? saved with { ModelPath = firstModel ?? "", ModelsDirectory = "models" }
            : saved;

        var webViewHost = new WebViewHost();
        _ = webViewHost.InitializeAsync(webViewData, CancellationToken.None);

        var viewModel = new ShellViewModel(
            serverPath, logPath, configStore, webViewHost, effective);

        var webView = new WebView2();
        Uri? serviceBase = null;
        webViewHost.NavigationRequested += uri =>
        {
            serviceBase = uri;
            webView.CoreWebView2.Navigate(uri.ToString());
        };
        webView.NavigationStarting += (_, e) =>
        {
            if (serviceBase is null) { e.Cancel = true; return; }
            if (!NavigationPolicy.IsAllowed(new Uri(e.Uri), serviceBase))
            {
                e.Cancel = true;
                try
                {
                    System.Diagnostics.Process.Start(
                        new System.Diagnostics.ProcessStartInfo(e.Uri) { UseShellExecute = true });
                }
                catch { }
            }
        };
        _ = webView.EnsureCoreWebView2Async();

        var window = new ShellWindow(viewModel, webView);
        app.MainWindow = window;
        window.Show();
    }
}
```

`src/LlamaDesktop.App/Program.cs`:
```csharp
using System;
using System.Windows;

namespace LlamaDesktop.App;

public static class Program
{
    [STAThread]
    public static void Main()
    {
        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
```

- [ ] **Step 4: Build and smoke-verify the window**

Run:
```powershell
dotnet build LlamaDesktop.sln -c Release
$p = Start-Process .\src\LlamaDesktop.App\bin\Release\net8.0-windows\LlamaDesktop.exe -PassThru
Start-Sleep -Seconds 5
$p.Refresh()
"PID=$($p.Id) TITLE=$($p.MainWindowTitle)"
Stop-Process -Id $p.Id -Force
```
Expected: Build succeeds; window opens with a visible title.

- [ ] **Step 5: Commit**

```bash
git add src/LlamaDesktop.App
git commit -m "feat: WPF shell with WebView2 host"
```

---

### Task 12: Self-Contained Publish and Acceptance Verification

**Files:**
- Create: `scripts/publish.ps1`
- Create: `scripts/verify-publish.ps1`

**Interfaces:**
- Consumes: all previous tasks.
- Produces: `dist/LlamaDesktop/` self-contained folder and a verification script.

- [ ] **Step 1: Write the publish script**

`scripts/publish.ps1`:
```powershell
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root 'dist\LlamaDesktop'
if (Test-Path $out) { Remove-Item -LiteralPath $out -Recurse -Force }
dotnet publish (Join-Path $root 'src\LlamaDesktop.App\LlamaDesktop.App.csproj') `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=false -p:IncludeNativeLibrariesForSelfExtract=false `
  -o $out
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }
Copy-Item (Join-Path $root 'llama-server.exe') $out -Force
# Copy the full llama.cpp runtime closure: the portable package's root DLLs are all
# runtime dependencies (llama-server-impl, llama-common, llama, mtmd, libomp, ggml*, CUDA).
Get-ChildItem (Join-Path $root '*.dll') -File -ErrorAction SilentlyContinue | Copy-Item -Destination $out -Force
Copy-Item (Join-Path $root '启动Llama.cmd') $out -Force
Copy-Item (Join-Path $root 'LlamaLauncher.ps1') $out -Force
Copy-Item (Join-Path $root 'Launcher.Core.psm1') $out -Force
if (-not (Test-Path (Join-Path $out 'models'))) { Copy-Item (Join-Path $root 'models') $out -Recurse -Force }
Write-Host "Published to $out"
```

- [ ] **Step 2: Write the verification script**

`scripts/verify-publish.ps1`:
```powershell
$ErrorActionPreference = 'Stop'
$out = Join-Path (Split-Path -Parent $PSScriptRoot) 'dist\LlamaDesktop'
foreach ($required in @('LlamaDesktop.exe','llama-server.exe','启动Llama.cmd','LlamaLauncher.ps1','Launcher.Core.psm1','WebView2Loader.dll')) {
    $hit = Get-ChildItem $out -Recurse -Filter $required -File -ErrorAction SilentlyContinue
    if (-not $hit) { throw "缺少发布文件：$required" }
}
$ggufCount = @(Get-ChildItem (Join-Path $out 'models') -Recurse -Filter '*.gguf' -File -ErrorAction SilentlyContinue).Count
if ($ggufCount -eq 0) { throw '发布目录中没有 GGUF 模型。' }
# Runtime gate: the published server binary must actually execute (loads its DLL closure).
# Pin the working directory to the publish output so the binary cannot borrow DLLs from the caller's CWD.
foreach ($closureDll in @('llama-server-impl.dll','llama-common.dll','llama.dll','mtmd.dll','libomp.dll')) {
    if (-not (Test-Path (Join-Path $out $closureDll))) { throw "缺少运行时依赖：$closureDll" }
}
Push-Location $out
try {
    $oldEap = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    $versionOutput = & .\llama-server.exe --version 2>&1 | Out-String
    $ErrorActionPreference = $oldEap
    if ($LASTEXITCODE -ne 0) { throw "llama-server 无法运行（DLL 缺失或损坏）：`n$versionOutput" }
}
finally { Pop-Location }
Write-Host "Publish verification OK. GGUF count: $ggufCount"
```

- [ ] **Step 3: Publish and verify**

Run:
```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\publish.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\verify-publish.ps1
```
Expected: `Publish verification OK. GGUF count: N`.

- [ ] **Step 4: Run the full test suite**

Run: `dotnet test LlamaDesktop.sln -c Release`
Expected: All tests pass.

- [ ] **Step 5: Real integration smoke (manual/nightly)**

Run from `dist\LlamaDesktop`:
1. Launch `LlamaDesktop.exe`.
2. Start the service with the discovered default model.
3. Confirm status transitions to `运行中`, WebView shows the official UI.
4. Stop; confirm `已停止`, PID gone, port released.
5. Close window; confirm the keep-running/stop dialog behavior.

- [ ] **Step 6: Commit**

```bash
git add scripts
git commit -m "chore: publish and verification scripts"
```

---

## Self-Review Notes

- Legacy model scan fix (Task 2) is independent of the new app and unblocks the repacked `models` layout immediately.
- Capability gating: MVP passes `CapabilitySnapshot.Full`; the real capability detector from the architecture (probe `--help`) is Phase 2 work. `--log-file` fallback and endpoint gating are implemented at the argument level now so Phase 2 only adds the detector.
- Non-loopback host → native placeholder is wired via `NavigationPolicy`/`WebPlaceholderState`; the MVP window keeps `Host` at its default loopback value.
- The `WebView2` control is created in `CompositionRoot` and hosted in `WebHostGrid`; `WebViewHost` stays event-driven so the App.Tests project can test the policy without instantiating the control.
- No Git repository existed at plan time; Task 1 Step 0 initializes one, and each task ends with a commit.
- Phase 2 (hardware profiling, capability detection, recommendations), Phase 3 (Router multi-model), Phase 4 (productization) are out of scope for this MVP plan and become follow-up plans.
