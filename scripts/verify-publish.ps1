$ErrorActionPreference = 'Stop'
$out = Join-Path (Split-Path -Parent $PSScriptRoot) 'dist\LlamaDesktop'
foreach ($required in @('LlamaDesktop.exe','llama-server.exe','启动Llama.cmd','LlamaLauncher.ps1','Launcher.Core.psm1','WebView2Loader.dll')) {
    $hit = Get-ChildItem $out -Recurse -Filter $required -File -ErrorAction SilentlyContinue
    if (-not $hit) { throw "缺少发布文件：$required" }
}
$ggufCount = @(Get-ChildItem (Join-Path $out 'models') -Recurse -Filter '*.gguf' -File -ErrorAction SilentlyContinue).Count
if ($ggufCount -eq 0) { throw '发布目录中没有 GGUF 模型。' }
# Runtime gate: the published server binary must actually execute (loads its DLL closure).
$serverExe = Join-Path $out 'llama-server.exe'
# llama-server prints its version banner to stderr; under $ErrorActionPreference='Stop'
# the 2>&1 merge raises a terminating NativeCommandError, so relax EAP around the call.
$prevEap = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
$versionOutput = & $serverExe --version 2>&1 | Out-String
$ErrorActionPreference = $prevEap
if ($LASTEXITCODE -ne 0) { throw "llama-server 无法运行（DLL 缺失或损坏）：`n$versionOutput" }
Write-Host "Publish verification OK. GGUF count: $ggufCount"
