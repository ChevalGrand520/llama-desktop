Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase, System.Xaml, System.Net.Http
Import-Module (Join-Path $PSScriptRoot 'Launcher.Core.psm1') -Force

$script:ServerPath = Join-Path $PSScriptRoot 'llama-server.exe'
$script:ConfigPath = Join-Path $PSScriptRoot 'launcher-config.json'
$script:LogPath = Join-Path $PSScriptRoot 'launcher-server.log'
$script:ServerProcess = $null
$script:CurrentSettings = $null
$script:StopRequested = $false
$script:LogOffset = 0L
$script:HealthTask = $null
$script:LastHealthAttempt = [datetime]::MinValue
$script:IsReady = $false
$script:IsClosing = $false
$script:StopInProgress = $false
$script:StopDeadline = [datetime]::MinValue
$script:StopPhase = 'none'
$script:ForceDeadline = [datetime]::MinValue
$script:TaskkillProcess = $null
$script:CloseAfterStop = $false
$script:LogDecoder = [Text.Encoding]::UTF8.GetDecoder()

[xml]$xaml = @'
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Llama 快速启动器" Width="880" Height="740" MinWidth="720" MinHeight="600"
        WindowStartupLocation="CenterScreen" Background="#F5F6F8" FontFamily="Segoe UI">
  <Window.Resources>
    <Style TargetType="TextBox"><Setter Property="MinHeight" Value="30"/><Setter Property="Padding" Value="7,4"/><Setter Property="VerticalContentAlignment" Value="Center"/></Style>
    <Style TargetType="ComboBox"><Setter Property="MinHeight" Value="30"/><Setter Property="Padding" Value="5,3"/></Style>
    <Style TargetType="Button"><Setter Property="MinHeight" Value="32"/><Setter Property="Padding" Value="12,5"/></Style>
  </Window.Resources>
  <Grid Margin="20">
    <Grid.RowDefinitions><RowDefinition Height="Auto"/><RowDefinition Height="Auto"/><RowDefinition Height="Auto"/><RowDefinition Height="*"/></Grid.RowDefinitions>
    <Grid x:Name="SettingsPanel" Grid.Row="0">
      <Grid.ColumnDefinitions><ColumnDefinition Width="120"/><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
      <Grid.RowDefinitions><RowDefinition Height="Auto"/><RowDefinition Height="Auto"/><RowDefinition Height="Auto"/><RowDefinition Height="Auto"/></Grid.RowDefinitions>
      <TextBlock Grid.Row="0" Text="GGUF 模型" VerticalAlignment="Center" Margin="0,0,12,10"/><ComboBox x:Name="ModelPath" Grid.Row="0" Grid.Column="1" IsEditable="True" Margin="0,0,8,10"/><Button x:Name="BrowseModel" Grid.Row="0" Grid.Column="2" Content="浏览..." MinWidth="78" Margin="0,0,0,10"/>
      <TextBlock Grid.Row="1" Text="GPU 层数" VerticalAlignment="Center" Margin="0,0,12,10"/><TextBox x:Name="GpuLayers" Grid.Row="1" Grid.Column="1" Margin="0,0,8,10"/><TextBlock Grid.Row="1" Grid.Column="2" Text="all / auto / 数字" Foreground="#5F6368" VerticalAlignment="Center" Margin="0,0,0,10"/>
      <TextBlock Grid.Row="2" Text="上下文 / 线程" VerticalAlignment="Center" Margin="0,0,12,10"/><Grid Grid.Row="2" Grid.Column="1" Grid.ColumnSpan="2" Margin="0,0,0,10"><Grid.ColumnDefinitions><ColumnDefinition/><ColumnDefinition Width="12"/><ColumnDefinition/></Grid.ColumnDefinitions><TextBox x:Name="ContextSize" Grid.Column="0" ToolTip="上下文长度"/><TextBox x:Name="Threads" Grid.Column="2" ToolTip="CPU 线程数"/></Grid>
      <TextBlock Grid.Row="3" Text="地址 / 端口" VerticalAlignment="Center" Margin="0,0,12,0"/><Grid Grid.Row="3" Grid.Column="1" Grid.ColumnSpan="2"><Grid.ColumnDefinitions><ColumnDefinition/><ColumnDefinition Width="12"/><ColumnDefinition Width="150"/></Grid.ColumnDefinitions><TextBox x:Name="Host" Grid.Column="0" ToolTip="监听地址"/><TextBox x:Name="Port" Grid.Column="2" ToolTip="服务端口"/></Grid>
    </Grid>
    <Expander x:Name="AdvancedExpander" Grid.Row="1" Header="高级设置" Margin="0,16,0,12">
      <Grid Margin="0,10,0,0">
        <Grid.ColumnDefinitions><ColumnDefinition Width="120"/><ColumnDefinition Width="*"/><ColumnDefinition Width="24"/><ColumnDefinition Width="120"/><ColumnDefinition Width="*"/></Grid.ColumnDefinitions>
        <Grid.RowDefinitions><RowDefinition Height="Auto"/><RowDefinition Height="Auto"/><RowDefinition Height="Auto"/></Grid.RowDefinitions>
        <TextBlock Text="批大小" VerticalAlignment="Center" Margin="0,0,12,10"/><TextBox x:Name="BatchSize" Grid.Column="1" Margin="0,0,0,10"/><TextBlock Grid.Column="3" Text="并行请求数" VerticalAlignment="Center" Margin="0,0,12,10"/><TextBox x:Name="Parallel" Grid.Column="4" Margin="0,0,0,10"/>
        <StackPanel Grid.Row="1" Grid.ColumnSpan="5" Orientation="Horizontal" Margin="0,0,0,10"><CheckBox x:Name="FlashAttention" Content="Flash Attention" Margin="0,0,24,0"/><CheckBox x:Name="MemoryMap" Content="内存映射" Margin="0,0,24,0"/><CheckBox x:Name="MemoryLock" Content="锁定内存"/></StackPanel>
        <TextBlock Grid.Row="2" Text="额外参数" VerticalAlignment="Center" Margin="0,0,12,0"/><TextBox x:Name="ExtraArguments" Grid.Row="2" Grid.Column="1" Grid.ColumnSpan="4" ToolTip="不能重复指定模型、地址、端口等受管理参数"/>
      </Grid>
    </Expander>
    <Grid Grid.Row="2" Height="46" Margin="0,0,0,12">
      <Grid.ColumnDefinitions><ColumnDefinition Width="Auto"/><ColumnDefinition Width="Auto"/><ColumnDefinition Width="Auto"/><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
      <Button x:Name="StartStopButton" Content="启动服务" Width="112" Margin="0,0,8,0"/><Button x:Name="OpenUiButton" Grid.Column="1" Content="打开聊天页" Width="112" Margin="0,0,8,0" IsEnabled="False"/><Button x:Name="CopyApiButton" Grid.Column="2" Content="复制 API 地址" Width="124" IsEnabled="False"/><TextBlock x:Name="StatusText" Grid.Column="4" Text="未运行" VerticalAlignment="Center" FontWeight="SemiBold" Foreground="#374151"/>
    </Grid>
    <TextBox x:Name="LogText" Grid.Row="3" IsReadOnly="True" AcceptsReturn="True" VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Auto" FontFamily="Consolas" FontSize="12" Background="#111827" Foreground="#E5E7EB" BorderThickness="0" Padding="10" TextWrapping="NoWrap"/>
  </Grid>
