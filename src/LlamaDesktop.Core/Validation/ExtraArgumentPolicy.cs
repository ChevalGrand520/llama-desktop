namespace LlamaDesktop.Core.Validation;

public static class ExtraArgumentPolicy
{
    private static readonly HashSet<string> Managed = new(StringComparer.OrdinalIgnoreCase)
    {
        "--model", "--models-dir", "--host", "--port", "--log-file", "--gpu-layers", "--fit",
        "--fit-target", "--ctx-size", "-c", "--threads", "-t", "--batch-size", "--parallel",
        "--ui", "--no-ui", "--models-preset", "--models-max", "--cache-type-k", "--cache-type-v",
        "--reasoning", "--n-predict", "-n", "--flash-attn", "-fa",
    };

    private static readonly HashSet<string> SensitiveOrDangerous = new(StringComparer.OrdinalIgnoreCase)
    {
        "--api-key", "--api-key-file", "--hf-token", "--path", "--log-disable",
    };

    public static bool Validate(string[] extra, out IReadOnlyList<string> errors)
    {
        var list = new List<string>();
        foreach (var raw in extra)
        {
            var flag = raw.Split('=', 2)[0];
            if (SensitiveOrDangerous.Contains(flag))
                list.Add($"参数 {flag} 被禁止（敏感或危险选项）。");
            else if (Managed.Contains(flag))
                list.Add($"参数 {flag} 受启动器管理，不能在额外参数中重复指定。");
        }
        errors = list;
        return list.Count == 0;
    }
}
