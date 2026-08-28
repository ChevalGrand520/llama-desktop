namespace LlamaDesktop.Core.Models;

public sealed record CapabilitySnapshot
{
    public bool WebUi { get; init; }
    public bool ModelsDir { get; init; }
    public bool ModelsMax { get; init; }
    public bool ModelsPreset { get; init; }
    public bool Fit { get; init; }
    public bool FitTarget { get; init; }
    public bool FlashAttnValueSyntax { get; init; }
    public bool CacheTypeK { get; init; }
    public bool CacheTypeV { get; init; }
    public bool Jinja { get; init; }
    public bool Reasoning { get; init; }
    public bool Metrics { get; init; }
    public bool Slots { get; init; }
    public bool LoadMode { get; init; }
    public bool LogFile { get; init; }
    public bool HealthEndpoint { get; init; }
    public bool V1ModelsEndpoint { get; init; }
    public bool GpuLayersAllKeyword { get; init; }

    public static CapabilitySnapshot Unknown { get; } = new();
    public static CapabilitySnapshot Full { get; } = new()
    {
        WebUi = true, ModelsDir = true, ModelsMax = true, ModelsPreset = true, Fit = true,
        FitTarget = true, FlashAttnValueSyntax = true, CacheTypeK = true, CacheTypeV = true,
        Jinja = true, Reasoning = true, Metrics = true, Slots = true, LoadMode = true,
        LogFile = true, HealthEndpoint = true, V1ModelsEndpoint = true, GpuLayersAllKeyword = true,
    };
}