</Window>
'@

$reader = New-Object System.Xml.XmlNodeReader $xaml
$window = [Windows.Markup.XamlReader]::Load($reader)
$controlNames = @('SettingsPanel','ModelPath','BrowseModel','GpuLayers','ContextSize','Threads','Host','Port','AdvancedExpander','BatchSize','Parallel','FlashAttention','MemoryMap','MemoryLock','ExtraArguments','StartStopButton','OpenUiButton','CopyApiButton','StatusText','LogText')
$controls = @{}
foreach ($name in $controlNames) { $control = $window.FindName($name); if ($null -eq $control) { throw "界面控件加载失败：$name" }; $controls[$name] = $control }

function Add-LogLine {
    param([AllowEmptyString()][string]$Text)
    $controls.LogText.AppendText("[$([datetime]::Now.ToString('HH:mm:ss'))] $Text`r`n")
    $lines = $controls.LogText.Text -split "`r?`n"
    if ($lines.Count -gt 2000) { $controls.LogText.Text = ($lines[($lines.Count - 2001)..($lines.Count - 1)] -join "`r`n"); $controls.LogText.CaretIndex = $controls.LogText.Text.Length }
    $controls.LogText.ScrollToEnd()
}

function Find-Models {
    $directory = Join-Path $PSScriptRoot 'models'
    if (-not (Test-Path -LiteralPath $directory -PathType Container)) { return @() }
    @(Get-ChildItem -LiteralPath $directory -Filter '*.gguf' -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -notlike 'Modelfile.*' } |
        Sort-Object Name | Select-Object -ExpandProperty FullName)
}

