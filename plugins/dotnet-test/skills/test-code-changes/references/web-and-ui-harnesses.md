# Web and UI test harnesses

## ASP.NET Core endpoints and pages

Reuse the suite's app factory; create one as below if none exists. If `Program` is inaccessible, put `public partial class Program { }` after the top-level statements in the app, not in the test project. Use `ConfigureTestServices` (from `Microsoft.AspNetCore.TestHost`) after the app's registrations, and remove the old registration before adding the test double.

```csharp
using var app = new WebApplicationFactory<Program>();
using var factory = app.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
{
    services.RemoveAll<IOrderStore>();
    services.AddSingleton<IOrderStore>(new FakeOrderStore());
}));
using var client = factory.CreateClient();
using var response = await client.GetAsync("/orders/42");
Assert.Equal(HttpStatusCode.OK, response.StatusCode);
Assert.Equal("Ready", await response.Content.ReadAsStringAsync());
```

For proxy routing and transforms, inject a fake downstream handler if the forwarder accepts one, and assert what it receives. Use a loopback destination if the forwarder needs a listening URL or the behavior depends on real network transport. TestServer has no listening port.

## gRPC

Use the app's factory without the store override above. Pass TestServer's handler to the generated client's channel; the base address alone would send requests over the network. This tests the service and interceptors, not Kestrel's HTTP/2 or TLS configuration.

```csharp
using var channel = GrpcChannel.ForAddress(factory.Server.BaseAddress,
    new GrpcChannelOptions { HttpHandler = factory.Server.CreateHandler() });
var orders = new Orders.OrdersClient(channel);
var reply = await orders.GetOrderAsync(new GetOrderRequest { Id = 42 });
Assert.Equal("Pending", reply.Status);
```

## Forms with antiforgery

Use the app's factory seeded with a `Pending` order. This tests the POST handler directly at a known URL with the default token field name. Take the cookie and hidden field from the same GET and POST with the same client. To test form wiring, assert the rendered action and field names too. Use HTTPS for secure cookies and disable redirects to assert the POST response itself.

```csharp
using var client = factory.CreateClient(new()
{
    BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = true
});
var page = await new HtmlParser().ParseDocumentAsync(await client.GetStringAsync("/orders/edit"));
var token = page.QuerySelector("#edit-order input[name='__RequestVerificationToken']")
    ?.GetAttribute("value") ?? throw new InvalidOperationException("Missing antiforgery token.");
using var form = new FormUrlEncodedContent(new Dictionary<string, string>
    { ["__RequestVerificationToken"] = token, ["status"] = "Shipped" });
using var saved = await client.PostAsync("/orders/edit", form);
Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
Assert.Equal("Shipped", await client.GetStringAsync("/orders/42"));
```

## Blazor components

Use bUnit's render and DOM-event APIs for component behavior that doesn't depend on a real browser. bUnit 2 uses `BunitContext` and `Render`; keep `TestContext` and `RenderComponent` in a suite on bUnit 1.

```csharp
using var ctx = new BunitContext();
var editor = ctx.Render<OrderEditor>(parameters => parameters.Add(p => p.InitialStatus, "Pending"));
editor.Find("button").Click();
editor.Find("[role=status]").MarkupMatches("""<p role="status">Shipped</p>""");
```

## Browser fixtures

Reuse the existing Playwright fixture's host and page rather than adding another browser stack. If the fixture can't run here (for example, its browser can't be installed), add a regression test to the repo's suite one layer down: an in-process request, form post, or component render that exercises the same server or component behavior. Report the browser tests as not run and name any browser-only behavior left uncovered.

```csharp
await page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
await Assertions.Expect(page.GetByRole(AriaRole.Status)).ToHaveTextAsync("Order saved");
```
