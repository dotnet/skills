using Xunit;

namespace Catalog.BrowserTests;

[Trait("Category", "Browser")]
public sealed class CreatePageBrowserTests(BrowserFixture fixture) : IClassFixture<BrowserFixture>
{
    [Fact]
    public async Task Create_form_is_available()
    {
        var page = await fixture.Browser.NewPageAsync();
        try
        {
            var response = await page.GotoAsync(fixture.Address + "/Products/Create");
            Assert.Equal(200, response!.Status);
            Assert.Equal("Create product", await page.GetByRole(Microsoft.Playwright.AriaRole.Heading).InnerTextAsync());
        }
        finally
        {
            await page.CloseAsync();
        }
    }
}
