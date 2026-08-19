namespace TextTools;

public static class TextNormalizer
{
    public static string CollapseSpaces(string value) =>
        string.Join(' ', value.Split(' ', StringSplitOptions.RemoveEmptyEntries));
}
