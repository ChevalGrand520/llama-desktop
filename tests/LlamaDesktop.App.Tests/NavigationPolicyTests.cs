using LlamaDesktop.App.Web;
using Xunit;

namespace LlamaDesktop.App.Tests;

public class NavigationPolicyTests
{
    private static readonly Uri Service = new("http://127.0.0.1:8080/");

    [Fact]
    public void Loopback_Service_Origin_Is_Allowed()
    {
        Assert.True(NavigationPolicy.IsAllowed(new Uri("http://127.0.0.1:8080/"), Service));
        Assert.True(NavigationPolicy.IsAllowed(new Uri("http://localhost:8080/chat"), Service));
    }

    [Fact]
    public void Foreign_Origin_Is_Rejected()
    {
        Assert.False(NavigationPolicy.IsAllowed(new Uri("https://example.com/"), Service));
        Assert.False(NavigationPolicy.IsAllowed(new Uri("http://127.0.0.1:9999/"), Service));
        Assert.False(NavigationPolicy.IsAllowed(new Uri("file:///C:/x.html"), Service));
    }
}
