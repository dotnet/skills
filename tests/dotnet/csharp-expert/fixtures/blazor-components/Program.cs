using BlazorFixture.Components;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRazorComponents();

var app = builder.Build();
app.MapRazorComponents<App>();
app.Run();
