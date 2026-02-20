namespace MultiTargetLib;

public class MultiClass
{
    public string GetFramework() =>
#if NET8_0
        "net8.0";
#elif NET9_0
        "net9.0";
#else
        "unknown";
#endif
}
