using LlamaDesktop.Core.Models;
using LlamaDesktop.Core.Services;
using Xunit;

namespace LlamaDesktop.Core.Tests;

public class StateMachineTests
{
    private readonly ServerLifecycleStateMachine _m = new();

    [Theory]
    [InlineData(ServerLifecycleState.Stopped, ServerLifecycleState.Inspecting)]
    [InlineData(ServerLifecycleState.Inspecting, ServerLifecycleState.ReadyToStart)]
    [InlineData(ServerLifecycleState.ReadyToStart, ServerLifecycleState.StartingProcess)]
    [InlineData(ServerLifecycleState.StartingProcess, ServerLifecycleState.WaitingForUi)]
    [InlineData(ServerLifecycleState.WaitingForUi, ServerLifecycleState.UiReady)]
    [InlineData(ServerLifecycleState.UiReady, ServerLifecycleState.Running)]
    [InlineData(ServerLifecycleState.Running, ServerLifecycleState.StoppingGraceful)]
    [InlineData(ServerLifecycleState.StoppingGraceful, ServerLifecycleState.StoppingSoft)]
    [InlineData(ServerLifecycleState.StoppingSoft, ServerLifecycleState.StoppingForced)]
    [InlineData(ServerLifecycleState.StoppingForced, ServerLifecycleState.StopFailed)]
    [InlineData(ServerLifecycleState.StoppingForced, ServerLifecycleState.Stopped)]
    [InlineData(ServerLifecycleState.Running, ServerLifecycleState.Failed)]
    [InlineData(ServerLifecycleState.Running, ServerLifecycleState.Detached)]
    public void Allowed_Transitions_Are_Valid(ServerLifecycleState from, ServerLifecycleState to)
    {
        Assert.True(_m.CanTransition(from, to));
    }

    [Theory]
    [InlineData(ServerLifecycleState.Stopped, ServerLifecycleState.Running)]
    [InlineData(ServerLifecycleState.ReadyToStart, ServerLifecycleState.Running)]
    [InlineData(ServerLifecycleState.Failed, ServerLifecycleState.Running)]
    public void Forbidden_Transitions_Are_Invalid(ServerLifecycleState from, ServerLifecycleState to)
    {
        Assert.False(_m.CanTransition(from, to));
    }

    [Fact]
    public void Terminal_States_Are_Recognized()
    {
        Assert.True(_m.IsTerminal(ServerLifecycleState.Stopped));
        Assert.True(_m.IsTerminal(ServerLifecycleState.Failed));
        Assert.True(_m.IsTerminal(ServerLifecycleState.StopFailed));
        Assert.True(_m.IsTerminal(ServerLifecycleState.Detached));
        Assert.False(_m.IsTerminal(ServerLifecycleState.Running));
    }
}
