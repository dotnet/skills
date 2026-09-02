using Xunit;

namespace MyApp.Tests;

public class ConditionalTests
{
    [SkippableFact]
    public void OnlyOnWindows()
    {
        Skip.IfNot(OperatingSystem.IsWindows());
        Assert.True(true);
    }

    [SkippableTheory]
    [InlineData("feature-a")]
    [InlineData("feature-b")]
    public void OnlyWithFeature(string feature)
    {
        Skip.If(string.IsNullOrEmpty(Environment.GetEnvironmentVariable(feature)));
        Assert.NotNull(feature);
    }
}
