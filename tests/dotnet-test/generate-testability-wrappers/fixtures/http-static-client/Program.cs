using Contoso.Sync.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<InventorySyncService>();

var app = builder.Build();
app.Run();
