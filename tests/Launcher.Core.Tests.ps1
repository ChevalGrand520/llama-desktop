$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot '..\Launcher.Core.psm1') -Force

function Assert-Equal($Expected, $Actual, [string]$Message) {
    if ($Expected -ne $Actual) { throw "$Message`nExpected: $Expected`nActual: $Actual" }
}
function Assert-True([bool]$Value, [string]$Message) {
    if (-not $Value) { throw $Message }
}
function Assert-Throws([scriptblock]$Action, [string]$MessageLike) {
    $didThrow = $false
    try { & $Action }
    catch {
        $didThrow = $true
        if ($_.Exception.Message -notlike $MessageLike) { throw }
    }
    if (-not $didThrow) { throw 'Expected action to throw.' }
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
$wrongType = [pscustomobject]@{ ModelPath = $true; Host = 123; Port = '8080' }
$mergedWrong = Merge-LauncherConfig -Defaults $defaults -Saved $wrongType
Assert-Equal 'C:\models\a.gguf' $mergedWrong.ModelPath 'Non-string model fallback'
Assert-Equal '127.0.0.1' $mergedWrong.Host 'Non-string host fallback'
Assert-Equal 8080 $mergedWrong.Port 'Numeric string port accepted'

$tempModel = Join-Path ([IO.Path]::GetTempPath()) 'launcher-core-test.gguf'
[IO.File]::WriteAllBytes($tempModel, [byte[]]@(0))
$valid = Get-LauncherDefaults -LogicalProcessorCount 8 -DefaultModel $tempModel
$errors = @(Test-LauncherSettings -Settings $valid -ServerPath $PSCommandPath)
Assert-Equal 0 $errors.Count 'Valid settings'
$valid.Port = 70000
Assert-True ((Test-LauncherSettings -Settings $valid -ServerPath $PSCommandPath) -contains '端口必须在 1 到 65535 之间。') 'Port validation'
$invalid = Get-LauncherDefaults -LogicalProcessorCount 8 -DefaultModel ''
$invalid.ContextSize = 'bad'
$invalidErrors = @(Test-LauncherSettings -Settings $invalid -ServerPath $PSCommandPath)
Assert-True ($invalidErrors -contains '请选择有效的 GGUF 模型文件。') 'Empty model validation does not throw'
Assert-True ($invalidErrors -contains '上下文长度必须是大于 0 的整数。') 'Nonnumeric context validation does not throw'
$shapeErrors = @(Test-LauncherSettings -Settings ([pscustomobject]@{}) -ServerPath '')
Assert-True ($shapeErrors -contains '找不到 llama-server.exe。') 'Empty server path validation does not throw'
Assert-True ($shapeErrors -contains '请选择有效的 GGUF 模型文件。') 'Incomplete settings validation does not throw'
$valid.Host = 'not a host!'
Assert-True ((Test-LauncherSettings -Settings $valid -ServerPath $PSCommandPath) -contains '监听地址必须是 localhost 或有效的 IPv4/IPv6 地址。') 'Host validation'
$valid.Host = '127.0.0.1'

$split = @(Split-ExtraArguments '--verbose --alias "Local Model"')
Assert-Equal 3 $split.Count 'Quoted extra argument count'
Assert-Equal 'Local Model' $split[2] 'Quoted extra argument value'
$emptySplit = @(Split-ExtraArguments '--alias "" --verbose')
Assert-Equal 3 $emptySplit.Count 'Empty quoted argument count'
Assert-Equal '' $emptySplit[1] 'Empty quoted argument preserved'
Assert-Throws { Split-ExtraArguments '--alias "broken' } '*引号*'
Assert-Throws { New-LlamaServerArguments -Settings ([pscustomobject]@{
    ModelPath=$PSCommandPath;GpuLayers='all';ContextSize=8192;Threads=8;Host='127.0.0.1';Port=8080
    BatchSize=2048;Parallel=1;FlashAttention=$true;MemoryMap=$true;MemoryLock=$false
    ExtraArguments='--port 9999'
}) -LogPath 'C:\tmp\server.log' } '*受启动器管理*'

Assert-Throws { New-LlamaServerArguments -Settings ([pscustomobject]@{
    ModelPath=$tempModel;GpuLayers='all';ContextSize=8192;Threads=8;Host='127.0.0.1';Port=8080
    BatchSize=2048;Parallel=1;FlashAttention=$true;MemoryMap=$true;MemoryLock=$false
    ExtraArguments='--api-key secret-value'
}) -LogPath 'C:\tmp\server.log' } '*敏感参数*'

$q = ConvertTo-WindowsCommandLine @('plain', 'C:\model files\x.gguf', 'a"b', '')
Assert-Equal 'plain "C:\model files\x.gguf" "a\"b" ""' $q 'Windows quoting'

$args = @(New-LlamaServerArguments -Settings ([pscustomobject]@{
    ModelPath='C:\model files\x.gguf';GpuLayers='all';ContextSize=8192;Threads=8
    Host='127.0.0.1';Port=8080;BatchSize=1024;Parallel=2;FlashAttention=$true
    MemoryMap=$true;MemoryLock=$true;ExtraArguments='--verbose'
}) -LogPath 'C:\tmp\server.log')
Assert-Equal 'mmap+mlock' $args[[Array]::IndexOf($args, '--load-mode') + 1] 'Load mode mapping'
Assert-True ($args -contains '--log-file') 'Log file argument'
Assert-True ($args -contains '--verbose') 'Extra argument'
Remove-Item -LiteralPath $tempModel -Force -ErrorAction SilentlyContinue
Write-Host 'Launcher.Core tests passed.' -ForegroundColor Green
