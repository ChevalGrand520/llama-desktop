using LlamaDesktop.Core.Models;

namespace LlamaDesktop.Core.Arguments;

public static class ServerArgumentBuilder
{
    public static string[] Build(ServerSettings s, string logPath, CapabilitySnapshot caps, int logicalProcessors)
    {
        var args = new List<string>
        {
            "--host", s.Host,
            "--port", s.Port.ToString(),
            "-t", Math.Max(1, logicalProcessors).ToString(),
        };

        if (caps.LogFile)
        {
            args.Add("--log-file");
            args.Add(logPath);
        }

        args.Add("--model");
        args.Add(s.ModelPath);

        if (caps.WebUi)
        {
            args.Add("--ui");
            args.Add("on");
        }

        args.Add("--ctx-size");
        args.Add(s.ContextSize.ToString());
        args.Add("--batch-size");
        args.Add(s.BatchSize.ToString());
        args.Add("--parallel");
        args.Add(s.Parallel.ToString());

        // When fit is active, omit --gpu-layers so llama-server can auto-partition layers
        // across devices; passing both makes fit abort ("n_gpu_layers already set by user").
        if (caps.Fit && s.FitMode == "on")
        {
            args.Add("--fit");
            args.Add("on");
            if (caps.FitTarget)
            {
                args.Add("--fit-target");
                args.Add(s.FitTargetMiB.ToString());
            }
        }
        else if (caps.GpuLayersAllKeyword && s.GpuLayers == "all")
        {
            args.Add("--gpu-layers");
            args.Add("all");
        }
        else if (int.TryParse(s.GpuLayers, out var layers))
        {
            args.Add("--gpu-layers");
            args.Add(layers.ToString());
        }

        if (caps.FlashAttnValueSyntax && !s.FlashAttention.Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            args.Add("--flash-attn");
            args.Add(s.FlashAttention.ToLowerInvariant());
        }

        if (caps.CacheTypeK && s.CacheTypeK != "f16")
        {
            args.Add("--cache-type-k");
            args.Add(s.CacheTypeK);
        }

        if (caps.CacheTypeV && s.CacheTypeV != "f16")
        {
            args.Add("--cache-type-v");
            args.Add(s.CacheTypeV);
        }

        if (caps.Reasoning && s.ReasoningMode == "on")
        {
            args.Add("--reasoning");
            args.Add("on");
        }

        if (caps.ModelsDir && s.ModelsMax > 1)
        {
            args.Add("--models-max");
            args.Add(s.ModelsMax.ToString());
        }

        args.Add("-n");
        args.Add(s.MaxPredict.ToString());

        if (!string.IsNullOrWhiteSpace(s.ExtraArguments))
        {
            foreach (var part in s.ExtraArguments.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                args.Add(part);
        }

        return args.ToArray();
    }
}
