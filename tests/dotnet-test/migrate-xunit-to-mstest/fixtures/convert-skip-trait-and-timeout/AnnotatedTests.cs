using Xunit;
namespace MyApp.Tests;

[Trait("Category", "Unit")]
public class AnnotatedTests
{
    [Fact(Skip = "broken")]
    public void Broken() => Assert.True(false);

    [Fact(Timeout = 5000)]
    [Trait("Owner", "alice")]
    public void Slow() => Assert.True(true);
}
