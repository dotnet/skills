var expected =
#if NET8_0
    "net8";
#elif NET10_0
    "net10";
#else
#error Unexpected target framework
#endif

var actual = PlatformName.Current();

if (actual != expected)
{
    throw new InvalidOperationException($"Expected {expected}, got {actual}.");
}

Console.WriteLine($"{actual}-target-ok");
