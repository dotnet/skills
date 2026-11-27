namespace Catalog;

public static class CategoryUrlBuilder
{
    public static string For(string department, string category) =>
        $"/c/{SlugHelper.ToSlug(department)}/{SlugHelper.ToSlug(category)}";
}
