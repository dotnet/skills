using Microsoft.EntityFrameworkCore;
using StoreApi.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<StoreDbContext>(options =>
    options.UseInMemoryDatabase("StoreDb"));
builder.Services.AddControllers();

var app = builder.Build();

app.MapControllers();

app.Run();
