namespace LlamaDesktop.Core.Models;

public sealed record ServerSettings
{
    public required string ModelPath { get; init; }
    public required string ModelsDirectory { get; init; }
    public required string Host { get; init; }
    public required int Port { get; init; }
    public required bool AutoSelectPort { get; init; }
    public required string GpuLayers { get; init; }
    public required int ContextSize { get; init; }
    public required int Threads { get; init; }
    public required int BatchSize { get; init; }
    public required int Parallel { get; init; }
    public required string FlashAttention { get; init; }
    public required string FitMode { get; init; }
    public required int FitTargetMiB { get; init; }
    public required string CacheTypeK { get; init; }
    public required string CacheTypeV { get; init; }
    public required string ReasoningMode { get; init; }
    public required int MaxPredict { get; init; }
    public required int ModelsMax { get; init; }
    public required string ExtraArguments { get; init; }

    public static ServerSettings WithDefaults(int logicalProcessorCount, string defaultModel) =>
        new()
        {
            ModelPath = defaultModel,
            ModelsDirectory = @"models",
            Host = "127.0.0.1",
            Port = 8080,
            AutoSelectPort = true,
            GpuLayers = "all",
            ContextSize = 8192,
            Threads = Math.Max(1, logicalProcessorCount),
            BatchSize = 2048,
            Parallel = 1,
            FlashAttention = "on",
            FitMode = "on",
            FitTargetMiB = 2048,
            CacheTypeK = "q8_0",
            CacheTypeV = "q8_0",
            ReasoningMode = "off",
            MaxPredict = 8192,
            ModelsMax = 1,
            ExtraArguments = "",
        };
}