function Read-LauncherConfig {
    if (-not (Test-Path -LiteralPath $script:ConfigPath -PathType Leaf)) { return $null }
    try { Get-Content -LiteralPath $script:ConfigPath -Raw -Encoding UTF8 | ConvertFrom-Json }
    catch { Add-LogLine "配置读取失败，已使用默认值：$($_.Exception.Message)"; $null }
}

function Save-LauncherConfig {
    param($Settings)
    $temporaryPath = "$script:ConfigPath.tmp"
    try { $Settings | ConvertTo-Json | Set-Content -LiteralPath $temporaryPath -Encoding UTF8; Move-Item -LiteralPath $temporaryPath -Destination $script:ConfigPath -Force }
    finally { if (Test-Path -LiteralPath $temporaryPath) { Remove-Item -LiteralPath $temporaryPath -Force -ErrorAction SilentlyContinue } }
}

function Set-ControlsFromSettings {
    param($Settings)
    $controls.ModelPath.Text=[string]$Settings.ModelPath; $controls.GpuLayers.Text=[string]$Settings.GpuLayers; $controls.ContextSize.Text=[string]$Settings.ContextSize; $controls.Threads.Text=[string]$Settings.Threads
    $controls.Host.Text=[string]$Settings.Host; $controls.Port.Text=[string]$Settings.Port; $controls.BatchSize.Text=[string]$Settings.BatchSize; $controls.Parallel.Text=[string]$Settings.Parallel
    $controls.FlashAttention.IsChecked=[bool]$Settings.FlashAttention; $controls.MemoryMap.IsChecked=[bool]$Settings.MemoryMap; $controls.MemoryLock.IsChecked=[bool]$Settings.MemoryLock; $controls.ExtraArguments.Text=[string]$Settings.ExtraArguments
}

function Assert-SavedConfigWarnings {
    param($Defaults,$Saved)
    if($null -eq $Saved){return}
    foreach($property in $Defaults.PSObject.Properties){
        if(-not($Saved.PSObject.Properties.Name -contains $property.Name)){Add-LogLine "配置缺少字段 $($property.Name)，已使用默认值。";continue}
        $candidate=$Saved.$($property.Name);$parsedTemp=0
        if($property.Value -is [bool] -and $candidate -isnot [bool]){Add-LogLine "配置字段 $($property.Name) 类型无效，已使用默认值。"}
        elseif($property.Value -is [int] -and ($candidate -is [bool] -or -not [int]::TryParse([string]$candidate,[ref]$parsedTemp))){Add-LogLine "配置字段 $($property.Name) 无效，已使用默认值。"}
        elseif($property.Value -is [string] -and $candidate -isnot [string]){Add-LogLine "配置字段 $($property.Name) 类型无效，已使用默认值。"}
    }
}

