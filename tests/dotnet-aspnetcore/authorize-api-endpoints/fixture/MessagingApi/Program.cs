using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using MessagingApi.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<MessagingDbContext>(options =>
    options.UseInMemoryDatabase("MessagingDb"));
builder.Services.AddControllers();

// Authentication is already configured: the current user arrives as a JWT bearer token.
// Claims available on User include the subject (sub / NameIdentifier), tenant ("tid"), and role.
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();
builder.Services.AddAuthorization();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
