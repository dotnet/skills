namespace Alpha.Configuration.Advanced.Tests;

public sealed class AdvancedOptionsTests
{
    public void DefaultsToEnabled()
    {
        var options = new Options();

        _ = options.Enabled;
    }
}