function Get-SettingsFromControls {
    [pscustomobject][ordered]@{ ModelPath=[string]$controls.ModelPath.Text.Trim(); GpuLayers=[string]$controls.GpuLayers.Text.Trim().ToLowerInvariant(); ContextSize=[string]$controls.ContextSize.Text.Trim(); Threads=[string]$controls.Threads.Text.Trim(); Host=[string]$controls.Host.Text.Trim(); Port=[string]$controls.Port.Text.Trim(); BatchSize=[string]$controls.BatchSize.Text.Trim(); Parallel=[string]$controls.Parallel.Text.Trim(); FlashAttention=[bool]$controls.FlashAttention.IsChecked; MemoryMap=[bool]$controls.MemoryMap.IsChecked; MemoryLock=[bool]$controls.MemoryLock.IsChecked; ExtraArguments=[string]$controls.ExtraArguments.Text }
}

function Set-RunningState {
    param([bool]$Running,[string]$Status)
    $controls.SettingsPanel.IsEnabled=-not $Running; $controls.AdvancedExpander.IsEnabled=-not $Running; $controls.StartStopButton.Content=if($Running){'停止服务'}else{'启动服务'}; $controls.StatusText.Text=$Status; $controls.OpenUiButton.IsEnabled=$Running -and $script:IsReady; $controls.CopyApiButton.IsEnabled=$Running -and $script:IsReady
}

function Show-ValidationErrors {
    param([string[]]$Errors)
    $message=$Errors -join "`r`n"; Add-LogLine $message; [Windows.MessageBox]::Show($window,$message,'无法启动',[Windows.MessageBoxButton]::OK,[Windows.MessageBoxImage]::Warning)|Out-Null
}

function Get-ConnectHost {
    param([string]$BindHost)
    if($BindHost -eq '0.0.0.0'){return '127.0.0.1'}
    if($BindHost -eq '::' -or $BindHost -eq '[::]'){return '::1'}
    $BindHost
}

function Get-ServiceBaseUrl {
    param($Settings)
    $connectHost=Get-ConnectHost ([string]$Settings.Host)
    $address=$null
    if([Net.IPAddress]::TryParse($connectHost,[ref]$address) -and $address.AddressFamily -eq [Net.Sockets.AddressFamily]::InterNetworkV6){$connectHost="[$connectHost]"}
    "http://$connectHost`:$($Settings.Port)"
}

function Test-PortOccupied {
    param([string]$HostName,[int]$PortNumber)
    try {
        [bool](Test-NetConnection -ComputerName $HostName -Port $PortNumber -InformationLevel Quiet -WarningAction SilentlyContinue)
    }
    catch { $false }
}

function Read-NewServerLog {
    if(-not(Test-Path -LiteralPath $script:LogPath -PathType Leaf)){return}
    try {
        $stream=New-Object IO.FileStream($script:LogPath,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::ReadWrite)
        try {
            if($script:LogOffset -gt $stream.Length){$script:LogOffset=0;$script:LogDecoder.Reset()}
            [void]$stream.Seek($script:LogOffset,[IO.SeekOrigin]::Begin)
            $available=[Math]::Min(65536,[int]($stream.Length-$script:LogOffset))
            if($available -le 0){return}
            $bytes=New-Object byte[] $available
            $read=$stream.Read($bytes,0,$available)
            $script:LogOffset += $read
        } finally {$stream.Dispose()}
        if($read -gt 0){
            $charCount=$script:LogDecoder.GetCharCount($bytes,0,$read,$false)
            if($charCount -gt 0){$chars=New-Object char[] $charCount;[void]$script:LogDecoder.GetChars($bytes,0,$read,$chars,0,$false);$controls.LogText.AppendText((-join $chars));$lines=$controls.LogText.Text -split "`r?`n";if($lines.Count -gt 2000){$controls.LogText.Text=($lines[($lines.Count-2001)..($lines.Count-1)] -join "`r`n");$controls.LogText.CaretIndex=$controls.LogText.Text.Length};$controls.LogText.ScrollToEnd()}
        }
    } catch {Add-LogLine "读取服务日志失败：$($_.Exception.Message)"}
}

