using System.Text;
using LlamaDesktop.Infrastructure.Logging;
using Xunit;

namespace LlamaDesktop.Infrastructure.Tests;

public class LogReaderTests
{
    [Fact]
    public async Task Multibyte_Characters_Split_Across_Chunks_Are_Decoded()
    {
        var path = Path.Combine(Path.GetTempPath(), "ld-log-" + Guid.NewGuid().ToString("N") + ".log");
        try
        {
            var bytes = Encoding.UTF8.GetBytes("中文日志");
            var split = bytes.Length / 2;
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
            {
                fs.Write(bytes, 0, split);
            }
            var reader = new IncrementalUtf8LogReader(path);
            var first = reader.ReadNew();
            using (var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
            {
                fs.Write(bytes, split, bytes.Length - split);
            }
            var second = reader.ReadNew();
            Assert.Contains("中文日志", (first ?? "") + (second ?? ""));
        }
        finally { try { File.Delete(path); } catch { } }
    }

    [Fact]
    public void Truncation_Resets_Offset()
    {
        var path = Path.Combine(Path.GetTempPath(), "ld-log-" + Guid.NewGuid().ToString("N") + ".log");
        try
        {
            File.WriteAllText(path, "aaaa", Encoding.UTF8);
            var reader = new IncrementalUtf8LogReader(path);
            Assert.Equal("aaaa", reader.ReadNew());
            File.WriteAllText(path, "bb", Encoding.UTF8);
            Assert.Equal("bb", reader.ReadNew());
        }
        finally { try { File.Delete(path); } catch { } }
    }
}
