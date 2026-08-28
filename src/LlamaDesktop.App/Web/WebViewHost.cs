using Microsoft.Web.WebView2.Core;

namespace LlamaDesktop.App.Web;

public enum WebPlaceholderState { Loading, Error, NonLoopbackHost, Disconnected }

public sealed class WebViewHost : IDisposable
{
    private CoreWebView2Environment? _environment;
    private bool _initialized;

    public async Task InitializeAsync(string userDataFolder, CancellationToken ct)
    {
        _environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
        _initialized = true;
    }

    public Task NavigateToServiceAsync(Uri baseUri, CancellationToken ct)
    {
        if (!_initialized) throw new InvalidOperationException("WebView2 尚未初始化。");
        NavigationRequested?.Invoke(baseUri);
        return Task.CompletedTask;
    }

    public void ShowNativePlaceholder(WebPlaceholderState state, string? detail = null)
    {
        PlaceholderChanged?.Invoke(state, detail);
    }

    public Task ClearBrowsingDataAsync()
    {
        // Note: the pinned WebView2 SDK (1.0.2903.40) exposes no environment-level
        // profile creation API — CoreWebView2Environment.CreateBrowserProfileAsync
        // does not exist in Microsoft.Web.WebView2.Core. Profiles are only reachable
        // via CoreWebView2.Profile after a controller/WebView is created (Task 11 WPF
        // shell), so without a WebView instance there is no browsing data to clear.
        return Task.CompletedTask;
    }

    public event Action<Uri>? NavigationRequested;
    public event Action<WebPlaceholderState, string?>? PlaceholderChanged;

    public void Dispose()
    {
    }
}
