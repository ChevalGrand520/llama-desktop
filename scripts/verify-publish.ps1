$ErrorActionPreference = 'Stop'
$out = Join-Path (Split-Path -Parent $PSScriptRoot) 'dist\LlamaDesktop'
foreach ($required in @('LlamaDesktop.exe','llama-server.exe','启动Llama.cmd','LlamaLauncher.ps1','Launcher.Core.psm1','WebView2Loader.dll')) {
    $hit = Get-ChildItem $out -Recurse -Filter $required -File -ErrorAction SilentlyContinue
    if (-not $hit) { throw "缺少发布文件：$required" }
}
$ggufCount = @(Get-ChildItem (Join-Path $out 'models') -Recurse -Filter '*.gguf' -File -ErrorAction SilentlyContinue).Count
if ($ggufCount -eq 0) { throw '发布目录中没有 GGUF 模型。' }
Write-Host "Publish verification OK. GGUF count: $ggufCount"
