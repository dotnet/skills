# Test Patterns

Complete code patterns for testing C# MCP servers at every level.

## MockHttpMessageHandler Helper

Reusable mock for tools that use `HttpClient`:

```csharp
public class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly string _response;
    private readonly HttpStatusCode _statusCode;

    public MockHttpMessageHandler(
        string response = "",
        HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        _response = response;
        _statusCode = statusCode;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken) =>
        Task.FromResult(new HttpResponseMessage
        {
            StatusCode = _statusCode,
            Content = new StringContent(_response)
        });
}
```

## ClientServerTestBase (In-Memory Testing)

The SDK provides `ClientServerTestBase` for zero-network integration tests using `System.IO.Pipelines`:

```csharp
using ModelContextProtocol.Tests; // from SDK test utilities

public class MyToolTests : ClientServerTestBase
{
    public MyToolTests(ITestOutputHelper output) : base(output) { }

    protected override void ConfigureServices(
        ServiceCollection services, IMcpServerBuilder builder)
    {
        builder.WithTools<MyTools>();
        // Register any DI services your tools need
        services.AddSingleton<IMyService, FakeMyService>();
    }

    [Fact]
    public async Task MyTool_ReturnsExpected()
    {
        await using var client = await CreateMcpClientForServer();
        var result = await client.CallToolAsync("my_tool",
            new() { ["input"] = "test" },
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(result);
    }
}
```

**Key advantages:**
- In-memory transport — no process spawning, no network
- Full DI support — inject fakes/mocks for external dependencies
- Runs in milliseconds

## HTTP Testing with WebApplicationFactory

Test HTTP MCP servers using ASP.NET Core's test infrastructure.

**Important:** `WebApplicationFactory<Program>` requires access to the `Program` class. Either:
- Add `<InternalsVisibleTo Include="YourServer.Tests" />` to the server's `.csproj`, or
- Make the `Program` class public: `public partial class Program { }`

```csharp
using Microsoft.AspNetCore.Mvc.Testing;

public class HttpServerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HttpServerTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task McpEndpoint_AcceptsInitialize()
    {
        var client = _factory.CreateClient();
        var request = new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "initialize",
            @params = new
            {
                protocolVersion = "2024-11-05",
                capabilities = new { },
                clientInfo = new { name = "test", version = "1.0" }
            }
        };

        var response = await client.PostAsJsonAsync("/mcp", request);
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task McpEndpoint_InvokesTool()
    {
        var client = _factory.CreateClient();

        // First initialize the session
        var init = new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "initialize",
            @params = new
            {
                protocolVersion = "2024-11-05",
                capabilities = new { },
                clientInfo = new { name = "test", version = "1.0" }
            }
        };
        await client.PostAsJsonAsync("/mcp", init);

        // Then call a tool
        var toolCall = new
        {
            jsonrpc = "2.0",
            id = 2,
            method = "tools/call",
            @params = new
            {
                name = "echo",
                arguments = new { message = "hello" }
            }
        };

        var response = await client.PostAsJsonAsync("/mcp", toolCall);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("hello", body);
    }

    [Fact]
    public async Task HealthEndpoint_ReturnsOk()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/health");
        response.EnsureSuccessStatusCode();
    }
}
```
