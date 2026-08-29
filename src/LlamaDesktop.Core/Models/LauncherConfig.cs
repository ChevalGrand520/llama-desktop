namespace LlamaDesktop.Core.Models;

public sealed record UiState
{
    public bool SidebarOpen { get; init; } = true;
    public double SidebarWidth { get; init; } = 320;
    public bool LogDrawerOpen { get; init; } = false;
}

public sealed record LauncherConfig
{
    public const int SchemaVersion = 2;

    public int SchemaVersionValue { get; init; } = SchemaVersion;
    public required ServerSettings Settings { get; init; }
    public UiState Ui { get; init; } = new();
}
