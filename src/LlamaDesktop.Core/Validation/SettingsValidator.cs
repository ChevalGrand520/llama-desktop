using System.Net;
using LlamaDesktop.Core.Models;

namespace LlamaDesktop.Core.Validation;

public sealed record ValidationIssue(string Code, string Message);

public static class SettingsValidator
{
    public static IReadOnlyList<ValidationIssue> Validate(ServerSettings s)
    {
        var issues = new List<ValidationIssue>();

        if (string.IsNullOrWhiteSpace(s.ModelPath))
            issues.Add(new ValidationIssue("ModelRequired", "请选择有效的 GGUF 模型文件。"));

        if (s.Port is < 1 or > 65535)
            issues.Add(new ValidationIssue("PortOutOfRange", "端口必须在 1 到 65535 之间。"));

        if (string.IsNullOrWhiteSpace(s.Host) ||
            (!s.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) &&
             !IPAddress.TryParse(s.Host, out _)))
            issues.Add(new ValidationIssue("InvalidHost", "监听地址必须是 localhost 或有效的 IPv4/IPv6 地址。"));

        if (s.Threads < 1)
            issues.Add(new ValidationIssue("ThreadsOutOfRange", "CPU 线程数必须是大于 0 的整数。"));

        if (s.ContextSize < 1)
            issues.Add(new ValidationIssue("ContextOutOfRange", "上下文长度必须是大于 0 的整数。"));

        if (s.BatchSize < 1)
            issues.Add(new ValidationIssue("BatchOutOfRange", "批大小必须是大于 0 的整数。"));

        if (s.Parallel < 1)
            issues.Add(new ValidationIssue("ParallelOutOfRange", "并行请求数必须是大于 0 的整数。"));

        if (s.MaxPredict < 1)
            issues.Add(new ValidationIssue("MaxPredictOutOfRange", "生成上限必须是大于 0 的整数。"));

        if (s.ModelsMax < 1)
            issues.Add(new ValidationIssue("ModelsMaxOutOfRange", "最大模型数必须是大于 0 的整数。"));

        return issues;
    }
}
