using Xunit;

namespace MyApp.Tests;

public class CombinatorialTests
{
    [Theory, CombinatorialData]
    public void AllCombinations(bool a, bool b)
    {
        Assert.NotNull($"{a}{b}");
    }

    [StaFact]
    public void StaThread_Test()
    {
        Assert.True(true);
    }
}
