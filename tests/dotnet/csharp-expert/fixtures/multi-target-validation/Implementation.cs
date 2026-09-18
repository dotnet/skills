internal static class PlatformName
{
    public static string Current()
    {
#if NET8_0
        return "net8";
#elif NET10_0
        return "net8";
#else
#error Unexpected target framework
#endif
    }
}
