using Web;

namespace Tests;

public class SmokeTests
{
    public WebService Dependency { get; } = new();
}
