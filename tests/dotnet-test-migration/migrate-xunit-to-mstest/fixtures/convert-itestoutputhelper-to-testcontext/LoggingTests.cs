using Xunit;
using Xunit.Abstractions;

namespace MyApp.Tests;

public class LoggingTests
{
    private readonly ITestOutputHelper _output;
    public LoggingTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void LogsSomething()
    {
        _output.WriteLine("hello from test");
        Assert.True(true);
    }
}
