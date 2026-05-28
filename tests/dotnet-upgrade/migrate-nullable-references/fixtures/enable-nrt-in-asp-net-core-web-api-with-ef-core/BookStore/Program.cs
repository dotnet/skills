using Microsoft.EntityFrameworkCore;
using BookStore.Data;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddDbContext<BookStoreContext>(options =>
    options.UseInMemoryDatabase("BookStore"));

var app = builder.Build();
app.MapControllers();
app.Run();
