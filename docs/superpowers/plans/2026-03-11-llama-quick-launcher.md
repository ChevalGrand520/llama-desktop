# Llama Quick Launcher Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a zero-install Windows WPF launcher that starts, monitors, opens, and stops the bundled `llama-server.exe` with persisted settings and actionable Chinese errors.

**Architecture:** Keep testable configuration, validation, argument construction, and quoting in a focused PowerShell module. Keep WPF composition and process orchestration in one entry script, use the server's `--log-file` plus incremental file tailing so a user may close the launcher while leaving the server alive, and expose a small CMD bootstrap for double-click use.

**Tech Stack:** Windows PowerShell 5.1, WPF/XAML, .NET Framework process/network APIs, JSON, built-in PowerShell test harness; no runtime package installation.

## Global Constraints

- Runtime must require no Python, Node.js, .NET SDK, PowerShell modules, or downloaded dependencies.
- Application root is always the directory containing `LlamaLauncher.ps1`, regardless of the caller's current directory.
- Managed executable is exactly `llama-server.exe` in the application root.
- Default model is the saved model when valid, otherwise the first sorted `models\*.gguf` file.
- Defaults are GPU layers `all`, context `8192`, logical processor count, host `127.0.0.1`, port `8080`, Flash Attention enabled, mmap enabled, and mlock disabled.
- Current server arguments are `--model`, `--gpu-layers`, `--ctx-size`, `--threads`, `--host`, `--port`, `--batch-size`, `--parallel`, `--flash-attn`, and `--load-mode`.
- Do not use deprecated `--mmap`, `--no-mmap`, or `--mlock`; map the two UI booleans to `--load-mode auto|none|mmap|mlock|mmap+mlock`.
- Only the PID started by this launcher may be stopped; never kill by process name.
- While the process remains alive, health checks have no overall loading deadline; each HTTP attempt has a one-second timeout.
- Configuration contains no secrets and is stored as UTF-8 JSON beside the launcher.
- The workspace is not a Git repository, so commit steps are explicitly skipped unless the user initializes Git before execution.

---

## File Map

- Create `Launcher.Core.psm1`: defaults, configuration normalization, validation, managed-option conflict detection, Windows argument quoting, and server argument construction.
- Create `LlamaLauncher.ps1`: WPF window, control state, model browser, config load/save, process lifecycle, log tailing, asynchronous health polling, clipboard/browser actions, and close behavior.
- Create `启动Llama.cmd`: stable double-click bootstrap using Windows PowerShell STA mode.
- Create `tests\Launcher.Core.Tests.ps1`: dependency-free tests for all pure core behavior.
- Create `tests\Launcher.Smoke.Tests.ps1`: static/bootstrap smoke tests that do not load the large model.
- Runtime-generated `launcher-config.json` and `launcher-server.log` are not checked in or pre-created.

---

### Task 1: Core Configuration and Validation Module

**Files:**
- Create: `Launcher.Core.psm1`
- Create: `tests\Launcher.Core.Tests.ps1`

**Interfaces:**
- Produces: `Get-LauncherDefaults -LogicalProcessorCount <int> -DefaultModel <string>` returning a PSCustomObject.
- Produces: `Merge-LauncherConfig -Defaults <object> -Saved <object>` returning a normalized PSCustomObject.
- Produces: `Test-LauncherSettings -Settings <object> -ServerPath <string>` returning `string[]` errors.
- Produces: `Split-ExtraArguments -Text <string>` returning `string[]` or throwing a localized parse error.
- Produces: `ConvertTo-WindowsCommandLine -Arguments <string[]>` returning one safely quoted argument string.
- Produces: `New-LlamaServerArguments -Settings <object> -LogPath <string>` returning `string[]`.

- [ ] **Step 1: Write the dependency-free failing test harness and default/config tests**

