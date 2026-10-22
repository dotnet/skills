namespace Contoso.Catalog;

public static class CatalogItem
{
    public static string Format(string sku, bool inStock) =>
        $"{sku}:{(inStock ? "in-stock" : "out-of-stock")}";
}
