using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using LlamaDesktop.App.Web;
using LlamaDesktop.App.Presentation;
using LlamaDesktop.Core.Arguments;
using LlamaDesktop.Core.Models;
using LlamaDesktop.Core.Validation;
using LlamaDesktop.Infrastructure.Logging;
using LlamaDesktop.Infrastructure.Network;
using LlamaDesktop.Infrastructure.Persistence;
using LlamaDesktop.Infrastructure.Processes;

namespace LlamaDesktop.App.Presentation.ViewModels;

public sealed class ShellViewModel : ObservableObject
{
    private readonly string _serverPath;
    private readonly string _logPath;
    private readonly JsonConfigStore _configStore;
    private readonly WebViewHost _webView;
    private readonly LlamaHealthMonitor _health = new();
    private LlamaServerController? _controller;
    private IncrementalUtf8LogReader? _logReader;
    private ServerSettings _settings;
    private ServerLifecycleState _state = ServerLifecycleState.Stopped;
    private string _statusText = "未运行";
    private Uri? _serviceUri;
    private bool _stopRequested;

    public ShellViewModel(
        string serverPath,
        string logPath,
        JsonConfigStore configStore,
        WebViewHost webView,
        ServerSettings initialSettings)
    {
        _serverPath = serverPath;
        _logPath = logPath;
        _configStore = configStore;
        _webView = webView;
        _settings = initialSettings;

        StartCommand = new RelayCommand(_ => StartAsync().ConfigureAwait(false), _ => CanStart);
        StopCommand = new RelayCommand(_ => StopAsync().ConfigureAwait(false), _ => CanStop);
        OpenBrowserCommand = new RelayCommand(_ => OpenBrowser(), _ => _serviceUri is not null);
        CopyApiCommand = new RelayCommand(_ => CopyApi(), _ => _serviceUri is not null);
    }

    public ICommand StartCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand OpenBrowserCommand { get; }
    public ICommand CopyApiCommand { get; }
    public LogViewModel Log { get; } = new();

    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }
    public bool CanStart => _state is ServerLifecycleState.Stopped or ServerLifecycleState.Failed or ServerLifecycleState.StopFailed;
    public bool CanStop => _state is ServerLifecycleState.Running or ServerLifecycleState.UiReady or ServerLifecycleState.WaitingForUi;
    public string ApiBaseUrl => _serviceUri?.ToString().TrimEnd('/') ?? "";

    private async Task StartAsync()
    {
        _stopRequested = false;
        var issues = SettingsValidator.Validate(_settings);
        if (issues.Count > 0)
        {
            foreach (var issue in issues) Log.Append(issue.Message);
            return;
        }

        var extra = _settings.ExtraArguments.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        ExtraArgumentPolicy.Validate(extra, out var errors);
        if (errors.Count > 0)
        {
            foreach (var error in errors) Log.Append(error);
            return;
        }

        var port = _settings.AutoSelectPort
            ? WindowsPortAllocator.PickFreePort(_settings.Port)
            : _settings.Port;
        if (port < 0)
        {
            Log.Append($"端口 {_settings.Port} 已被占用，且没有可用候选端口。");
            return;
        }
        var effective = _settings with { Port = port };

        var config = new LauncherConfig { Settings = effective };
        try
        {
            _configStore.Save(config);
        }
        catch (Exception ex)
        {
            Log.Append($"保存配置失败：{ex.Message}");
        }

        var args = ServerArgumentBuilder.Build(effective, _logPath, CapabilitySnapshot.Full,
            logicalProcessors: Math.Max(1, Environment.ProcessorCount));
        _serviceUri = new Uri($"http://127.0.0.1:{port}");
        _state = ServerLifecycleState.StartingProcess;
        StatusText = "启动中";

        try
        {
            File.WriteAllText(_logPath, "");
            _logReader = new IncrementalUtf8LogReader(_logPath);
            _controller = new LlamaServerController();
            _controller.ProcessExited += code =>
            {
                Log.Append($"llama-server 已退出，退出码：{code}");
                if (_stopRequested)
                {
                    StatusText = "已停止";
                }
                else
                {
                    _state = ServerLifecycleState.Failed;
                    StatusText = $"启动失败（退出码 {code}）";
                }
            };
            var proc = _controller.Start(_serverPath, WindowsArgumentQuoter.Quote(args));
            Log.Append($"已启动 llama-server，PID：{proc.Id}");
            _state = ServerLifecycleState.WaitingForUi;
            _ = PollHealthAsync(effective);
        }
        catch (Exception ex)
        {
            _state = ServerLifecycleState.Failed;
            StatusText = "启动失败";
            Log.Append($"启动服务失败：{ex.Message}");
        }
    }

    private async Task PollHealthAsync(ServerSettings effective)
    {
        while (_controller is { IsAlive: true })
        {
            ReadLog();
            if (await _health.ProbeAsync(_serviceUri!.ToString(), "health", CancellationToken.None))
            {
                _state = ServerLifecycleState.Running;
                StatusText = "运行中";
                _webView.NavigateToServiceAsync(_serviceUri, CancellationToken.None);
                break;
            }
            await Task.Delay(TimeSpan.FromSeconds(1));
        }
    }

    private void ReadLog()
    {
        if (_logReader is null) return;
        var text = _logReader.ReadNew();
        if (!string.IsNullOrEmpty(text))
        {
            foreach (var line in text.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries))
                Log.Append(line);
        }
    }

    private async Task StopAsync()
    {
        if (_controller is null) return;
        _stopRequested = true;
        StatusText = "正在停止";
        var result = await _controller.StopAsync(new Progress<StopPhase>(phase =>
        {
            Log.Append($"停止阶段：{phase}");
        }), CancellationToken.None);
        if (result.Succeeded)
        {
            _state = ServerLifecycleState.Stopped;
            StatusText = "已停止";
        }
        else
        {
            _state = ServerLifecycleState.StopFailed;
            StatusText = result.Message;
        }
    }

    private void OpenBrowser()
    {
        if (_serviceUri is null) return;
        Process.Start(new ProcessStartInfo(_serviceUri.ToString()) { UseShellExecute = true });
    }

    private void CopyApi()
    {
        if (_serviceUri is null) return;
        System.Windows.Clipboard.SetText($"{_serviceUri.ToString().TrimEnd('/')}/v1");
        Log.Append("API 地址已复制到剪贴板。");
    }
}

public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Predicate<object?>? _canExecute;

    public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) => _execute(parameter);
    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }
}