```powershell
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot '..\Launcher.Core.psm1') -Force

function Assert-Equal($Expected, $Actual, [string]$Message) {
    if ($Expected -ne $Actual) { throw "$Message`nExpected: $Expected`nActual: $Actual" }
}
function Assert-True([bool]$Value, [string]$Message) {
    if (-not $Value) { throw $Message }
}
function Assert-Throws([scriptblock]$Action, [string]$MessageLike) {
    try { & $Action; throw 'Expected action to throw.' }
    catch { if ($_.Exception.Message -notlike $MessageLike) { throw } }
}

$defaults = Get-LauncherDefaults -LogicalProcessorCount 16 -DefaultModel 'C:\models\a.gguf'
Assert-Equal 'all' $defaults.GpuLayers 'GPU default'
Assert-Equal 8192 $defaults.ContextSize 'Context default'
Assert-Equal 16 $defaults.Threads 'Thread default'
Assert-Equal '127.0.0.1' $defaults.Host 'Host default'
Assert-Equal 8080 $defaults.Port 'Port default'
Assert-True $defaults.FlashAttention 'Flash Attention default'
Assert-True $defaults.MemoryMap 'mmap default'
Assert-True (-not $defaults.MemoryLock) 'mlock default'

$saved = [pscustomobject]@{ Port = 9090; ContextSize = 'bad'; Unknown = 1 }
$merged = Merge-LauncherConfig -Defaults $defaults -Saved $saved
Assert-Equal 9090 $merged.Port 'Valid saved field'
Assert-Equal 8192 $merged.ContextSize 'Invalid saved field fallback'
Assert-True (-not ($merged.PSObject.Properties.Name -contains 'Unknown')) 'Unknown fields ignored'
```

- [ ] **Step 2: Run tests and verify the module is missing**

Run: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Launcher.Core.Tests.ps1`

Expected: FAIL with `Launcher.Core.psm1` not found or `Get-LauncherDefaults` not recognized.

- [ ] **Step 3: Implement defaults and typed configuration merging**

```powershell
Set-StrictMode -Version Latest

function Get-LauncherDefaults {
    param([int]$LogicalProcessorCount, [string]$DefaultModel)
    [pscustomobject][ordered]@{
        ModelPath = $DefaultModel; GpuLayers = 'all'; ContextSize = 8192
        Threads = [Math]::Max(1, $LogicalProcessorCount); Host = '127.0.0.1'; Port = 8080
        BatchSize = 2048; Parallel = 1; FlashAttention = $true
        MemoryMap = $true; MemoryLock = $false; ExtraArguments = ''
    }
}

function Merge-LauncherConfig {
    param([Parameter(Mandatory)]$Defaults, $Saved)
    $result = [ordered]@{}
    foreach ($property in $Defaults.PSObject.Properties) {
        $value = $property.Value
        if ($null -ne $Saved -and $Saved.PSObject.Properties.Name -contains $property.Name) {
            $candidate = $Saved.$($property.Name)
            try {
                if ($value -is [bool]) { if ($candidate -isnot [bool]) { throw 'type' }; $value = $candidate }
                elseif ($value -is [int]) { $value = [int]$candidate }
                else { $value = [string]$candidate }
            } catch { $value = $property.Value }
        }
        $result[$property.Name] = $value
    }
    [pscustomobject]$result
}
```

- [ ] **Step 4: Add failing tests for validation, parsing, quoting, conflicts, and argument mapping**

Append before the end of `tests\Launcher.Core.Tests.ps1`:

```powershell
$valid = Get-LauncherDefaults -LogicalProcessorCount 8 -DefaultModel $PSCommandPath
$errors = Test-LauncherSettings -Settings $valid -ServerPath $PSCommandPath
Assert-Equal 0 $errors.Count 'Valid settings'
$valid.Port = 70000
Assert-True ((Test-LauncherSettings $valid $PSCommandPath) -contains '端口必须在 1 到 65535 之间。') 'Port validation'

