AssertCount("", 0);
AssertCount("   ", 0);
AssertCount("one", 1);
AssertCount(" one  two three ", 3);

const string input = "alpha beta  gamma delta";
for (var index = 0; index < 1_000; index++)
{
    WordCounter.CountAsciiWords(input);
}

var before = GC.GetAllocatedBytesForCurrentThread();
for (var index = 0; index < 50_000; index++)
{
    if (WordCounter.CountAsciiWords(input) != 4)
    {
        throw new InvalidOperationException("The hot-path result changed.");
    }
}

var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
if (allocated > 1_024)
{
    throw new InvalidOperationException($"Allocated {allocated} bytes.");
}

Console.WriteLine("allocation-ok");

static void AssertCount(string input, int expected)
{
    if (WordCounter.CountAsciiWords(input) != expected)
    {
        throw new InvalidOperationException($"Unexpected count for '{input}'.");
    }
}

internal static class WordCounter
{
    public static int CountAsciiWords(string input)
    {
        return input.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
    }
}
