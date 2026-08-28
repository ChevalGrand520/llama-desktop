using LlamaDesktop.Core.Models;
using LlamaDesktop.Core.Validation;
using Xunit;

namespace LlamaDesktop.Core.Tests;

public class SettingsValidatorTests
{
    private static ServerSettings Defaults() =>
        ServerSettings.WithDefaults(logicalProcessorCount: 8, defaultModel: @"C:\models\a.gguf");

    [Fact]
    public void Defaults_Are_Valid()
    {
        var issues = SettingsValidator.Validate(Defaults());
        Assert.Empty(issues);
    }

    [Fact]
    public void Port_Out_Of_Range_Is_Rejected()
    {
        var s = Defaults() with { Port = 70000 };
        Assert.Contains(SettingsValidator.Validate(s), i => i.Code == "PortOutOfRange");
    }

    [Fact]
    public void Empty_Model_Is_Rejected()
    {
        var s = Defaults() with { ModelPath = "" };
        Assert.Contains(SettingsValidator.Validate(s), i => i.Code == "ModelRequired");
    }

    [Fact]
    public void Invalid_Host_Is_Rejected()
    {
        var s = Defaults() with { Host = "not a host!" };
        Assert.Contains(SettingsValidator.Validate(s), i => i.Code == "InvalidHost");
    }

    [Fact]
    public void Threads_Must_Be_Positive()
    {
        var s = Defaults() with { Threads = 0 };
        Assert.Contains(SettingsValidator.Validate(s), i => i.Code == "ThreadsOutOfRange");
    }
}
