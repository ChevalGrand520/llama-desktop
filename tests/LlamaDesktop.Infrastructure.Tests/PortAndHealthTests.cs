using System.Net;
using System.Net.Sockets;
using LlamaDesktop.Infrastructure.Network;
using Xunit;

namespace LlamaDesktop.Infrastructure.Tests;

public class PortAndHealthTests
{
    [Fact]
    public void Occupied_Port_Is_Not_Free()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            Assert.False(WindowsPortAllocator.IsFree(port));
        }
        finally { listener.Stop(); }
    }

    [Fact]
    public void Free_Port_Is_Free()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        Assert.True(WindowsPortAllocator.IsFree(port));
    }

    [Fact]
    public async Task Health_Probe_Sees_Listening_Http()
    {
        using var http = new HttpListener();
        http.Prefixes.Add("http://127.0.0.1:18099/");
        http.Start();
        var monitor = new LlamaHealthMonitor();
        Assert.True(await monitor.ProbeAsync("http://127.0.0.1:18099", "health", CancellationToken.None));
        http.Stop();
    }

    [Fact]
    public async Task Health_Probe_Fails_On_Closed_Port()
    {
        var monitor = new LlamaHealthMonitor();
        Assert.False(await monitor.ProbeAsync("http://127.0.0.1:19999", "health", CancellationToken.None));
    }
}
