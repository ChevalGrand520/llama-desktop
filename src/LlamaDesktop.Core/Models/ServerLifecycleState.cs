namespace LlamaDesktop.Core.Models;

public enum ServerLifecycleState
{
    Stopped,
    Inspecting,
    ConfigurationError,
    ReadyToStart,
    StartingProcess,
    WaitingForUi,
    UiReady,
    Running,
    StoppingGraceful,
    StoppingSoft,
    StoppingForced,
    StopFailed,
    Failed,
    Detached,
    ExternalServiceDetected,
    ExternalConnected,
    ExternalDisconnected,
}
