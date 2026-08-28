using System.Net;
using System.Net.Sockets;

namespace LlamaDesktop.Infrastructure.Network;

public static class WindowsPortAllocator
{
    public static readonly int[] Candidates = { 8080, 8090, 8081, 8188, 11434, 18080 };

    public static bool IsFree(int port)
    {
        try
        {
            var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static int PickFreePort(int preferred, IReadOnlyList<int>? candidates = null)
    {
        if (IsFree(preferred)) return preferred;
        foreach (var candidate in candidates ?? Candidates)
        {
            if (candidate == preferred) continue;
            if (IsFree(candidate)) return candidate;
        }
        return -1;
    }
}
