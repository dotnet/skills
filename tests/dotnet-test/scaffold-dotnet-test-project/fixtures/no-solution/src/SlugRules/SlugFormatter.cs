namespace SlugRules;

public static class SlugFormatter
{
    public static string Normalize(string value) =>
        string.Join('-', value
            .Trim()
            .ToLowerInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));
}
