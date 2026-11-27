namespace Catalog;

public static class ProductUrlBuilder
{
    public static string For(string category, string productName, int productId) =>
        $"/products/{SlugHelper.ToSlug(category)}/{SlugHelper.ToSlug(productName)}-{productId}";
}
