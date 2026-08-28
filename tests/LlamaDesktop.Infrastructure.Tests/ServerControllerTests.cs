using System.Diagnostics;
using LlamaDesktop.Infrastructure.Processes;
using Xunit;

namespace LlamaDesktop.Infrastructure.Tests;

public class ProcessIdentityTests
{
    [Fact]
    public void Identity_Matches_Same_Process()
    {
        using var p = Process.Start(new ProcessStartInfo("powershell.exe", "-NoProfile -Command Start-Sleep 30")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        });
        Assert.NotNull(p);
        p!.Refresh();
        var identity = new ProcessIdentity(p.Id, p.StartTime.ToUniversalTime());
        p.Refresh();
        Assert.True(identity.Matches(p));
        p.Kill();
    }

    [Fact]
    public void Identity_Does_Not_Match_Different_Pid()
    {
        var identity = new ProcessIdentity(999999, DateTime.UtcNow.AddMinutes(-10));
        Assert.False(identity.Matches(new Process()));
    }
}