$split = Split-ExtraArguments '--verbose --alias "Local Model"'
Assert-Equal 3 $split.Count 'Quoted extra argument count'
Assert-Equal 'Local Model' $split[2] 'Quoted extra argument value'
Assert-Throws { Split-ExtraArguments '--alias "broken' } '*引号*'
Assert-Throws { New-LlamaServerArguments -Settings ([pscustomobject]@{
    ModelPath=$PSCommandPath;GpuLayers='all';ContextSize=8192;Threads=8;Host='127.0.0.1';Port=8080
    BatchSize=2048;Parallel=1;FlashAttention=$true;MemoryMap=$true;MemoryLock=$false
    ExtraArguments='--port 9999'
}) -LogPath 'C:\tmp\server.log' } '*受启动器管理*'

$q = ConvertTo-WindowsCommandLine @('plain', 'C:\model files\x.gguf', 'a"b', '')
Assert-Equal 'plain "C:\model files\x.gguf" "a\"b" ""' $q 'Windows quoting'

$args = New-LlamaServerArguments -Settings ([pscustomobject]@{
    ModelPath='C:\model files\x.gguf';GpuLayers='all';ContextSize=8192;Threads=8
    Host='127.0.0.1';Port=8080;BatchSize=1024;Parallel=2;FlashAttention=$true
    MemoryMap=$true;MemoryLock=$true;ExtraArguments='--verbose'
}) -LogPath 'C:\tmp\server.log'
Assert-Equal 'mmap+mlock' $args[[Array]::IndexOf($args, '--load-mode') + 1] 'Load mode mapping'
Assert-True ($args -contains '--log-file') 'Log file argument'
Assert-True ($args -contains '--verbose') 'Extra argument'
Write-Host 'Launcher.Core tests passed.' -ForegroundColor Green
```

- [ ] **Step 5: Implement validation, extra-argument parsing, conflict detection, quoting, and argument construction**

Implementation requirements in `Launcher.Core.psm1`:

```powershell
$script:ManagedOptions = @('--model','-m','--gpu-layers','--n-gpu-layers','-ngl','--ctx-size','-c','--threads','-t','--host','--port','--batch-size','-b','--parallel','-np','--flash-attn','-fa','--load-mode','-lm','--log-file')

function Test-LauncherSettings {
    param($Settings, [string]$ServerPath)
    $errors = [Collections.Generic.List[string]]::new()
    if (-not (Test-Path -LiteralPath $ServerPath -PathType Leaf)) { $errors.Add('找不到 llama-server.exe。') }
    if (-not (Test-Path -LiteralPath $Settings.ModelPath -PathType Leaf) -or [IO.Path]::GetExtension($Settings.ModelPath) -ine '.gguf') { $errors.Add('请选择有效的 GGUF 模型文件。') }
    if ($Settings.GpuLayers -notmatch '^(all|auto|0|[1-9][0-9]*)$') { $errors.Add('GPU 层数必须是 all、auto 或非负整数。') }
    if ($Settings.ContextSize -lt 1) { $errors.Add('上下文长度必须大于 0。') }
    if ($Settings.Threads -lt 1) { $errors.Add('CPU 线程数必须大于 0。') }
    if ($Settings.Port -lt 1 -or $Settings.Port -gt 65535) { $errors.Add('端口必须在 1 到 65535 之间。') }
    if ($Settings.BatchSize -lt 1) { $errors.Add('批大小必须大于 0。') }
    if ($Settings.Parallel -lt 1) { $errors.Add('并行请求数必须大于 0。') }
    if ([string]::IsNullOrWhiteSpace($Settings.Host)) { $errors.Add('监听地址不能为空。') }
    $errors.ToArray()
}

function Split-ExtraArguments {
    param([string]$Text)
    if ([string]::IsNullOrWhiteSpace($Text)) { return ,([string[]]@()) }
    $result = [Collections.Generic.List[string]]::new(); $current = [Text.StringBuilder]::new(); $quoted = $false
    for ($i=0; $i -lt $Text.Length; $i++) {
        $c = $Text[$i]
        if ($c -eq '"') { $quoted = -not $quoted; continue }
        if ([char]::IsWhiteSpace($c) -and -not $quoted) {
            if ($current.Length) { $result.Add($current.ToString()); [void]$current.Clear() }
        } else { [void]$current.Append($c) }
    }
    if ($quoted) { throw '额外参数中的引号没有闭合。' }
    if ($current.Length) { $result.Add($current.ToString()) }
    $result.ToArray()
}

