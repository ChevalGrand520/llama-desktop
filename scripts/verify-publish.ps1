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