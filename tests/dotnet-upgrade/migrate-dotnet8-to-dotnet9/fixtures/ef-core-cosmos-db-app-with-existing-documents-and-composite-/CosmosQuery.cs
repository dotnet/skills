class CosmosQuery
{
    void FindProducts(ProductContext ctx)
    {
        // Direct Cosmos SQL
        var sql = "SELECT * FROM c WHERE c.Discriminator = 'Product' AND c.Sku = @sku";
        // Also uses sync APIs
        var products = ctx.Products.Where(p => p.Sku == "ABC").ToList();
    }
}
