using Xunit;

namespace MyApp.Tests;

public class DatabaseTests
{
    [Fact]
    [DatabaseSetup]
    public void Query_ReturnsResults()
    {
        Assert.True(true);
    }
}
