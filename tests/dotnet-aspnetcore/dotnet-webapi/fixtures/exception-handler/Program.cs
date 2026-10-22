var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.MapGet("/api/items/{id:int}", static (int id) =>
{
    if (id <= 0)
    {
        throw new ArgumentException("The ID must be positive.", nameof(id));
    }

    throw new KeyNotFoundException($"Item {id} was not found.");
});

app.Run();

public partial class Program;
