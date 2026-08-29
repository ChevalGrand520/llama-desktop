using LlamaDesktop.Core.Models;

namespace LlamaDesktop.Core.Arguments;

public static class ServerArgumentBuilder
{
    public static string[] Build(ServerSettings s, string logPath, CapabilitySnapshot caps)
    {
        var args = new List<string>
        {
            "--host", s.Host,
            "--port", s.Port.ToString(),
            "-t", Math.Max(1, s.Threads).ToString(),
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
            // --ui is a boolean switch (no value); --ui on would fail with "invalid argument: on".
            args.Add("--ui");
        }

        args.Add("--ctx-size");
        args.Add(s.ContextSize.ToString());
        args.Add("--batch-size");
        args.Add(s.BatchSize.ToString());
        args.Add("--parallel");
        args.Add(s.Parallel.ToString());

        // Emit the user's explicit choice for fit; never rely on the binary default.
        var fitOn = caps.Fit && s.FitMode.Equals("on", StringComparison.OrdinalIgnoreCase);
        if (caps.Fit)
        {
            args.Add("--fit");
            args.Add(fitOn ? "on" : "off");
            if (caps.FitTarget && fitOn)
            {
                args.Add("--fit-target");
                args.Add(s.FitTargetMiB.ToString());
            }
        }

        // When fit is off, emit an explicit --gpu-layers; when fit is on, omit it so
        // llama-server can auto-partition layers (passing both makes fit abort).
        if (!fitOn)
        {
            if (caps.GpuLayersAllKeyword && s.GpuLayers == "all")
            {
                args.Add("--gpu-layers");
                args.Add("all");
            }
            else if (int.TryParse(s.GpuLayers, out var layers))
            {
                args.Add("--gpu-layers");
                args.Add(layers.ToString());
            }
        }

        if (caps.FlashAttnValueSyntax)
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

        if (caps.Reasoning)
        {
            args.Add("--reasoning");
            args.Add(s.ReasoningMode.ToLowerInvariant());
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
