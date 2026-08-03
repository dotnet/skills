using Microsoft.EntityFrameworkCore;
using MessagingApi.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<MessagingDbContext>(options =>
    options.UseInMemoryDatabase("MessagingDb"));
builder.Services.AddControllers();

var app = builder.Build();

app.MapControllers();

app.Run();
