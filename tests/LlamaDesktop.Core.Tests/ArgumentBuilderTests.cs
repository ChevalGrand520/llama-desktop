using LlamaDesktop.Core.Arguments;
using LlamaDesktop.Core.Models;
using LlamaDesktop.Core.Validation;
using Xunit;

namespace LlamaDesktop.Core.Tests;

public class ArgumentBuilderTests
{
    private static ServerSettings Settings() =>
        ServerSettings.WithDefaults(logicalProcessorCount: 8, defaultModel: @"C:\model files\x.gguf") with
        {
            Port = 18080, BatchSize = 1024, Parallel = 2, ModelsMax = 1,
        };

    [Fact]
    public void Windows_Quoting_Handles_Spaces_Quotes_And_Empty()
    {
        var q = WindowsArgumentQuoter.Quote(new[] { "plain", @"C:\model files\x.gguf", "a\"b", "" });
        Assert.Equal("plain \"C:\\model files\\x.gguf\" \"a\\\"b\" \"\"", q);
    }

    [Fact]
    public void Build_Produces_Single_Model_Arguments()
    {
        var args = ServerArgumentBuilder.Build(Settings(), @"C:\tmp\server.log",
            CapabilitySnapshot.Full, logicalProcessors: 8);
        Assert.Contains("--model", args);
        Assert.Contains(@"C:\model files\x.gguf", args);
        Assert.Contains("--port", args);
        Assert.Contains("18080", args);
        Assert.Contains("--log-file", args);
        Assert.Contains("--ctx-size", args);
        Assert.Contains("8192", args);
    }

    [Fact]
    public void Build_Gates_Unsupported_Flags()
    {
        var caps = CapabilitySnapshot.Unknown with { LogFile = false, Fit = false, HealthEndpoint = true };
        var args = ServerArgumentBuilder.Build(Settings(), @"C:\tmp\server.log", caps, logicalProcessors: 8);
        Assert.DoesNotContain("--log-file", args);
        Assert.DoesNotContain("--fit", args);
    }

    [Fact]
    public void Extra_Arguments_Reject_Managed_And_Sensitive()
    {
        Assert.False(ExtraArgumentPolicy.Validate(new[] { "--port", "9999" }, out var e1) || e1.Count == 0);
        Assert.False(ExtraArgumentPolicy.Validate(new[] { "--api-key=secret" }, out var e2) || e2.Count == 0);
        Assert.False(ExtraArgumentPolicy.Validate(new[] { "--log-disable" }, out var e3) || e3.Count == 0);
        Assert.False(ExtraArgumentPolicy.Validate(new[] { "--flash-attn", "on" }, out var e5) || e5.Count == 0);
        Assert.True(ExtraArgumentPolicy.Validate(new[] { "--verbose" }, out var e4) && e4.Count == 0);
    }
}