function Stop-ManagedServer {
    if($null -eq $script:ServerProcess -or $script:StopInProgress){return}
    $script:StopRequested=$true;$script:StopInProgress=$true;$script:StopPhase='graceful';$script:StopDeadline=[datetime]::Now.AddSeconds(3);$script:ForceDeadline=[datetime]::MinValue;$script:TaskkillProcess=$null
    Set-RunningState -Running $true -Status '正在停止'
    try{if(-not $script:ServerProcess.HasExited){[void]$script:ServerProcess.CloseMainWindow()}}catch{Add-LogLine "请求停止服务时出错：$($_.Exception.Message)"}
}

function Start-ForceKill {
    param([bool]$Force)
    try{
        $killInfo=New-Object Diagnostics.ProcessStartInfo;$killInfo.FileName='taskkill.exe'
        $killInfo.Arguments=if($Force){"/PID $($script:ServerProcess.Id) /T /F"}else{"/PID $($script:ServerProcess.Id) /T"}
        $killInfo.UseShellExecute=$false;$killInfo.CreateNoWindow=$true
        $script:TaskkillProcess=[Diagnostics.Process]::Start($killInfo)
        $script:ForceDeadline=[datetime]::Now.AddSeconds(5)
        Add-LogLine $(if($Force){'正在强制终止服务进程树...'}else{'正在请求终止服务进程树...'})
    }catch{Add-LogLine "无法启动 taskkill：$($_.Exception.Message)";$script:StopInProgress=$false;$script:CloseAfterStop=$false;Set-RunningState -Running $true -Status '停止失败'}
}

function Complete-StopFailure {
    Add-LogLine "服务进程未能停止，请检查后重试。";$script:StopInProgress=$false;$script:CloseAfterStop=$false;Set-RunningState -Running $true -Status '停止失败'
}

function Start-ManagedServer {
    $settings=Get-SettingsFromControls; $validationErrors=@(Test-LauncherSettings -Settings $settings -ServerPath $script:ServerPath);if($validationErrors.Count -gt 0){Show-ValidationErrors $validationErrors;return}
    try{$arguments=@(New-LlamaServerArguments -Settings $settings -LogPath $script:LogPath)}catch{Show-ValidationErrors @($_.Exception.Message);return}
    if(Test-PortOccupied -HostName $settings.Host -PortNumber ([int]$settings.Port)){Show-ValidationErrors @("端口 $($settings.Port) 已被占用。");return}
    try { Save-LauncherConfig $settings;Set-Content -LiteralPath $script:LogPath -Value '' -Encoding UTF8;$script:LogOffset=0L;$script:LogDecoder.Reset();$script:IsReady=$false;$script:StopRequested=$false;$script:StopInProgress=$false;$script:StopPhase='none';$script:TaskkillProcess=$null;$script:CloseAfterStop=$false;$script:HealthTask=$null;$script:LastHealthAttempt=[datetime]::MinValue;$script:CurrentSettings=$settings
        $startInfo=New-Object Diagnostics.ProcessStartInfo;$startInfo.FileName=$script:ServerPath;$startInfo.Arguments=ConvertTo-WindowsCommandLine -Arguments $arguments;$startInfo.WorkingDirectory=$PSScriptRoot;$startInfo.UseShellExecute=$false;$startInfo.CreateNoWindow=$true;$script:ServerProcess=[Diagnostics.Process]::Start($startInfo);Add-LogLine "已启动 llama-server，PID：$($script:ServerProcess.Id)";Set-RunningState -Running $true -Status '启动中'
    } catch {$script:ServerProcess=$null;Set-RunningState -Running $false -Status '启动失败';Show-ValidationErrors @("启动服务失败：$($_.Exception.Message)")}
}

