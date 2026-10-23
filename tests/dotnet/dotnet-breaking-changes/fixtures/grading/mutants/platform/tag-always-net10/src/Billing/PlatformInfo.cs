namespace Billing;

// Grading mutant: Tag() ignores the target and always reports net10, so only a net8 assertion detects it.
// Current() stays correct, so only tests of Tag() can kill this mutant.
public static class PlatformInfo
{
    private const string ModernTag = "net10";
    private const string LegacyTag = "net8";

    public static string Current()
    {
#if NET8_0
        return Label(LegacyTag);
#else
        return Label(ModernTag);
#endif
    }

    public static string Tag() => ModernTag;

    private static string Label(string tag) => $"platform:{tag}";
}
