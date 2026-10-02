namespace Warehouse;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();

        builder.Services.AddScoped<IInventoryService, InventoryService>();
        builder.Services.AddSingleton<InventoryViewModel>();
        builder.Services.AddSingleton<InventoryPage>();
        builder.Services.AddSingleton<ItemDetailsViewModel>();
        builder.Services.AddSingleton<ItemDetailsPage>();

        return builder.Build();
    }
}
