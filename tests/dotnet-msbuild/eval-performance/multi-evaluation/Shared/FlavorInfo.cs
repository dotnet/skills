namespace Shared;

public static class FlavorInfo
{
    public static string Name =>
#if FLAVOR_ALPHA
        "Alpha";
#elif FLAVOR_BETA
        "Beta";
#else
        "Default";
#endif
}
