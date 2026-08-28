using System.Text.Json;
using LlamaDesktop.Core.Models;

namespace LlamaDesktop.Infrastructure.Persistence;

public static class LegacyConfigImporter
{
    public static ServerSettings? TryImport(string legacyPath)
    {
        if (!File.Exists(legacyPath)) return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(legacyPath));
            var root = doc.RootElement;
            var defaults = ServerSettings.WithDefaults(
                logicalProcessorCount: Math.Max(1, Environment.ProcessorCount),
                defaultModel: "");

            string Str(string name, string fallback) =>
                root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
                    ? el.GetString() ?? fallback
                    : fallback;

            int Int(string name, int fallback) =>
                root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.Number &&
                el.TryGetInt32(out var v)
                    ? v
                    : fallback;

            return defaults with
            {
                ModelPath = Str("ModelPath", defaults.ModelPath),
                Host = Str("Host", defaults.Host),
                Port = Int("Port", defaults.Port),
                ContextSize = Int("ContextSize", defaults.ContextSize),
                Threads = Int("Threads", defaults.Threads),
                BatchSize = Int("BatchSize", defaults.BatchSize),
                Parallel = Int("Parallel", defaults.Parallel),
                ExtraArguments = Str("ExtraArguments", ""),
            };
        }
        catch
        {
            return null;
        }
    }
}
