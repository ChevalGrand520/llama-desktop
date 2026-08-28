$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$scriptPath = Join-Path $root 'LlamaLauncher.ps1'
if (-not (Test-Path -LiteralPath $scriptPath -PathType Leaf)) {
    throw 'LlamaLauncher.ps1 is missing.'
}

$tokens = $null
$errors = $null
[void][Management.Automation.Language.Parser]::ParseFile($scriptPath, [ref]$tokens, [ref]$errors)
if ($errors.Count -gt 0) { throw ($errors | Out-String) }

$text = Get-Content -LiteralPath $scriptPath -Raw
$markers = @(
    'llama-server.exe', 'launcher-config.json', 'launcher-server.log',
    'New-LlamaServerArguments', 'taskkill.exe', '/health', 'ShowDialog',
    'ModelPath', 'GpuLayers', 'ContextSize', 'Threads', 'Host', 'Port',
    'BatchSize', 'Parallel', 'FlashAttention', 'MemoryMap', 'MemoryLock',
    'ExtraArguments', 'StartStopButton', 'OpenUiButton', 'CopyApiButton',
    'StatusText', 'LogText'
)
foreach ($marker in $markers) {
    if ($text -notmatch [regex]::Escape($marker)) {
        throw "Expected launcher marker missing: $marker"
    }
}

$cmdPath = Join-Path $root '启动Llama.cmd'
if (-not (Test-Path -LiteralPath $cmdPath -PathType Leaf)) { throw 'CMD bootstrap is missing.' }
$cmd = Get-Content -LiteralPath $cmdPath -Raw
$expectedCommand = 'start "Llama Quick Launcher" powershell.exe -NoProfile -STA -WindowStyle Hidden -ExecutionPolicy Bypass -File "%~dp0LlamaLauncher.ps1"'
if ($cmd -notmatch [regex]::Escape($expectedCommand)) {
    throw 'Bootstrap command does not safely quote the launcher path or hide the PowerShell host.'
}

Write-Host 'Launcher smoke tests passed.' -ForegroundColor Green

$launcherSource = Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\LlamaLauncher.ps1') -Raw
if ($launcherSource -notmatch 'Get-ChildItem -LiteralPath \$directory -Filter ''\*\.gguf'' -Recurse') {
    throw 'Find-Models must scan recursively for .gguf under models\.'
}
if ($launcherSource -notmatch 'Modelfile') {
    throw 'Find-Models must exclude non-weight files such as Modelfile.'
}