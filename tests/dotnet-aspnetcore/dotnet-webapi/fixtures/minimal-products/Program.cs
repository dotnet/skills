var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.MapGet("/health", () => TypedResults.Ok(new { status = "healthy" }));

app.Run();

public partial class Program;
