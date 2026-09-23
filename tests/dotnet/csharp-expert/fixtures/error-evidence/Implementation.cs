internal static class CodeReader
{
    public static string Read(object? value)
    {
        return ((string)value!).Trim().ToUpperInvariant();
    }
}
