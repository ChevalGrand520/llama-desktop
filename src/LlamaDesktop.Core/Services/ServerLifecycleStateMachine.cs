using LlamaDesktop.Core.Models;

namespace LlamaDesktop.Core.Services;

public sealed class ServerLifecycleStateMachine
{
    private static readonly Dictionary<ServerLifecycleState, HashSet<ServerLifecycleState>> Allowed = new()
    {
        [ServerLifecycleState.Stopped] = new HashSet<ServerLifecycleState> { ServerLifecycleState.Inspecting },
        [ServerLifecycleState.Inspecting] = new HashSet<ServerLifecycleState>
        {
            ServerLifecycleState.ReadyToStart,
            ServerLifecycleState.ConfigurationError,
            ServerLifecycleState.ExternalServiceDetected,
        },
        [ServerLifecycleState.ConfigurationError] = new HashSet<ServerLifecycleState> { ServerLifecycleState.Stopped },
        [ServerLifecycleState.ExternalServiceDetected] = new HashSet<ServerLifecycleState>
        {
            ServerLifecycleState.ExternalConnected,
            ServerLifecycleState.ReadyToStart,
        },
        [ServerLifecycleState.ExternalConnected] = new HashSet<ServerLifecycleState>
        {
            ServerLifecycleState.ExternalDisconnected,
            ServerLifecycleState.Stopped,
        },
        [ServerLifecycleState.ExternalDisconnected] = new HashSet<ServerLifecycleState>
        {
            ServerLifecycleState.ExternalConnected,
            ServerLifecycleState.ReadyToStart,
        },
        [ServerLifecycleState.ReadyToStart] = new HashSet<ServerLifecycleState>
        {
            ServerLifecycleState.StartingProcess,
            ServerLifecycleState.Stopped,
        },
        [ServerLifecycleState.StartingProcess] = new HashSet<ServerLifecycleState>
        {
            ServerLifecycleState.WaitingForUi,
            ServerLifecycleState.Failed,
        },
        [ServerLifecycleState.WaitingForUi] = new HashSet<ServerLifecycleState>
        {
            ServerLifecycleState.UiReady,
            ServerLifecycleState.Failed,
        },
        [ServerLifecycleState.UiReady] = new HashSet<ServerLifecycleState>
        {
            ServerLifecycleState.Running,
            ServerLifecycleState.Failed,
            ServerLifecycleState.Detached,
        },
        [ServerLifecycleState.Running] = new HashSet<ServerLifecycleState>
        {
            ServerLifecycleState.StoppingGraceful,
            ServerLifecycleState.Failed,
            ServerLifecycleState.Detached,
        },
        [ServerLifecycleState.StoppingGraceful] = new HashSet<ServerLifecycleState>
        {
            ServerLifecycleState.Stopped,
            ServerLifecycleState.StoppingSoft,
            ServerLifecycleState.StopFailed,
        },
        [ServerLifecycleState.StoppingSoft] = new HashSet<ServerLifecycleState>
        {
            ServerLifecycleState.Stopped,
            ServerLifecycleState.StoppingForced,
            ServerLifecycleState.StopFailed,
        },
        [ServerLifecycleState.StoppingForced] = new HashSet<ServerLifecycleState>
        {
            ServerLifecycleState.Stopped,
            ServerLifecycleState.StopFailed,
        },
        [ServerLifecycleState.StopFailed] = new HashSet<ServerLifecycleState> { ServerLifecycleState.Stopped },
        [ServerLifecycleState.Failed] = new HashSet<ServerLifecycleState> { ServerLifecycleState.Stopped },
        [ServerLifecycleState.Detached] = new HashSet<ServerLifecycleState> { ServerLifecycleState.Stopped },
    };

    public bool CanTransition(ServerLifecycleState from, ServerLifecycleState to) =>
        Allowed.TryGetValue(from, out var set) && set.Contains(to);

    public bool IsTerminal(ServerLifecycleState state) =>
        state is ServerLifecycleState.Stopped or ServerLifecycleState.Failed
            or ServerLifecycleState.StopFailed or ServerLifecycleState.Detached;
}
