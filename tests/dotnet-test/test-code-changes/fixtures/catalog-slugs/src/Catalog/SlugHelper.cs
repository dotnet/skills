using System.Text;

namespace Catalog;

public static class SlugHelper
{
    public static string ToSlug(string text)
    {
        var slug = new StringBuilder(text.Length);
        foreach (var c in text.Trim().ToLowerInvariant())
        {
            slug.Append(char.IsAsciiLetterOrDigit(c) ? c : '-');
        }

        return slug.ToString().Trim('-');
    }
}