$script:HealthClient=New-Object Net.Http.HttpClient;$script:HealthClient.Timeout=[timespan]::FromSeconds(1)
$timer=New-Object Windows.Threading.DispatcherTimer;$timer.Interval=[timespan]::FromMilliseconds(500)
$timer.Add_Tick({
    Read-NewServerLog;if($null -eq $script:ServerProcess){return}
    if($script:ServerProcess.HasExited){$exitCode=$script:ServerProcess.ExitCode;$wasStopped=$script:StopRequested;$wasReady=$script:IsReady;Add-LogLine "llama-server 已退出，退出码：$exitCode";$script:ServerProcess.Dispose();$script:ServerProcess=$null;$script:HealthTask=$null;$script:IsReady=$false;$script:StopInProgress=$false;if($null -ne $script:TaskkillProcess){$script:TaskkillProcess.Dispose();$script:TaskkillProcess=$null};Set-RunningState -Running $false -Status $(if($wasStopped){'已停止'}elseif($wasReady){"已停止（异常退出，退出码 $exitCode）"}else{"启动失败（退出码 $exitCode）"});if($script:CloseAfterStop){$script:IsClosing=$true;$window.Close()};return}
    if($script:StopInProgress){
        if($null -eq $script:TaskkillProcess){
            if($script:StopPhase -eq 'graceful' -and [datetime]::Now -ge $script:StopDeadline){$script:StopPhase='soft';$script:StopDeadline=[datetime]::Now.AddSeconds(3);Start-ForceKill -Force $false}
            elseif($script:StopPhase -eq 'soft' -and [datetime]::Now -ge $script:StopDeadline){$script:StopPhase='hard';$script:StopDeadline=[datetime]::Now.AddSeconds(3);Start-ForceKill -Force $true}
            elseif($script:StopPhase -eq 'hard' -and [datetime]::Now -ge $script:StopDeadline){Complete-StopFailure}
        } else {
            if($script:TaskkillProcess.HasExited){
                $killCode=$script:TaskkillProcess.ExitCode;$script:TaskkillProcess.Dispose();$script:TaskkillProcess=$null
                if($script:ServerProcess.HasExited){return}
                if($killCode -ne 0){Add-LogLine "taskkill 退出码：$killCode"}
                if($script:StopPhase -eq 'soft'){$script:StopPhase='hard';$script:StopDeadline=[datetime]::Now.AddSeconds(3);Start-ForceKill -Force $true}
                elseif($script:StopPhase -eq 'hard'){Complete-StopFailure}
            } elseif([datetime]::Now -ge $script:ForceDeadline){
                Add-LogLine "taskkill 未在限时内退出，放弃本次停止。"
                try{$script:TaskkillProcess.Kill()}catch{}
                $script:TaskkillProcess.Dispose();$script:TaskkillProcess=$null
                Complete-StopFailure
            }
        }
        return
    }
    if($script:IsReady){return}
    if($null -ne $script:HealthTask -and $script:HealthTask.IsCompleted){try{$response=$script:HealthTask.GetAwaiter().GetResult();try{if($response.IsSuccessStatusCode){$script:IsReady=$true;Set-RunningState -Running $true -Status '运行中';Add-LogLine "服务已就绪：$(Get-ServiceBaseUrl $script:CurrentSettings)/"}}finally{if($null -ne $response){$response.Dispose()}}}catch{};$script:HealthTask=$null}
    if($null -eq $script:HealthTask -and ([datetime]::Now-$script:LastHealthAttempt).TotalSeconds -ge 1){$script:LastHealthAttempt=[datetime]::Now;$healthUrl="$(Get-ServiceBaseUrl $script:CurrentSettings)/health";try{$script:HealthTask=$script:HealthClient.GetAsync($healthUrl)}catch{$script:HealthTask=$null}}
})

