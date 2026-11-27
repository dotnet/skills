using Microsoft.AspNetCore.Builder;
using Microsoft.Playwright;
using Xunit;

namespace Catalog.BrowserTests;

public sealed class BrowserFixture : IAsyncLifetime
{
    private IPlaywright? playwright;
    private WebApplication? app;

    public IBrowser Browser { get; private set; } = null!;
    public string Address { get; private set; } = "";

    public async Task InitializeAsync()
    {
        Environment.SetEnvironmentVariable("PLAYWRIGHT_BROWSERS_PATH",
            Environment.GetEnvironmentVariable("PLAYWRIGHT_BROWSERS_PATH") ?? "0");
        playwright = await Playwright.CreateAsync();
        Browser = await playwright.Chromium.LaunchAsync(new() { Timeout = 5_000 });

        app = CatalogApp.Build(["--urls", "http://127.0.0.1:0"]);
        await app.StartAsync();
        Address = app.Urls.Single();
    }

    public async Task DisposeAsync()
    {
        if (app is not null)
            await app.DisposeAsync();
        if (Browser is not null)
            await Browser.DisposeAsync();
        playwright?.Dispose();
    }
}
