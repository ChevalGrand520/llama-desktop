using System.Text.Json;
using LlamaDesktop.Core.Models;

namespace LlamaDesktop.Infrastructure.Persistence;

public sealed class JsonConfigStore
{
    private readonly string _path;
    private readonly Action<string> _log;

    public JsonConfigStore(string path, Action<string> log)
    {
        _path = path;
        _log = log;
    }

    public LauncherConfig? Load()
    {
        if (!File.Exists(_path)) return null;
        try
        {
            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<LauncherConfig>(json);
        }
        catch (Exception ex)
        {
            _log($"配置读取失败，已使用默认值：{ex.Message}");
            return null;
        }
    }

    public void Save(LauncherConfig config)
    {
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var temp = _path + ".tmp";
        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(temp, json);
        File.Move(temp, _path, overwrite: true);
    }
}
