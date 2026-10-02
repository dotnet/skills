public static class Slug
{
    public static string Create(string value)
        => string.Join("-", value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .ToLowerInvariant();
}
