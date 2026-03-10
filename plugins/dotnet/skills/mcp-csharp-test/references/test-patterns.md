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

Test HTTP MCP servers using ASP.NET Core's test infrastructure:

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
    public async Task HealthEndpoint_ReturnsOk()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/health");
        response.EnsureSuccessStatusCode();
    }
}
```

**Note:** Requires `<InternalsVisibleTo Include="YourTest.Project" />` or a public `Program` class.

## Input Validation Tests

```csharp
public class ValidationTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(101)]
    public void Search_ClampsInvalidLimit(int invalidLimit)
    {
        var result = SearchTools.Search("query", limit: invalidLimit);
        result.Should().NotBeNull();
    }

    [Fact]
    public void Search_HandlesSpecialCharacters()
    {
        var result = SearchTools.Search("'; DROP TABLE users; --");
        result.Should().NotContain("DROP TABLE");
    }
}
```

## Test Categories

Organize tests with traits for selective execution:

```csharp
[Trait("Category", "Unit")]
public class UnitTests { ... }

[Trait("Category", "Integration")]
public class IntegrationTests { ... }
```

Run by category:
```bash
dotnet test --filter "Category=Unit"
dotnet test --filter "Category=Integration"
```

## Coverage Reporting

```bash
# Add coverage collector
dotnet add package coverlet.collector

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"

# Generate HTML report
dotnet tool install --global dotnet-reportgenerator-globaltool
reportgenerator \
  -reports:TestResults/**/coverage.cobertura.xml \
  -targetdir:coveragereport
```
