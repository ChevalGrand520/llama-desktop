Set-StrictMode -Version Latest

$script:SensitiveOptions = @('--api-key', '--api-key-file', '--hf-token')

$script:ManagedOptions = @(
    '--model', '-m', '--gpu-layers', '--n-gpu-layers', '-ngl',
    '--ctx-size', '-c', '--threads', '-t', '--host', '--port',
    '--batch-size', '-b', '--parallel', '-np', '--flash-attn', '-fa',
    '--load-mode', '-lm', '--log-file'
)

function Get-LauncherDefaults {
    [CmdletBinding()]
    param(
        [int]$LogicalProcessorCount,
        [AllowEmptyString()][string]$DefaultModel
    )

    [pscustomobject][ordered]@{
        ModelPath       = $DefaultModel
        GpuLayers       = 'all'
        ContextSize     = 8192
        Threads         = [Math]::Max(1, $LogicalProcessorCount)
        Host            = '127.0.0.1'
        Port            = 8080
        BatchSize       = 2048
        Parallel        = 1
        FlashAttention  = $true
        MemoryMap       = $true
        MemoryLock      = $false
        ExtraArguments  = ''
    }
}

function Merge-LauncherConfig {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Defaults,
        $Saved
    )

    $result = [ordered]@{}
    foreach ($property in $Defaults.PSObject.Properties) {
        $value = $property.Value
        if ($null -ne $Saved -and $Saved.PSObject.Properties.Name -contains $property.Name) {
            $candidate = $Saved.$($property.Name)
            try {
                if ($value -is [bool]) {
                    if ($candidate -isnot [bool]) { throw 'Invalid Boolean value.' }
                    $value = $candidate
                }
                elseif ($value -is [int]) {
                    if ($candidate -is [bool]) { throw 'Invalid integer value.' }
                    $value = [int]$candidate
                }
                else {
                    if ($candidate -isnot [string]) { throw 'Invalid string value.' }
                    $value = $candidate
                }
            }
            catch {
                $value = $property.Value
            }
        }
        $result[$property.Name] = $value
    }

    [pscustomobject]$result
}

function Test-LauncherSettings {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Settings,
        [Parameter(Mandatory)][AllowEmptyString()][string]$ServerPath
    )

    $errors = New-Object 'System.Collections.Generic.List[string]'
    $readSetting = {
        param([string]$Name)
        $property = $Settings.PSObject.Properties[$Name]
        if ($null -eq $property) { return $null }
        $property.Value
    }

    if ([string]::IsNullOrWhiteSpace($ServerPath) -or
        -not (Test-Path -LiteralPath $ServerPath -PathType Leaf)) {
        $errors.Add('找不到 llama-server.exe。')
    }
    $modelPath = [string](& $readSetting 'ModelPath')
    if ([string]::IsNullOrWhiteSpace($modelPath) -or
        -not (Test-Path -LiteralPath $modelPath -PathType Leaf) -or
        [IO.Path]::GetExtension($modelPath) -ine '.gguf') {
        $errors.Add('请选择有效的 GGUF 模型文件。')
    }
    $gpuLayers = [string](& $readSetting 'GpuLayers')
    if ($gpuLayers -notmatch '^(all|auto|0|[1-9][0-9]*)$') {
        $errors.Add('GPU 层数必须是 all、auto 或非负整数。')
    }

    $parsed = 0
    if (-not [int]::TryParse([string](& $readSetting 'ContextSize'), [ref]$parsed) -or $parsed -lt 1) {
        $errors.Add('上下文长度必须是大于 0 的整数。')
    }
    if (-not [int]::TryParse([string](& $readSetting 'Threads'), [ref]$parsed) -or $parsed -lt 1) {
        $errors.Add('CPU 线程数必须是大于 0 的整数。')
    }
    if (-not [int]::TryParse([string](& $readSetting 'Port'), [ref]$parsed) -or $parsed -lt 1 -or $parsed -gt 65535) {
        $errors.Add('端口必须在 1 到 65535 之间。')
    }
    if (-not [int]::TryParse([string](& $readSetting 'BatchSize'), [ref]$parsed) -or $parsed -lt 1) {
        $errors.Add('批大小必须是大于 0 的整数。')
    }
    if (-not [int]::TryParse([string](& $readSetting 'Parallel'), [ref]$parsed) -or $parsed -lt 1) {
        $errors.Add('并行请求数必须是大于 0 的整数。')
    }
    $hostValue = [string](& $readSetting 'Host')
    $parsedAddress = $null
    if ([string]::IsNullOrWhiteSpace($hostValue) -or
        ($hostValue -ine 'localhost' -and -not [Net.IPAddress]::TryParse($hostValue, [ref]$parsedAddress))) {
        $errors.Add('监听地址必须是 localhost 或有效的 IPv4/IPv6 地址。')
    }

    [string[]]$errors.ToArray()
}

