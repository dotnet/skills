using Xunit;
using Xunit.Abstractions;

namespace MyApp.Tests;

public class OutputLogger
{
    private readonly ITestOutputHelper _output;

    public OutputLogger(ITestOutputHelper output)
    {
        _output = output;
    }

    public void Log(string message) => _output.WriteLine(message);
}
