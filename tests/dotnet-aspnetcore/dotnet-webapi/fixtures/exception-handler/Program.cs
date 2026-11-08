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

app.MapGet("/api/conflict", ThrowConflict);
app.MapGet("/api/failure", ThrowUnexpectedFailure);

app.Run();

static IResult ThrowConflict() =>
    throw new InvalidOperationException("The item is currently locked.");

static IResult ThrowUnexpectedFailure() =>
    throw new Exception("TOP-SECRET-FAILURE");

public partial class Program;
