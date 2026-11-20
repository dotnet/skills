var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<IProductService, ProductService>();

var app = builder.Build();

app.MapGet("/api/products/{id:int}", async (
    int id,
    IProductService service,
    CancellationToken cancellationToken) =>
{
    var product = await service.GetByIdAsync(id, cancellationToken);
    return product is null
        ? TypedResults.NotFound()
        : TypedResults.Ok(product);
});

app.Run();

public sealed record ProductResponse(int Id, string Name);

public interface IProductService
{
    Task<ProductResponse?> GetByIdAsync(int id, CancellationToken cancellationToken);
}

public sealed class ProductService : IProductService
{
    public Task<ProductResponse?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken)
    {
        ProductResponse? product = id == 1 ? new ProductResponse(1, "Keyboard") : null;
        return Task.FromResult(product);
    }
}

public partial class Program;
