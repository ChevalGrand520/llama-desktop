using System.Diagnostics;

namespace LlamaDesktop.Infrastructure.Processes;

public sealed class ProcessTreeTerminator
{
    public async Task<int> TerminateAsync(int pid, bool force, CancellationToken ct)
    {
        var psi = new ProcessStartInfo("taskkill.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("/PID");
        psi.ArgumentList.Add(pid.ToString());
        psi.ArgumentList.Add("/T");
        if (force) psi.ArgumentList.Add("/F");

        using var p = Process.Start(psi);
        if (p is null) throw new InvalidOperationException("无法启动 taskkill.exe。");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            await p.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try { p.Kill(); } catch { }
            throw new TimeoutException("taskkill 未在限时内退出。");
        }
        return p.ExitCode;
    }
}
