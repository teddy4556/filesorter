using FileSorter.Core.Models;
using Xunit;

namespace FileSorter.Tests;

public class SanityTest
{
    [Fact]
    public void RulesFile_DefaultVersion_IsZero()
    {
        var r = new RulesFile();
        Assert.Equal(0, r.Version);
    }

    [Fact]
    public void Scaffold_Loads()
    {
        Assert.True(true);
    }
}