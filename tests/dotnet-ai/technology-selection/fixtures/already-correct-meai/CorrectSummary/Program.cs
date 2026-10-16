var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
app.MapPost("/summaries", () => Results.StatusCode(StatusCodes.Status501NotImplemented));
app.Run();
