namespace LlamaDesktop.Core.Models;

public sealed record UiState
{
    public bool LogDrawerOpen { get; init; }
    public double LeftPanelWidth { get; init; } = 300;
}

public sealed record LauncherConfig
{
    public const int SchemaVersion = 2;

    public int SchemaVersionValue { get; init; } = SchemaVersion;
    public required ServerSettings Settings { get; init; }
    public UiState Ui { get; init; } = new();
}
