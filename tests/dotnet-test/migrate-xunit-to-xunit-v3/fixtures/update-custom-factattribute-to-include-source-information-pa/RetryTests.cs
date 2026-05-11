using Xunit;

namespace MyApp.Tests;

public class RetryTests
{
    [RetryFact(MaxRetries = 2)]
    public void Flaky_Test_Retries()
    {
        Assert.True(true);
    }

    [ConditionalTheory(Feature = "Premium")]
    [InlineData("admin")]
    public void Premium_Feature_Test(string role)
    {
        Assert.NotNull(role);
    }
}
