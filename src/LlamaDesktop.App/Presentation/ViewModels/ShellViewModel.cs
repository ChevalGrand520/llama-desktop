using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using LlamaDesktop.App.Web;
using LlamaDesktop.App.Presentation;
using LlamaDesktop.Core.Arguments;
using LlamaDesktop.Core.Models;
using LlamaDesktop.Core.Services;
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
    private bool _isLogDrawerOpen;
    private bool _isSidebarAnimating;
    private bool _isServiceReady;
    private string _gpuLayers = "all";
    private string _contextSize = "8192";
    private string _threads = "24";
    private string _batchSize = "2048";
    private string _parallel = "1";
    private string _flashAttention = "on";
    private string _fitMode = "on";
    private string _cacheTypeK = "q8_0";
    private string _cacheTypeV = "q8_0";
    private string _reasoningMode = "off";
    private string _maxPredict = "8192";
    private bool _autoSelectPort = true;
    private string _extraArguments = "";
    private readonly IReadOnlyDictionary<string, string> _mmprojByModel;
    private string _currentMmproj = "";
    private bool _hasMultimodal;

    public ShellViewModel(
        string serverPath,
        string logPath,
        JsonConfigStore configStore,
        WebViewHost webView,
        ServerSettings initialSettings,
        IReadOnlyList<string> models,
        UiState ui,
        IReadOnlyDictionary<string, string> mmprojByModel)
    {
        _serverPath = serverPath;
        _logPath = logPath;
        _configStore = configStore;
        _webView = webView;
        _settings = initialSettings;
        _mmprojByModel = mmprojByModel;
        _isSidebarOpen = ui.SidebarOpen;
        SidebarWidth = ui.SidebarWidth > 0 ? ui.SidebarWidth : 320;
        _isLogDrawerOpen = ui.LogDrawerOpen;
        _gpuLayers = _settings.GpuLayers;
        _contextSize = _settings.ContextSize.ToString();
        _threads = _settings.Threads.ToString();
        _batchSize = _settings.BatchSize.ToString();
        _parallel = _settings.Parallel.ToString();
        _flashAttention = _settings.FlashAttention;
        _fitMode = _settings.FitMode;
        _cacheTypeK = _settings.CacheTypeK;
        _cacheTypeV = _settings.CacheTypeV;
        _reasoningMode = _settings.ReasoningMode;
        _maxPredict = _settings.MaxPredict.ToString();
        _autoSelectPort = _settings.AutoSelectPort;
        _extraArguments = _settings.ExtraArguments;

        Models = new ObservableCollection<string>(models);
        _selectedModel = string.IsNullOrWhiteSpace(_settings.ModelPath)
            ? (models.Count > 0 ? models[0] : "")
            : _settings.ModelPath;

        ToggleServerCommand = new RelayCommand(_ => ToggleServer(), _ => CanToggleServer);
        ToggleSidebarCommand = new RelayCommand(_ => ToggleSidebar(), _ => !IsSidebarAnimating);
        OpenBrowserCommand = new RelayCommand(_ => OpenBrowser(), _ => IsServiceReady);
        CopyApiCommand = new RelayCommand(_ => CopyApi(), _ => IsServiceReady);
        BrowseModelCommand = new RelayCommand(_ => BrowseModel());

        UpdateMmproj();
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
        set
        {
            if (SetProperty(ref _selectedModel, value ?? "")) UpdateMmproj();
        }
    }

    public string CurrentMmproj { get => _currentMmproj; private set => SetProperty(ref _currentMmproj, value); }
    public bool HasMultimodal { get => _hasMultimodal; private set => SetProperty(ref _hasMultimodal, value); }
    public string MultimodalLabel => HasMultimodal ? "多模态 ✓" : "多模态 —";

    private void UpdateMmproj()
    {
        CurrentMmproj = _mmprojByModel.TryGetValue(SelectedModel, out var mmproj) ? mmproj : "";
        HasMultimodal = CurrentMmproj.Length > 0;
        OnPropertyChanged(nameof(MultimodalLabel));
    }

    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }
    private bool CanStart => _state is ServerLifecycleState.Stopped or ServerLifecycleState.Failed or ServerLifecycleState.StopFailed;
    private bool CanStop => _state is ServerLifecycleState.Running or ServerLifecycleState.WaitingForUi;
    private bool CanToggleServer => CanStart || CanStop;
    public string StartStopLabel => CanStop ? "停止服务" : "启动服务";
    public string ApiBaseUrl => _serviceUri?.ToString().TrimEnd('/') ?? "服务启动后可用";
    public bool IsServiceReady
    {
        get => _isServiceReady;
        private set
        {
            if (SetProperty(ref _isServiceReady, value)) CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool IsSidebarOpen
    {
        get => _isSidebarOpen;
        private set => SetProperty(ref _isSidebarOpen, value);
    }

    public double SidebarWidth { get; }

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

    // 推理参数（绑定到侧栏控件，启动时组装为 ServerSettings）
    public string GpuLayers { get => _gpuLayers; set => SetProperty(ref _gpuLayers, value); }
    public string ContextSize { get => _contextSize; set => SetProperty(ref _contextSize, value); }
    public string Threads { get => _threads; set => SetProperty(ref _threads, value); }
    public string BatchSize { get => _batchSize; set => SetProperty(ref _batchSize, value); }
    public string Parallel { get => _parallel; set => SetProperty(ref _parallel, value); }
    public string FlashAttention { get => _flashAttention; set => SetProperty(ref _flashAttention, value); }
    public string FitMode { get => _fitMode; set => SetProperty(ref _fitMode, value); }
    public string CacheTypeK { get => _cacheTypeK; set => SetProperty(ref _cacheTypeK, value); }
    public string CacheTypeV { get => _cacheTypeV; set => SetProperty(ref _cacheTypeV, value); }
    public string ReasoningMode { get => _reasoningMode; set => SetProperty(ref _reasoningMode, value); }
    public string MaxPredict { get => _maxPredict; set => SetProperty(ref _maxPredict, value); }
    public bool AutoSelectPort { get => _autoSelectPort; set => SetProperty(ref _autoSelectPort, value); }
    public string ExtraArguments { get => _extraArguments; set => SetProperty(ref _extraArguments, value); }

    private void ToggleSidebar()
    {
        if (IsSidebarAnimating) return;
        IsSidebarOpen = !IsSidebarOpen;
        OnPropertyChanged(nameof(SidebarToggleToolTip));
        SaveUiState();
    }

    private void ToggleServer()
    {
        if (CanStart) Start();
        else if (CanStop) _ = StopAsync();
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

    private bool TryBuildSettings(out ServerSettings settings)
    {
        settings = _settings;
        // Guard: a projector (mmproj) must never be loaded as the base model.
        // If the user picked one via the browse dialog, correct it to the paired
        // base model in the same directory.
        var selected = SelectedModel;
        if (MmprojPairing.IsMmproj(selected))
        {
            var dir = Path.GetDirectoryName(selected) ?? "";
            var baseModel = Models.FirstOrDefault(m =>
                string.Equals(Path.GetDirectoryName(m), dir, StringComparison.OrdinalIgnoreCase)
                && !MmprojPairing.IsMmproj(m));
            if (baseModel is not null)
            {
                Log.Append("所选文件是 mmproj 投影器，已自动改用同目录模型。");
                selected = baseModel;
                SelectedModel = baseModel; // triggers UpdateMmproj -> CurrentMmproj
            }
        }

        if (!TryPositiveInt(ContextSize, "上下文长度", out var ctx)) return false;
        if (!TryPositiveInt(Threads, "CPU 线程数", out var thr)) return false;
        if (!TryPositiveInt(BatchSize, "批大小", out var batch)) return false;
        if (!TryPositiveInt(Parallel, "并行请求数", out var par)) return false;
        if (!TryPositiveInt(MaxPredict, "生成上限", out var mp)) return false;

        settings = _settings with
        {
            ModelPath = SelectedModel,
            MmprojPath = CurrentMmproj,
            GpuLayers = GpuLayers.Trim(),
            ContextSize = ctx,
            Threads = thr,
            BatchSize = batch,
            Parallel = par,
            FlashAttention = FlashAttention.Trim(),
            FitMode = FitMode.Trim(),
            CacheTypeK = CacheTypeK.Trim(),
            CacheTypeV = CacheTypeV.Trim(),
            ReasoningMode = ReasoningMode.Trim(),
            MaxPredict = mp,
            AutoSelectPort = AutoSelectPort,
            ExtraArguments = ExtraArguments,
        };
        return true;
    }

    private bool TryPositiveInt(string text, string label, out int value)
    {
        if (int.TryParse(text, out value) && value > 0) return true;
        Log.Append($"{label}必须是大于 0 的整数。");
        value = 0;
        return false;
    }

    private void Start()
    {
        _stopRequested = false;
        IsServiceReady = false;
        if (!TryBuildSettings(out var settings))
            return;
        var issues = SettingsValidator.Validate(settings);
        if (issues.Count > 0)
        {
            foreach (var issue in issues) Log.Append(issue.Message);
            return;
        }

        var extra = settings.ExtraArguments.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        ExtraArgumentPolicy.Validate(extra, out var errors);
        if (errors.Count > 0)
        {
            foreach (var error in errors) Log.Append(error);
            return;
        }

        var port = settings.AutoSelectPort
            ? WindowsPortAllocator.PickFreePort(settings.Port)
            : settings.Port;
        if (port < 0)
        {
            Log.Append($"端口 {settings.Port} 已被占用，且没有可用候选端口。");
            return;
        }
        var effective = settings with { Port = port };
        _settings = effective;

        var config = new LauncherConfig { Settings = effective };
        try
        {
            _configStore.Save(config);
        }
        catch (Exception ex)
        {
            Log.Append($"保存配置失败：{ex.Message}");
        }

        var args = ServerArgumentBuilder.Build(effective, _logPath, CapabilitySnapshot.Full);
        SetServiceUri(new Uri($"http://127.0.0.1:{port}"));
        _state = ServerLifecycleState.StartingProcess;
        StatusText = "启动中";
        UpdateServerButtonState();

        try
        {
            _logReader?.Dispose();
            _logReader = null;
            try
            {
                File.WriteAllText(_logPath, "");
            }
            catch (IOException)
            {
                // A leftover server process (e.g. the user earlier chose
                // "keep running in background") may still hold the log file.
                // Do not block startup; continue and share the existing file.
                Log.Append("日志文件被其他进程占用，无法清空，将继续启动。");
            }
            _logReader = new IncrementalUtf8LogReader(_logPath);
            _controller = new LlamaServerController();
            _controller.ProcessExited += code =>
                System.Windows.Application.Current.Dispatcher.BeginInvoke(
                    new Action(() => HandleProcessExit(code)));
            var proc = _controller.Start(_serverPath, WindowsArgumentQuoter.Quote(args));
            Log.Append($"已启动 llama-server，PID：{proc.Id}");
            _state = ServerLifecycleState.WaitingForUi;
            UpdateServerButtonState();
            _ = PollHealthAsync(effective);
        }
        catch (Exception ex)
        {
            _state = ServerLifecycleState.Failed;
            IsServiceReady = false;
            SetServiceUri(null);
            StatusText = "启动失败";
            Log.Append($"启动服务失败：{ex.Message}");
            UpdateServerButtonState();
        }
    }

    private async Task PollHealthAsync(ServerSettings effective)
    {
        try
        {
            while (_controller is { IsAlive: true })
            {
                ReadLog();
                if (!IsServiceReady)
                {
                    if (await _health.ProbeAsync(_serviceUri!.ToString(), "health", CancellationToken.None))
                    {
                        _state = ServerLifecycleState.Running;
                        IsServiceReady = true;
                        StatusText = "运行中";
                        UpdateServerButtonState();
                        _webView.NavigateToService(_serviceUri);
                    }
                }
                await Task.Delay(TimeSpan.FromSeconds(1));
            }
            // Drain any final log lines after the process exits.
            ReadLog();
        }
        catch (Exception ex)
        {
            Log.Append($"健康检查异常：{ex.Message}");
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
        _state = ServerLifecycleState.Stopping;
        StatusText = "正在停止";
        UpdateServerButtonState();
        var result = await _controller.StopAsync(new Progress<StopPhase>(phase =>
        {
            Log.Append($"停止阶段：{phase}");
        }), CancellationToken.None);
        if (result.Succeeded)
        {
            _state = ServerLifecycleState.Stopped;
            IsServiceReady = false;
            SetServiceUri(null);
            StatusText = "已停止";
        }
        else
        {
            _state = ServerLifecycleState.StopFailed;
            StatusText = result.Message;
        }
        UpdateServerButtonState();
    }

    private void HandleProcessExit(int code)
    {
        Log.Append($"llama-server 已退出，退出码：{code}");
        IsServiceReady = false;
        SetServiceUri(null);
        if (_stopRequested)
        {
            _state = ServerLifecycleState.Stopped;
            StatusText = "已停止";
        }
        else
        {
            _state = ServerLifecycleState.Failed;
            StatusText = $"启动失败（退出码 {code}）";
        }
        UpdateServerButtonState();
    }

    private void SetServiceUri(Uri? uri)
    {
        if (_serviceUri == uri) return;
        _serviceUri = uri;
        OnPropertyChanged(nameof(ApiBaseUrl));
        CommandManager.InvalidateRequerySuggested();
    }

    private void UpdateServerButtonState()
    {
        OnPropertyChanged(nameof(StartStopLabel));
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