function ConvertTo-WindowsCommandLine {
    param([string[]]$Arguments)
    ($Arguments | ForEach-Object {
        if ($_ -eq '') { '""' }
        elseif ($_ -notmatch '[\s"]') { $_ }
        else { '"' + ([regex]::Replace($_, '(\\*)"', '$1$1\"') -replace '(\\+)$','$1$1') + '"' }
    }) -join ' '
}

function New-LlamaServerArguments {
    param($Settings, [string]$LogPath)
    $extra = @(Split-ExtraArguments $Settings.ExtraArguments)
    foreach ($token in $extra) { if ($script:ManagedOptions -contains $token.ToLowerInvariant()) { throw "额外参数 $token 由启动器管理，不能重复指定。" } }
    $loadMode = if ($Settings.MemoryMap -and $Settings.MemoryLock) {'mmap+mlock'} elseif ($Settings.MemoryMap) {'mmap'} elseif ($Settings.MemoryLock) {'mlock'} else {'none'}
    [string[]]$base = @('--model',$Settings.ModelPath,'--gpu-layers',[string]$Settings.GpuLayers,'--ctx-size',[string]$Settings.ContextSize,'--threads',[string]$Settings.Threads,'--host',$Settings.Host,'--port',[string]$Settings.Port,'--batch-size',[string]$Settings.BatchSize,'--parallel',[string]$Settings.Parallel,'--flash-attn',$(if($Settings.FlashAttention){'on'}else{'off'}),'--load-mode',$loadMode,'--log-file',$LogPath,'--log-timestamps')
    @($base + $extra)
}

Export-ModuleMember -Function Get-LauncherDefaults,Merge-LauncherConfig,Test-LauncherSettings,Split-ExtraArguments,ConvertTo-WindowsCommandLine,New-LlamaServerArguments
```

- [ ] **Step 6: Run core tests**

Run: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Launcher.Core.Tests.ps1`

Expected: `Launcher.Core tests passed.` and exit code `0`.

- [ ] **Step 7: Commit checkpoint**

Expected: SKIP because `git rev-parse --is-inside-work-tree` reports this workspace is not a Git repository.

---

### Task 2: WPF Launcher and Process Lifecycle

**Files:**
- Create: `LlamaLauncher.ps1`
- Create: `tests\Launcher.Smoke.Tests.ps1`

**Interfaces:**
- Consumes all exported functions from `Launcher.Core.psm1`.
- Produces a WPF application with named controls `ModelPath`, `GpuLayers`, `ContextSize`, `Threads`, `Host`, `Port`, `BatchSize`, `Parallel`, `FlashAttention`, `MemoryMap`, `MemoryLock`, `ExtraArguments`, `StartStopButton`, `OpenUiButton`, `CopyApiButton`, `StatusText`, and `LogText`.
- Maintains one `$script:ServerProcess` PID, one `$script:HealthClient`, one dispatcher timer, and one log byte offset.

- [ ] **Step 1: Write failing static smoke tests**

```powershell
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$scriptPath = Join-Path $root 'LlamaLauncher.ps1'
if (-not (Test-Path $scriptPath)) { throw 'LlamaLauncher.ps1 is missing.' }
$tokens = $null; $errors = $null
[void][Management.Automation.Language.Parser]::ParseFile($scriptPath, [ref]$tokens, [ref]$errors)
if ($errors.Count) { throw ($errors | Out-String) }
$text = Get-Content -LiteralPath $scriptPath -Raw
@('llama-server.exe','launcher-config.json','launcher-server.log','New-LlamaServerArguments','taskkill.exe','/health','ShowDialog') | ForEach-Object {
    if ($text -notmatch [regex]::Escape($_)) { throw "Expected launcher marker missing: $_" }
}
Write-Host 'Launcher smoke tests passed.' -ForegroundColor Green
```