function Split-ExtraArguments {
    [CmdletBinding()]
    param([AllowEmptyString()][string]$Text)

    if ([string]::IsNullOrWhiteSpace($Text)) { return [string[]]@() }

    $result = New-Object 'System.Collections.Generic.List[string]'
    $current = New-Object Text.StringBuilder
    $quoted = $false
    $tokenStarted = $false
    $backslashes = 0

    for ($i = 0; $i -lt $Text.Length; $i++) {
        $character = $Text[$i]
        if ($character -eq '\') {
            $tokenStarted = $true
            $backslashes++
            continue
        }
        if ($character -eq '"') {
            $tokenStarted = $true
            if (($backslashes % 2) -eq 0) {
                [void]$current.Append(('\' * [int]($backslashes / 2)))
                $quoted = -not $quoted
            }
            else {
                [void]$current.Append(('\' * [int](($backslashes - 1) / 2)))
                [void]$current.Append('"')
            }
            $backslashes = 0
            continue
        }
        if ($backslashes -gt 0) {
            [void]$current.Append(('\' * $backslashes))
            $backslashes = 0
        }
        if ([char]::IsWhiteSpace($character) -and -not $quoted) {
            if ($tokenStarted) {
                $result.Add($current.ToString())
                [void]$current.Clear()
                $tokenStarted = $false
            }
        }
        else {
            $tokenStarted = $true
            [void]$current.Append($character)
        }
    }

    if ($backslashes -gt 0) { [void]$current.Append(('\' * $backslashes)) }
    if ($quoted) { throw '额外参数中的引号没有闭合。' }
    if ($tokenStarted) { $result.Add($current.ToString()) }
    [string[]]$result.ToArray()
}

function ConvertTo-WindowsCommandLine {
    [CmdletBinding()]
    param([Parameter(Mandatory)][AllowEmptyCollection()][AllowEmptyString()][string[]]$Arguments)

    $quotedArguments = foreach ($argument in $Arguments) {
        if ($null -eq $argument -or $argument.Length -eq 0) {
            '""'
            continue
        }
        if ($argument -notmatch '[\s"]') {
            $argument
            continue
        }

        $builder = New-Object Text.StringBuilder
        [void]$builder.Append('"')
        $backslashes = 0
        foreach ($character in $argument.ToCharArray()) {
            if ($character -eq '\') {
                $backslashes++
            }
            elseif ($character -eq '"') {
                [void]$builder.Append(('\' * ($backslashes * 2 + 1)))
                [void]$builder.Append('"')
                $backslashes = 0
            }
            else {
                if ($backslashes -gt 0) { [void]$builder.Append(('\' * $backslashes)) }
                [void]$builder.Append($character)
                $backslashes = 0
            }
        }
        if ($backslashes -gt 0) { [void]$builder.Append(('\' * ($backslashes * 2))) }
        [void]$builder.Append('"')
        $builder.ToString()
    }

    $quotedArguments -join ' '
}

function New-LlamaServerArguments {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Settings,
        [Parameter(Mandatory)][string]$LogPath
    )

    $extra = @(Split-ExtraArguments -Text ([string]$Settings.ExtraArguments))
    foreach ($token in $extra) {
        $optionName = if ($token -match '^([^=]+)=') { $Matches[1] } else { $token }
        if ($script:SensitiveOptions -contains $optionName.ToLowerInvariant()) {
            throw "敏感参数 $optionName 不允许写入启动器配置；请改用环境变量或外部受保护文件。"
        }
        if ($script:ManagedOptions -contains $optionName.ToLowerInvariant()) {
            throw "额外参数 $optionName 受启动器管理，不能重复指定。"
        }
    }

    $loadMode = if ([bool]$Settings.MemoryMap -and [bool]$Settings.MemoryLock) {
        'mmap+mlock'
    }
    elseif ([bool]$Settings.MemoryMap) { 'mmap' }
    elseif ([bool]$Settings.MemoryLock) { 'mlock' }
    else { 'none' }

    [string[]]$baseArguments = @(
        '--model', [string]$Settings.ModelPath,
        '--gpu-layers', [string]$Settings.GpuLayers,
        '--ctx-size', [string]$Settings.ContextSize,
        '--threads', [string]$Settings.Threads,
        '--host', [string]$Settings.Host,
        '--port', [string]$Settings.Port,
        '--batch-size', [string]$Settings.BatchSize,
        '--parallel', [string]$Settings.Parallel,
        '--flash-attn', $(if ([bool]$Settings.FlashAttention) { 'on' } else { 'off' }),
        '--load-mode', $loadMode,
        '--log-file', $LogPath,
        '--log-timestamps'
    )

    [string[]]@($baseArguments + $extra)
}

Export-ModuleMember -Function @(
    'Get-LauncherDefaults', 'Merge-LauncherConfig', 'Test-LauncherSettings',
    'Split-ExtraArguments', 'ConvertTo-WindowsCommandLine', 'New-LlamaServerArguments'
)
