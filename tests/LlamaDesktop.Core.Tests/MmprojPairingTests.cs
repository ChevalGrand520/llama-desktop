using LlamaDesktop.Core.Services;
using Xunit;

namespace LlamaDesktop.Core.Tests;

public class MmprojPairingTests
{
    [Fact]
    public void Pairs_Mmproj_With_Model_In_Same_Directory()
    {
        var map = MmprojPairing.Pair(new[]
        {
            @"C:\models\A\model.gguf",
            @"C:\models\A\mmproj.gguf",
            @"C:\models\B\other.gguf",
        });
        Assert.True(map.ContainsKey(@"C:\models\A\model.gguf"));
        Assert.Equal(@"C:\models\A\mmproj.gguf", map[@"C:\models\A\model.gguf"]);
        Assert.False(map.ContainsKey(@"C:\models\B\other.gguf"));
    }

    [Fact]
    public void No_Mmproj_Means_Empty_Map()
    {
        var map = MmprojPairing.Pair(new[] { @"C:\models\A\model.gguf" });
        Assert.Empty(map);
    }

    [Fact]
    public void Ignores_Modelfile_Placeholders()
    {
        var map = MmprojPairing.Pair(new[]
        {
            @"C:\models\A\Modelfile.gguf",
            @"C:\models\A\mmproj.gguf",
            @"C:\models\A\model.gguf",
        });
        Assert.True(map.ContainsKey(@"C:\models\A\model.gguf"));
        Assert.Equal(@"C:\models\A\mmproj.gguf", map[@"C:\models\A\model.gguf"]);
    }
}