- [ ] **Step 2: Run smoke tests and verify failure**

Run: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Launcher.Smoke.Tests.ps1`

Expected: FAIL with `LlamaLauncher.ps1 is missing.`

- [ ] **Step 3: Build the WPF shell with complete controls and responsive layout**

Create `LlamaLauncher.ps1` with `Set-StrictMode`, WPF assemblies, module import, script-root paths, and XAML. The XAML must use a two-column settings grid, an `Expander Header="高级设置"`, fixed-height command row, and a read-only monospace log box. Parse it with:

```powershell
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase,System.Xaml
Import-Module (Join-Path $PSScriptRoot 'Launcher.Core.psm1') -Force
$script:ServerPath = Join-Path $PSScriptRoot 'llama-server.exe'
$script:ConfigPath = Join-Path $PSScriptRoot 'launcher-config.json'
$script:LogPath = Join-Path $PSScriptRoot 'launcher-server.log'
[xml]$xaml = @'
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" Title="Llama 快速启动器" Width="860" Height="720" MinWidth="720" MinHeight="600" WindowStartupLocation="CenterScreen" Background="#F5F6F8" FontFamily="Segoe UI">
  <Grid Margin="20">
    <Grid.RowDefinitions><RowDefinition Height="Auto"/><RowDefinition Height="Auto"/><RowDefinition Height="Auto"/><RowDefinition Height="*"/></Grid.RowDefinitions>
    <Grid x:Name="SettingsPanel" Grid.Row="0">
      <Grid.ColumnDefinitions><ColumnDefinition Width="120"/><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
      <Grid.RowDefinitions><RowDefinition Height="Auto"/><RowDefinition Height="Auto"/><RowDefinition Height="Auto"/><RowDefinition Height="Auto"/></Grid.RowDefinitions>
      <TextBlock Grid.Row="0" Text="GGUF 模型" VerticalAlignment="Center" Margin="0,0,12,10"/><ComboBox x:Name="ModelPath" Grid.Row="0" Grid.Column="1" IsEditable="True" Margin="0,0,8,10"/><Button x:Name="BrowseModel" Grid.Row="0" Grid.Column="2" Content="浏览..." MinWidth="76" Margin="0,0,0,10"/>
      <TextBlock Grid.Row="1" Text="GPU 层数" VerticalAlignment="Center" Margin="0,0,12,10"/><TextBox x:Name="GpuLayers" Grid.Row="1" Grid.Column="1" Margin="0,0,8,10"/><TextBlock Grid.Row="1" Grid.Column="2" Text="all / auto / 数字" Foreground="#666" VerticalAlignment="Center" Margin="0,0,0,10"/>
      <TextBlock Grid.Row="2" Text="上下文 / 线程" VerticalAlignment="Center" Margin="0,0,12,10"/><Grid Grid.Row="2" Grid.Column="1" Grid.ColumnSpan="2" Margin="0,0,0,10"><Grid.ColumnDefinitions><ColumnDefinition/><ColumnDefinition Width="12"/><ColumnDefinition/></Grid.ColumnDefinitions><TextBox x:Name="ContextSize" Grid.Column="0"/><TextBox x:Name="Threads" Grid.Column="2"/></Grid>
      <TextBlock Grid.Row="3" Text="地址 / 端口" VerticalAlignment="Center" Margin="0,0,12,0"/><Grid Grid.Row="3" Grid.Column="1" Grid.ColumnSpan="2"><Grid.ColumnDefinitions><ColumnDefinition/><ColumnDefinition Width="12"/><ColumnDefinition Width="140"/></Grid.ColumnDefinitions><TextBox x:Name="Host" Grid.Column="0"/><TextBox x:Name="Port" Grid.Column="2"/></Grid>
    </Grid>
    <Expander x:Name="AdvancedExpander" Grid.Row="1" Header="高级设置" Margin="0,16,0,12">
      <Grid Margin="0,10,0,0">
        <Grid.ColumnDefinitions><ColumnDefinition Width="120"/><ColumnDefinition Width="*"/><ColumnDefinition Width="24"/><ColumnDefinition Width="120"/><ColumnDefinition Width="*"/></Grid.ColumnDefinitions>
        <Grid.RowDefinitions><RowDefinition Height="Auto"/><RowDefinition Height="Auto"/><RowDefinition Height="Auto"/></Grid.RowDefinitions>
        <TextBlock Text="批大小" VerticalAlignment="Center" Margin="0,0,12,10"/><TextBox x:Name="BatchSize" Grid.Column="1" Margin="0,0,0,10"/><TextBlock Grid.Column="3" Text="并行请求数" VerticalAlignment="Center" Margin="0,0,12,10"/><TextBox x:Name="Parallel" Grid.Column="4" Margin="0,0,0,10"/>
        <StackPanel Grid.Row="1" Grid.ColumnSpan="5" Orientation="Horizontal" Margin="0,0,0,10"><CheckBox x:Name="FlashAttention" Content="Flash Attention" Margin="0,0,24,0"/><CheckBox x:Name="MemoryMap" Content="内存映射" Margin="0,0,24,0"/><CheckBox x:Name="MemoryLock" Content="锁定内存"/></StackPanel>
        <TextBlock Grid.Row="2" Text="额外参数" VerticalAlignment="Center" Margin="0,0,12,0"/><TextBox x:Name="ExtraArguments" Grid.Row="2" Grid.Column="1" Grid.ColumnSpan="4"/>
      </Grid>
    </Expander>
    <Grid Grid.Row="2" Height="46" Margin="0,0,0,12"><Grid.ColumnDefinitions><ColumnDefinition Width="Auto"/><ColumnDefinition Width="Auto"/><ColumnDefinition Width="Auto"/><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions><Button x:Name="StartStopButton" Content="启动服务" Width="112" Margin="0,0,8,0"/><Button x:Name="OpenUiButton" Grid.Column="1" Content="打开聊天页" Width="112" Margin="0,0,8,0" IsEnabled="False"/><Button x:Name="CopyApiButton" Grid.Column="2" Content="复制 API 地址" Width="124" IsEnabled="False"/><TextBlock x:Name="StatusText" Grid.Column="4" Text="未运行" VerticalAlignment="Center" FontWeight="SemiBold"/></Grid>
    <TextBox x:Name="LogText" Grid.Row="3" IsReadOnly="True" AcceptsReturn="True" VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Auto" FontFamily="Consolas" FontSize="12" Background="#111827" Foreground="#E5E7EB" Padding="10" TextWrapping="NoWrap"/>
  </Grid>
