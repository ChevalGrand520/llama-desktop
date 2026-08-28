using System.Diagnostics;

namespace LlamaDesktop.Infrastructure.Processes;

public enum StopPhase { Graceful, Soft, Hard, Completed, Failed }

public sealed class LlamaServerController
{
    private Process? _process;
    private ProcessIdentity? _identity;
    private readonly ProcessTreeTerminator _terminator = new();

    public event Action<int>? ProcessExited;
    public ProcessIdentity? Identity => _identity;
    public bool IsAlive => _process is { HasExited: false };

    public Process Start(string executablePath, string arguments)
    {
        var psi = new ProcessStartInfo(executablePath)
        {
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        var p = Process.Start(psi)
            ?? throw new InvalidOperationException("启动 llama-server 失败。");
        p.EnableRaisingEvents = true;
        _process = p;
        p.Refresh();
        _identity = new ProcessIdentity(p.Id, p.StartTime.ToUniversalTime());
        p.Exited += (_, _) => ProcessExited?.Invoke(p.ExitCode);
        return p;
    }

    public async Task<StopResult> StopAsync(
        IProgress<StopPhase> progress,
        CancellationToken ct)
    {
        if (_process is null) return StopResult.Failed("未跟踪进程。");
        progress.Report(StopPhase.Graceful);
        try { _process.CloseMainWindow(); } catch { }

        if (await WaitForExitAsync(TimeSpan.FromSeconds(3)))
        {
            progress.Report(StopPhase.Completed);
            return StopResult.Completed();
        }

        progress.Report(StopPhase.Soft);
        try
        {
            var code = await _terminator.TerminateAsync(_process.Id, force: false, ct);
            if (code == 0 && await WaitForExitAsync(TimeSpan.FromSeconds(3)))
            {
                progress.Report(StopPhase.Completed);
                return StopResult.Completed();
            }
        }
        catch { }

        progress.Report(StopPhase.Hard);
        try
        {
            var code = await _terminator.TerminateAsync(_process.Id, force: true, ct);
            if (code == 0 && await WaitForExitAsync(TimeSpan.FromSeconds(3)))
            {
                progress.Report(StopPhase.Completed);
                return StopResult.Completed();
            }
        }
        catch { }

        progress.Report(StopPhase.Failed);
        return StopResult.Failed($"服务未能停止，PID {_process.Id} 仍存活。");
    }

    private async Task<bool> WaitForExitAsync(TimeSpan timeout)
    {
        if (_process is null) return false;
        try
        {
            using var cts = new CancellationTokenSource(timeout);
            await _process.WaitForExitAsync(cts.Token);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Detach() => _process = null;
}

public sealed record StopResult(bool Succeeded, string Message)
{
    public static StopResult Completed() => new(true, "");
    public static StopResult Failed(string message) => new(false, message);
}