foreach($model in Find-Models){[void]$controls.ModelPath.Items.Add($model)};$defaultModel=if($controls.ModelPath.Items.Count -gt 0){[string]$controls.ModelPath.Items[0]}else{''};$defaults=Get-LauncherDefaults -LogicalProcessorCount $env:NUMBER_OF_PROCESSORS -DefaultModel $defaultModel;$saved=Read-LauncherConfig;$settings=Merge-LauncherConfig -Defaults $defaults -Saved $saved;Assert-SavedConfigWarnings -Defaults $defaults -Saved $saved;if(-not(Test-Path -LiteralPath $settings.ModelPath -PathType Leaf)){if(-not[string]::IsNullOrWhiteSpace([string]$settings.ModelPath)){Add-LogLine '上次选择的模型不存在，已改用默认模型。'};$settings.ModelPath=$defaultModel};Set-ControlsFromSettings $settings;Set-RunningState -Running $false -Status '未运行'

$controls.BrowseModel.Add_Click({$dialog=New-Object Microsoft.Win32.OpenFileDialog;$dialog.Title='选择 GGUF 模型';$dialog.Filter='GGUF 模型 (*.gguf)|*.gguf|所有文件 (*.*)|*.*';$dialog.InitialDirectory=Join-Path $PSScriptRoot 'models';if($dialog.ShowDialog($window)){if(-not $controls.ModelPath.Items.Contains($dialog.FileName)){[void]$controls.ModelPath.Items.Add($dialog.FileName)};$controls.ModelPath.Text=$dialog.FileName}})
$controls.StartStopButton.Add_Click({if($null -ne $script:ServerProcess -and -not $script:ServerProcess.HasExited){Stop-ManagedServer}else{Start-ManagedServer}})
$controls.OpenUiButton.Add_Click({if($null -ne $script:CurrentSettings){Start-Process "$(Get-ServiceBaseUrl $script:CurrentSettings)/"}})
$controls.CopyApiButton.Add_Click({if($null -ne $script:CurrentSettings){[Windows.Clipboard]::SetText("$(Get-ServiceBaseUrl $script:CurrentSettings)/v1");Add-LogLine 'API 地址已复制到剪贴板。'}})
$window.Add_Closing({param($sender,$eventArgs);if($script:IsClosing){$timer.Stop();$script:HealthClient.Dispose();return};try{$closeSettings=Get-SettingsFromControls;$null=New-LlamaServerArguments -Settings $closeSettings -LogPath $script:LogPath;Save-LauncherConfig $closeSettings}catch{Add-LogLine "配置未保存：$($_.Exception.Message)"};if($null -ne $script:ServerProcess -and -not $script:ServerProcess.HasExited){$answer=[Windows.MessageBox]::Show($window,'Llama 服务仍在运行。选择“是”将停止服务后退出；选择“否”将保留后台服务；选择“取消”返回启动器。','退出启动器',[Windows.MessageBoxButton]::YesNoCancel,[Windows.MessageBoxImage]::Question);if($answer -eq [Windows.MessageBoxResult]::Cancel){$eventArgs.Cancel=$true;return};if($answer -eq [Windows.MessageBoxResult]::Yes){$eventArgs.Cancel=$true;$script:CloseAfterStop=$true;Stop-ManagedServer;return};if($answer -eq [Windows.MessageBoxResult]::No){$script:ServerProcess=$null}};$script:IsClosing=$true;$timer.Stop();$script:HealthClient.Dispose()})

$timer.Start()
[void]$window.ShowDialog()
