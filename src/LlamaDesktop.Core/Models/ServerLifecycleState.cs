namespace LlamaDesktop.Core.Models;

public enum ServerLifecycleState
{
    Stopped,
    StartingProcess,
    WaitingForUi,
    Running,
    StopFailed,
    Failed,
}
