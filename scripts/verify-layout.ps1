param(
    [int]$TargetPid,
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

$rect = $window.Current.BoundingRectangle
"WINDOW: {0}x{1} at ({2},{3})" -f [int]$rect.Width, [int]$rect.Height, [int]$rect.X, [int]$rect.Y

$script:seen = @()
function Walk($el) {
    $name = $el.Current.Name
    $type = $el.Current.ControlType.ProgrammaticName -replace 'ControlType\.', ''
    $r = $el.Current.BoundingRectangle
    $fmt = { param($v) if ($v -is [double] -and ([double]::IsInfinity($v) -or [double]::IsNaN($v))) { '-' } else { [int]$v } }
    $line = ("{0}|{1}|x={2} y={3} w={4} h={5}" -f $type, $name, (& $fmt $r.X), (& $fmt $r.Y), (& $fmt $r.Width), (& $fmt $r.Height))
    $script:seen += $line
    $children = $el.FindAll([System.Windows.Automation.TreeScope]::Children,
        [System.Windows.Automation.Condition]::TrueCondition)
    foreach ($c in $children) { Walk $c }
}
Walk $window

$key = 'start','stop','copy','open','collapse','expand','log','model','Llama'
foreach ($k in $key) {
    $hit = $script:seen | Where-Object { $_ -match $k }
    if ($hit) { "FOUND [$k]: $($hit -join ' | ')" } else { "MISSING: $k" }
}
"---- full tree ----"
$script:seen | ForEach-Object { $_ }
