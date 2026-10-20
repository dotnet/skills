var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
app.MapPost("/score", () => Results.StatusCode(StatusCodes.Status501NotImplemented));
app.Run();
