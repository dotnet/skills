using Xunit;

namespace MyApp.Tests;

[TestCaseOrderer("MyApp.Tests.AlphabeticalOrderer", "TestProject")]
public class OrderedTests
{
    [Fact]
    public void Test_A() => Assert.True(true);

    [Fact]
    public void Test_B() => Assert.True(true);
}
