namespace Store;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();

        builder.Services.AddSingleton<IProductService, ProductService>();
        builder.Services.AddTransient<ProductListViewModel>();
        builder.Services.AddTransient<ProductListPage>();
        builder.Services.AddTransient<ProductDetailsViewModel>();
        builder.Services.AddTransient<ProductDetailsPage>();

        return builder.Build();
    }
}
