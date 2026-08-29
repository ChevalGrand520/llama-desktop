using System.IO;

namespace LlamaDesktop.Core.Services;

/// <summary>
/// Pairs a multimodal projector (mmproj) GGUF with the model GGUF that lives
/// in the same directory. Pure string/path logic — no filesystem I/O.
/// </summary>
public static class MmprojPairing
{
    public static IReadOnlyDictionary<string, string> Pair(IEnumerable<string> ggufPaths)
    {
        var all = ggufPaths
            .Where(f => !Path.GetFileName(f).StartsWith("Modelfile.", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mmproj in all.Where(IsMmproj))
        {
            var dir = Path.GetDirectoryName(mmproj) ?? "";
            var model = all
                .Where(f => !IsMmproj(f))
                .Where(f => string.Equals(Path.GetDirectoryName(f), dir, StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (model is not null) map[model] = mmproj;
        }
        return map;
    }

    public static bool IsMmproj(string path) =>
        Path.GetFileName(path).Contains("mmproj", StringComparison.OrdinalIgnoreCase);
}
