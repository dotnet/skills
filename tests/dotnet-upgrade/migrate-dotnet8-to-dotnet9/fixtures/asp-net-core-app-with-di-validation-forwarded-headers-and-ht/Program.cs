var builder = WebApplication.CreateBuilder(args);
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddSingleton<ICacheService>(sp =>
    new CacheService(sp.GetRequiredService<IUserService>()));
builder.Services.AddHttpClient("api")
    .ConfigureHttpMessageHandlerBuilder(b =>
    {
        ((HttpClientHandler)b.PrimaryHandler).UseCookies = false;
    });
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor;
});
var app = builder.Build();
app.UseForwardedHeaders();
app.Run();
