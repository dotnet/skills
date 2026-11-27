namespace Catalog;

public static class CatalogApp
{
    public static WebApplication Build(string[] args)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ApplicationName = typeof(CatalogApp).Assembly.GetName().Name
        });
        builder.Services.AddRazorPages();
        var app = builder.Build();
        app.MapRazorPages();
        app.MapGet("/", () => Results.Content("Catalog"));
        return app;
    }
}
