using System.Collections.ObjectModel;
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
using Microsoft.Win32;

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
    private string _selectedModel = "";
    private Uri? _serviceUri;
    private bool _stopRequested;
    private bool _isSidebarOpen = true;
    private double _sidebarWidth = 320;
    private bool _isLogDrawerOpen;
    private bool _isSidebarAnimating;

    public ShellViewModel(
        string serverPath,
        string logPath,
        JsonConfigStore configStore,
        WebViewHost webView,
        ServerSettings initialSettings,
        IReadOnlyList<string> models,
        UiState ui)
    {
        _serverPath = serverPath;
        _logPath = logPath;
        _configStore = configStore;
        _webView = webView;
        _settings = initialSettings;
        _isSidebarOpen = ui.SidebarOpen;
        _sidebarWidth = ui.SidebarWidth > 0 ? ui.SidebarWidth : 320;
        _isLogDrawerOpen = ui.LogDrawerOpen;

        Models = new ObservableCollection<string>(models);
        _selectedModel = string.IsNullOrWhiteSpace(_settings.ModelPath)
            ? (models.Count > 0 ? models[0] : "")
            : _settings.ModelPath;

        ToggleServerCommand = new RelayCommand(_ => ToggleServer(), _ => CanToggleServer);
        ToggleSidebarCommand = new RelayCommand(_ => ToggleSidebar(), _ => !IsSidebarAnimating);
        OpenBrowserCommand = new RelayCommand(_ => OpenBrowser(), _ => _serviceUri is not null);
        CopyApiCommand = new RelayCommand(_ => CopyApi(), _ => _serviceUri is not null);
        BrowseModelCommand = new RelayCommand(_ => BrowseModel());
    }

    public ICommand ToggleServerCommand { get; }
    public ICommand ToggleSidebarCommand { get; }
    public ICommand OpenBrowserCommand { get; }
    public ICommand CopyApiCommand { get; }
    public ICommand BrowseModelCommand { get; }
    public LogViewModel Log { get; } = new();

    public ObservableCollection<string> Models { get; }
    public string SelectedModel
    {
        get => _selectedModel;
        set => SetProperty(ref _selectedModel, value ?? "");
    }

    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }
    public bool CanStart => _state is ServerLifecycleState.Stopped or ServerLifecycleState.Failed or ServerLifecycleState.StopFailed;
    public bool CanStop => _state is ServerLifecycleState.Running or ServerLifecycleState.UiReady or ServerLifecycleState.WaitingForUi;
    public bool CanToggleServer => CanStart || CanStop;
    public string StartStopLabel => CanStop ? "停止服务" : "启动服务";
    public string ApiBaseUrl => _serviceUri?.ToString().TrimEnd('/') ?? "";

    public bool IsSidebarOpen
    {
        get => _isSidebarOpen;
        private set => SetProperty(ref _isSidebarOpen, value);
    }

    public double SidebarWidth
    {
        get => _sidebarWidth;
        private set => SetProperty(ref _sidebarWidth, value);
    }

    public bool IsLogDrawerOpen
    {
        get => _isLogDrawerOpen;
        set
        {
            if (SetProperty(ref _isLogDrawerOpen, value)) SaveUiState();
        }
    }

    public bool IsSidebarAnimating
    {
        get => _isSidebarAnimating;
        set
        {
            if (SetProperty(ref _isSidebarAnimating, value)) CommandManager.InvalidateRequerySuggested();
        }
    }

    public string SidebarToggleToolTip => IsSidebarOpen ? "收起侧栏" : "展开侧栏";

    private void ToggleSidebar()
    {
        if (IsSidebarAnimating) return;
        IsSidebarOpen = !IsSidebarOpen;
        OnPropertyChanged(nameof(SidebarToggleToolTip));
        SaveUiState();
    }

    private void ToggleServer()
    {
        if (CanStart) StartAsync().ConfigureAwait(false);
        else if (CanStop) StopAsync().ConfigureAwait(false);
    }

    /// <summary>持久化界面状态（侧栏收纳/宽度、日志抽屉）。由切换动作即时调用，窗口关闭时兜底调用。</summary>
    public void SaveUiState()
    {
        try
        {
            var config = new LauncherConfig
            {
                Settings = _settings,
                Ui = new UiState
                {
                    SidebarOpen = IsSidebarOpen,
                    SidebarWidth = SidebarWidth,
                    LogDrawerOpen = IsLogDrawerOpen,
                },
            };
            _configStore.Save(config);
        }
        catch (Exception ex)
        {
            Log.Append($"保存界面状态失败：{ex.Message}");
        }
    }

    private void BrowseModel()
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择 GGUF 模型",
            Filter = "GGUF 模型 (*.gguf)|*.gguf|所有文件 (*.*)|*.*",
            InitialDirectory = Path.GetDirectoryName(Path.GetFullPath(_settings.ModelPath)) ?? Environment.CurrentDirectory,
        };
        if (dialog.ShowDialog() == true)
        {
            if (!Models.Contains(dialog.FileName))
            {
                Models.Add(dialog.FileName);
            }
            SelectedModel = dialog.FileName;
        }
    }

    private async Task StartAsync()
    {
        _stopRequested = false;
        _settings = _settings with { ModelPath = SelectedModel };
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
        UpdateServerButtonState();

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
                UpdateServerButtonState();
            };
            var proc = _controller.Start(_serverPath, WindowsArgumentQuoter.Quote(args));
            Log.Append($"已启动 llama-server，PID：{proc.Id}");
            _state = ServerLifecycleState.WaitingForUi;
            UpdateServerButtonState();
            _ = PollHealthAsync(effective);
        }
        catch (Exception ex)
        {
            _state = ServerLifecycleState.Failed;
            StatusText = "启动失败";
            Log.Append($"启动服务失败：{ex.Message}");
            UpdateServerButtonState();
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
                UpdateServerButtonState();
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
        UpdateServerButtonState();
    }

    private void UpdateServerButtonState()
    {
        OnPropertyChanged(nameof(StartStopLabel));
        OnPropertyChanged(nameof(CanToggleServer));
        CommandManager.InvalidateRequerySuggested();
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
