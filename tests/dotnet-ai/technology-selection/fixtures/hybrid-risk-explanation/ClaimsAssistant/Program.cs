var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
app.MapPost("/claims/risk", () => Results.StatusCode(StatusCodes.Status501NotImplemented));
app.Run();
