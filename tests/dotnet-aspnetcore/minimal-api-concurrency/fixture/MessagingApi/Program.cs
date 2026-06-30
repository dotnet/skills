using Microsoft.EntityFrameworkCore;
using MessagingApi.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<MessagingDbContext>(options =>
    options.UseInMemoryDatabase("MessagingDb"));

var app = builder.Build();

var namespaces = app.MapGroup("/namespaces");

namespaces.MapGet("/", async (MessagingDbContext db) =>
    await db.Namespaces.AsNoTracking().Where(n => !n.IsDeleted).ToListAsync());

app.Run();