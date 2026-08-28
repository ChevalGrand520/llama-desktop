using System.Diagnostics;

namespace LlamaDesktop.Infrastructure.Processes;

public sealed record ProcessIdentity(int Pid, DateTime StartTimeUtc)
{
    public bool Matches(Process process)
    {
        try
        {
            if (process.Id != Pid) return false;
            process.Refresh();
            var start = process.StartTime.ToUniversalTime();
            return Math.Abs((start - StartTimeUtc).TotalSeconds) < 2;
        }
        catch
        {
            return false;
        }
    }
}
