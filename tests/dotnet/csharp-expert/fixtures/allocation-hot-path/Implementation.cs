internal static class WordCounter
{
    public static int CountAsciiWords(string input)
    {
        return input.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
    }
}
