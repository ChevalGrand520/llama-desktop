using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Wpf;
using LlamaDesktop.App.Presentation.ViewModels;
using LlamaDesktop.App.Web;
using LlamaDesktop.Core.Models;
using LlamaDesktop.Infrastructure.Persistence;

namespace LlamaDesktop.App;

public static class CompositionRoot
{
    public static void Run(Application app)
    {
        var appRoot = Path.GetFullPath(AppContext.BaseDirectory);
        var serverPath = Path.Combine(appRoot, "llama-server.exe");
        var dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LlamaDesktop");
        var configPath = Path.Combine(dataDir, "config.json");
        var logPath = Path.Combine(dataDir, "logs", "launcher-server.log");
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        var webViewData = Path.Combine(dataDir, "webview2");

        var logs = new List<string>();
        var configStore = new JsonConfigStore(configPath, logs.Add);
        var legacyPath = Path.Combine(appRoot, "launcher-config.json");

        var loaded = configStore.Load();
        var saved = loaded?.Settings
            ?? LegacyConfigImporter.TryImport(legacyPath)
            ?? ServerSettings.WithDefaults(Math.Max(1, Environment.ProcessorCount), "");
        var uiState = loaded?.Ui ?? new UiState();

        var modelsDir = Path.Combine(appRoot, "models");
        var models = Directory.Exists(modelsDir)
            ? Directory.EnumerateFiles(modelsDir, "*.gguf", SearchOption.AllDirectories)
                .Where(f => !Path.GetFileName(f).StartsWith("Modelfile."))
                .Where(f => !Path.GetFileName(f).Contains("mmproj", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : Array.Empty<string>();
        var firstModel = models.FirstOrDefault() ?? "";
        var effective = string.IsNullOrWhiteSpace(saved.ModelPath)
            ? saved with { ModelPath = firstModel, ModelsDirectory = "models" }
            : saved;

        var webViewHost = new WebViewHost();
        _ = webViewHost.InitializeAsync(webViewData);

        var viewModel = new ShellViewModel(
            serverPath, logPath, configStore, webViewHost, effective, models, uiState);

        var webView = new WebView2();
        Uri? serviceBase = null;
        webViewHost.NavigationRequested += uri =>
        {
            serviceBase = uri;
            if (webView.CoreWebView2 is null) return;
            webView.CoreWebView2.Navigate(uri.ToString());
        };
        webView.NavigationStarting += (_, e) =>
        {
            if (serviceBase is null) { e.Cancel = true; return; }
            if (!NavigationPolicy.IsAllowed(new Uri(e.Uri), serviceBase))
            {
                e.Cancel = true;
                try
                {
                    System.Diagnostics.Process.Start(
                        new System.Diagnostics.ProcessStartInfo(e.Uri) { UseShellExecute = true });
                }
                catch { }
            }
        };
        _ = InitializeWebViewAsync(webView);

        async Task InitializeWebViewAsync(WebView2 wv)
        {
            // CoreWebView2 is only available after initialization; attach the
            // new-window handler once the runtime exists to avoid an NRE.
            await wv.EnsureCoreWebView2Async();
            if (wv.CoreWebView2 is null) return;
            wv.CoreWebView2.NewWindowRequested += (_, e) =>
            {
                e.Handled = true;
                if (serviceBase is not null
                    && Uri.TryCreate(e.Uri, UriKind.Absolute, out var u)
                    && NavigationPolicy.IsAllowed(u, serviceBase))
                {
                    wv.CoreWebView2.Navigate(u.ToString());
                }
            };
        }

        var window = new ShellWindow(viewModel, webView);
        app.MainWindow = window;
        window.Show();
    }
}
