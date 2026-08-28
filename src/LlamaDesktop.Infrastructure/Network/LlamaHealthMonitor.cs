using System.Net.Http;

namespace LlamaDesktop.Infrastructure.Network;

public sealed class LlamaHealthMonitor
{
    private static readonly HttpClient Client = new()
    {
        Timeout = TimeSpan.FromSeconds(1),
    };

    public async Task<bool> ProbeAsync(string baseUrl, string path, CancellationToken ct)
    {
        try
        {
            using var response = await Client.GetAsync($"{baseUrl.TrimEnd('/')}/{path.TrimStart('/')}", ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