</Window>
'@
$reader = [System.Xml.XmlNodeReader]::new($xaml)
$window = [Windows.Markup.XamlReader]::Load($reader)
```

Bind every named control immediately after loading with `$window.FindName('<name>')`; fail fast with a localized error if any lookup returns `$null`.

- [ ] **Step 4: Implement model discovery, config load/save, control mapping, and validation display**

Use these concrete functions:

```powershell
function Find-DefaultModel { @(Get-ChildItem (Join-Path $PSScriptRoot 'models') -Filter '*.gguf' -File -ErrorAction SilentlyContinue | Sort-Object Name | Select-Object -ExpandProperty FullName -First 1)[0] }
function Read-LauncherConfig { try { if (Test-Path $script:ConfigPath) { Get-Content $script:ConfigPath -Raw -Encoding UTF8 | ConvertFrom-Json } } catch { Add-LogLine "配置读取失败，已使用默认值：$($_.Exception.Message)" } }
function Save-LauncherConfig($settings) {
    $temp = "$script:ConfigPath.tmp"
    $settings | ConvertTo-Json | Set-Content -LiteralPath $temp -Encoding UTF8
    Move-Item -LiteralPath $temp -Destination $script:ConfigPath -Force
}
function Show-ValidationErrors([string[]]$errors) {
    $message = $errors -join "`n"
    Add-LogLine $message
    [Windows.MessageBox]::Show($message, '无法启动', 'OK', 'Warning') | Out-Null
}
```

`Get-SettingsFromControls` must cast numeric fields inside `try/catch`; conversion errors become Chinese validation messages rather than terminating the GUI. `Set-ControlsFromSettings` restores every persisted field.

- [ ] **Step 5: Implement process start, file log tailing, asynchronous health checks, and UI state**

Start the server with `ProcessStartInfo` and the core quoting function:

```powershell
$arguments = New-LlamaServerArguments -Settings $settings -LogPath $script:LogPath
$psi = [Diagnostics.ProcessStartInfo]::new()
$psi.FileName = $script:ServerPath
$psi.Arguments = ConvertTo-WindowsCommandLine $arguments
$psi.WorkingDirectory = $PSScriptRoot
$psi.UseShellExecute = $false
$psi.CreateNoWindow = $true
$script:ServerProcess = [Diagnostics.Process]::Start($psi)
```

Before start, reject an occupied endpoint using `Test-NetConnection -ComputerName $settings.Host -Port $settings.Port -InformationLevel Quiet -WarningAction SilentlyContinue`. Clear `launcher-server.log`, save config atomically, reset log offset, lock settings, and show `启动中`.

A 500 ms `DispatcherTimer` must:

- Read newly appended UTF-8 log bytes from the last offset, append complete text, cap the log box to the last 2,000 lines, and auto-scroll.
- Detect process exit, capture `ExitCode`, unlock settings, and show `已停止` or `启动失败（退出码 N）`.
- Start at most one pending `HttpClient.GetAsync("http://$host`:$port/health")` request per second; configure `HttpClient.Timeout = [TimeSpan]::FromSeconds(1)`.
- On successful status, marshal back through `$window.Dispatcher.Invoke`, show `运行中`, and enable address actions.

- [ ] **Step 6: Implement browser, clipboard, stop, and window-close behavior**

Use the exact addresses:

```powershell
$webUrl = "http://$($settings.Host):$($settings.Port)/"
$apiUrl = "http://$($settings.Host):$($settings.Port)/v1"
```

Open Web UI with `Start-Process $webUrl`; copy API base with `[Windows.Clipboard]::SetText($apiUrl)`.

`Stop-ManagedServer` must call `$script:ServerProcess.CloseMainWindow()`, wait up to 3,000 ms, then run `taskkill.exe /PID $script:ServerProcess.Id /T /F` only if still alive. During window closing, show a three-option `MessageBox` mapped as: Yes = stop and exit, No = leave server running and exit, Cancel = cancel window close. Never run `Get-Process llama-server | Stop-Process`.

- [ ] **Step 7: Run core and smoke tests**

Run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Launcher.Core.Tests.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Launcher.Smoke.Tests.ps1
```

