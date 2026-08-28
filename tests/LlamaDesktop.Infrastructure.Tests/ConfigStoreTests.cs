using LlamaDesktop.Core.Models;
using LlamaDesktop.Infrastructure.Persistence;
using Xunit;

namespace LlamaDesktop.Infrastructure.Tests;

public class ConfigStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ld-tests-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _logs = new();

    public ConfigStoreTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private string ConfigPath => Path.Combine(_dir, "config.json");

    [Fact]
    public void Missing_Config_Returns_Null()
    {
        var store = new JsonConfigStore(ConfigPath, _logs.Add);
        Assert.Null(store.Load());
    }

    [Fact]
    public void Save_Then_Load_RoundTrips()
    {
        var store = new JsonConfigStore(ConfigPath, _logs.Add);
        var cfg = new LauncherConfig
        {
            Settings = ServerSettings.WithDefaults(8, @"C:\m\x.gguf") with { Port = 18080 },
        };
        store.Save(cfg);
        var loaded = store.Load();
        Assert.NotNull(loaded);
        Assert.Equal(18080, loaded!.Settings.Port);
        Assert.Equal(@"C:\m\x.gguf", loaded.Settings.ModelPath);
    }

    [Fact]
    public void Corrupt_Config_Returns_Null_And_Logs()
    {
        File.WriteAllText(ConfigPath, "not json {{{");
        var store = new JsonConfigStore(ConfigPath, _logs.Add);
        Assert.Null(store.Load());
        Assert.NotEmpty(_logs);
    }

    [Fact]
    public void Legacy_Import_Maps_Compatible_Fields()
    {
        var legacy = Path.Combine(_dir, "launcher-config.json");
        File.WriteAllText(legacy, """{"ModelPath":"C:\\m\\x.gguf","Port":9090,"ContextSize":"bad","Threads":8}""");
        var imported = LegacyConfigImporter.TryImport(legacy);
        Assert.NotNull(imported);
        Assert.Equal(@"C:\m\x.gguf", imported!.ModelPath);
        Assert.Equal(9090, imported.Port);
        Assert.Equal(8192, imported.ContextSize); // invalid falls back to default
    }
}
