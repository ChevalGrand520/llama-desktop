using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Wpf;
using LlamaDesktop.App.Presentation.ViewModels;
using LlamaDesktop.App.Web;
using LlamaDesktop.Core.Models;
using LlamaDesktop.Core.Services;
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

        // Discover GGUF models from the app's own models\ dir and up to two
        // parent levels, so a published build under dist\ still sees the
        // sibling/ancestor models\ folder without duplicating weights.
        var modelsDirs = new List<string>();
        var probe = appRoot;
        for (var i = 0; i < 3; i++)
        {
            var candidate = Path.Combine(probe, "models");
            if (!modelsDirs.Contains(candidate, StringComparer.OrdinalIgnoreCase))
                modelsDirs.Add(candidate);
            var parent = Path.GetDirectoryName(probe.TrimEnd(Path.DirectorySeparatorChar));
            if (parent is null) break;
            probe = parent;
        }
        var allGguf = modelsDirs
            .Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "*.gguf", SearchOption.AllDirectories))
            .Where(f => !Path.GetFileName(f).StartsWith("Modelfile."))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var mmprojByModel = MmprojPairing.Pair(allGguf);
        var models = allGguf
            .Where(f => !MmprojPairing.IsMmproj(f))
            .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var firstModel = models.FirstOrDefault() ?? "";
        var effective = string.IsNullOrWhiteSpace(saved.ModelPath)
            ? saved with { ModelPath = firstModel, ModelsDirectory = "models" }
            : saved;

        var webViewHost = new WebViewHost();
        _ = InitializeHostAsync(webViewHost, webViewData);

        async Task InitializeHostAsync(WebViewHost host, string data)
        {
            try
            {
                await host.InitializeAsync(data);
            }
            catch (Exception ex)
            {
                logs.Add($"WebView2 环境初始化失败：{ex.Message}");
            }
        }

        var viewModel = new ShellViewModel(
            serverPath, logPath, configStore, webViewHost, effective, models, uiState, mmprojByModel);

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
            try
            {
                await wv.EnsureCoreWebView2Async();
            }
            catch (Exception ex)
            {
                logs.Add($"WebView2 控件初始化失败：{ex.Message}");
                return;
            }
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