Expected: both scripts print their green `passed` message and exit `0`.

- [ ] **Step 8: Commit checkpoint**

Expected: SKIP because the workspace is not a Git repository.

---

### Task 3: Double-Click Bootstrap and UI Smoke Verification

**Files:**
- Create: `启动Llama.cmd`
- Modify: `tests\Launcher.Smoke.Tests.ps1`

**Interfaces:**
- Consumes: `LlamaLauncher.ps1` in the same directory.
- Produces: a double-click entry that works from any current working directory.

- [ ] **Step 1: Extend smoke tests to require a safe CMD bootstrap**

Append:

```powershell
$cmdPath = Join-Path $root '启动Llama.cmd'
if (-not (Test-Path $cmdPath)) { throw 'CMD bootstrap is missing.' }
$cmd = Get-Content -LiteralPath $cmdPath -Raw
@('%~dp0','powershell.exe','-NoProfile','-STA','-ExecutionPolicy Bypass','LlamaLauncher.ps1') | ForEach-Object {
    if ($cmd -notmatch [regex]::Escape($_)) { throw "Bootstrap marker missing: $_" }
}
```

- [ ] **Step 2: Run smoke test and verify failure**

Run: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Launcher.Smoke.Tests.ps1`

Expected: FAIL with `CMD bootstrap is missing.`

- [ ] **Step 3: Create the CMD entry**

```bat
@echo off
setlocal
start "Llama Quick Launcher" powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File "%~dp0LlamaLauncher.ps1"
endlocal
```

- [ ] **Step 4: Run all automated tests**

Run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Launcher.Core.Tests.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Launcher.Smoke.Tests.ps1
```

