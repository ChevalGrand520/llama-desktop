using Microsoft.Web.WebView2.Core;

namespace LlamaDesktop.App.Web;

public sealed class WebViewHost
{
    private bool _initialized;

    public async Task InitializeAsync(string userDataFolder)
    {
        await CoreWebView2Environment.CreateAsync(null, userDataFolder);
        _initialized = true;
    }

    public void NavigateToService(Uri baseUri)
    {
        if (!_initialized) throw new InvalidOperationException("WebView2 尚未初始化。");
        NavigationRequested?.Invoke(baseUri);
    }

    public event Action<Uri>? NavigationRequested;
}
