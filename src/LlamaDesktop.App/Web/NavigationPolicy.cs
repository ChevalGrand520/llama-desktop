using System.Net;

namespace LlamaDesktop.App.Web;

public static class NavigationPolicy
{
    public static bool IsAllowed(Uri uri, Uri serviceBaseUri)
    {
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return false;
        if (!IsLoopbackHost(uri.Host))
            return false;
        return uri.Port == serviceBaseUri.Port;
    }

    private static bool IsLoopbackHost(string host)
    {
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;
        if (IPAddress.TryParse(host, out var address))
        {
            return IPAddress.IsLoopback(address);
        }
        return false;
    }
}