Expected: both pass with exit code `0`.

- [ ] **Step 5: Launch the GUI without loading the model**

Run: `cmd.exe /c start "" ".\启动Llama.cmd"`

Verify manually/with window inspection: one centered `Llama 快速启动器` window appears; current GGUF path is selected; defaults display; advanced section expands/collapses; no console window remains open. Close without pressing Start.

- [ ] **Step 6: Commit checkpoint**

Expected: SKIP because the workspace is not a Git repository.

---

### Task 4: Real Server Integration and Failure Paths

**Files:**
- Modify only if verification reveals a defect: `LlamaLauncher.ps1`, `Launcher.Core.psm1`, or their focused test.
- Runtime generated: `launcher-config.json`, `launcher-server.log`.

**Interfaces:**
- Consumes the finished launcher and the existing GGUF model.
- Produces evidence that actual service start, health, Web UI, API base, stop, persistence, and error recovery work.

- [ ] **Step 1: Verify a cheap failure path before loading the model**

Temporarily enter port `70000` and press Start.

Expected: no server process starts; dialog contains `端口必须在 1 到 65535 之间。`; controls remain editable. Restore port `8080`.

- [ ] **Step 2: Verify port conflict handling**

Start a temporary listener in a separate managed background command on `127.0.0.1:8080`, press Start, then stop the temporary listener.

Expected: launcher reports that port `8080` is occupied and does not start `llama-server.exe`.

- [ ] **Step 3: Start the bundled model and wait for actual health**

Open `启动Llama.cmd` and press Start with the discovered GGUF model.

Expected: UI remains responsive and shows `启动中`; logs update; no loading deadline kills the process; eventually `/health` returns HTTP 200 and status changes to `运行中`.

- [ ] **Step 4: Verify Web UI and OpenAI-compatible API address**

Press `打开聊天页` and verify `http://127.0.0.1:8080/` loads. Press `复制 API 地址` and verify clipboard is exactly `http://127.0.0.1:8080/v1`.

- [ ] **Step 5: Stop and verify process-tree cleanup**

Press Stop, wait up to 5 seconds, and query the health endpoint and PID.

Expected: tracked PID no longer exists, `/health` is unreachable, port `8080` is free, controls are editable, and status is `已停止`.

- [ ] **Step 6: Verify configuration persistence and damaged-config recovery**

Set port `8081`, save by starting or closing through the normal path, reopen, and verify `8081` is restored. Back up `launcher-config.json`, replace it with invalid JSON, reopen, and verify defaults load with a log warning; restore the valid file afterward.

- [ ] **Step 7: Verify both close behaviors**

Start the server, close the launcher, choose Cancel and verify the window stays. Close again, choose No and verify the launcher exits while the server stays healthy. Reopen the launcher only to start a separately tracked instance is forbidden while the port is occupied; stop the preserved PID with the system PID command. Repeat start, close, choose Yes, and verify both launcher and server exit.

- [ ] **Step 8: Re-run regression tests after any integration fix**

Run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Launcher.Core.Tests.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Launcher.Smoke.Tests.ps1
```

Expected: both pass, with no leftover `llama-server.exe` process started by the test and ports `8080`/`8081` released.

- [ ] **Step 9: Final artifact check**

Confirm primary deliverables exist: `启动Llama.cmd`, `LlamaLauncher.ps1`, `Launcher.Core.psm1`, `tests\Launcher.Core.Tests.ps1`, `tests\Launcher.Smoke.Tests.ps1`, and the approved design/plan documents. Confirm runtime config and log files contain no credentials.

- [ ] **Step 10: Commit checkpoint**

Expected: SKIP because the workspace is not a Git repository.
