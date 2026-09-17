internal static class PlatformName
{
    public static string Current()
    {
#if NET8_0
        return "net10";
#elif NET10_0
        return "net10";
#else
#error Unexpected target framework
#endif
    }
}
