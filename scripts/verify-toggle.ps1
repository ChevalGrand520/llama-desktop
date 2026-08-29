param(
    [int]$TargetPid,
    [int]$Clicks = 1,
    [int]$WaitSeconds = 8
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$root = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $TargetPid)
$window = $null
$deadline = [datetime]::Now.AddSeconds($WaitSeconds)
while ([datetime]::Now -lt $deadline) {
    $window = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
    if ($window) { break }
    Start-Sleep -Milliseconds 300
}
if (-not $window) { throw "window not found (PID $TargetPid)" }

function Get-Snapshot {
    $script:rows = @()
    $walk = {
        param($el)
        $name = $el.Current.Name
        $type = $el.Current.ControlType.ProgrammaticName -replace 'ControlType\.', ''
        $r = $el.Current.BoundingRectangle
        $fx = if ($r.X -is [double] -and [double]::IsInfinity($r.X)) { -1 } else { [int]$r.X }
        $fw = if ($r.Width -is [double] -and [double]::IsInfinity($r.Width)) { -1 } else { [int]$r.Width }
        $line = "{0}|{1}|x={2} w={3}" -f $type, $name, $fx, $fw
        $script:rows += $line
        foreach ($c in $el.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)) { & $walk $c }
    }
    & $walk $window
    return $script:rows
}

"== BEFORE =="
$before = Get-Snapshot
($before | Where-Object { $_ -match '启动服务|停止服务|启动|GGUF|实时日志' }) | ForEach-Object { $_ }

# Find the unnamed toggle button (last Button in top bar, empty name) and invoke it.
$buttons = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Button)))
$toggle = $null
$maxX = -1
foreach ($b in $buttons) {
    $r = $b.Current.BoundingRectangle
    if ($r.X -is [double] -and -not [double]::IsInfinity($r.X) -and $r.X -gt $maxX) { $maxX = $r.X; $toggle = $b }
}
if ($toggle) {
    $invoke = $toggle.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    for ($i = 0; $i -lt $Clicks; $i++) {
        $invoke.Invoke()
        Start-Sleep -Milliseconds 600
    }
    "TOGGLE_CLICKED x$Clicks"
} else {
    "TOGGLE_BUTTON_NOT_FOUND"
}

"== AFTER =="
$after = Get-Snapshot
($after | Where-Object { $_ -match '启动服务|停止服务|启动|GGUF|实时日志' }) | ForEach-Object { $_ }
"== WEB PANE =="
($after | Where-Object { $_ -match 'about:blank' }) | Select-Object -First 1 | ForEach-Object { $_ }
