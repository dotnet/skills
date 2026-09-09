using Alpha.Configuration;

namespace NamespaceCollision.Tests;

public sealed class AlphaSettingsTests
{
    public void DefaultsToWestRegion()
    {
        var settings = new Settings();

        _ = settings.Region;
    }
}
