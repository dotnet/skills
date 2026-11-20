public sealed class ProductService
{
    public async Task<IReadOnlyList<FeaturedProduct>> GetFeaturedAsync()
    {
        await Task.Yield();

        if (Environment.GetEnvironmentVariable("FEATURED_PRODUCTS_FAIL") == "1")
        {
            throw new InvalidOperationException("FEATURED-PRODUCTS-SENSITIVE");
        }

        return
        [
            new FeaturedProduct(1, "Keyboard"),
            new FeaturedProduct(2, "Monitor"),
        ];
    }
}

public sealed record FeaturedProduct(int Id, string Name);
