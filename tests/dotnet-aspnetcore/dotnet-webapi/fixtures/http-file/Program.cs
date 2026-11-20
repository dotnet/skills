var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.MapGet("/api/catalog", () => TypedResults.Ok(Array.Empty<CatalogItem>()));
app.MapGet("/api/catalog/{id:int}", (int id) =>
    id == 1
        ? Results.Ok(new CatalogItem(1, "Notebook", 4.95m))
        : Results.NotFound());
app.MapPost("/api/catalog", (CreateCatalogItem request) =>
    TypedResults.Created("/api/catalog/1", new CatalogItem(1, request.Name, request.Price)));
app.MapDelete("/api/catalog/{id:int}", (int id) =>
    id == 1 ? Results.NoContent() : Results.NotFound());

app.Run();

public sealed record CreateCatalogItem(string Name, decimal Price);
public sealed record CatalogItem(int Id, string Name, decimal Price);

public partial class Program;
