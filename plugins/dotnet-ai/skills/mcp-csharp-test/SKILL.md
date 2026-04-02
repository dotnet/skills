---
name: mcp-csharp-test
description: >
  Test C# MCP servers at multiple levels: unit tests for individual tools and integration
  tests using the MCP client SDK.
  USE FOR: unit testing MCP tool methods, integration testing with in-memory MCP
  client/server, end-to-end testing via MCP protocol,
  testing HTTP MCP servers with WebApplicationFactory, mocking dependencies in tool tests,
  creating evaluations for MCP servers, writing eval questions, measuring tool quality.
  DO NOT USE FOR: testing MCP clients (this is server testing only), load or performance
  testing, testing non-.NET MCP servers, debugging server issues (use mcp-csharp-debug).
---

# C# MCP Server Testing

Test MCP servers at two levels: unit tests for individual tool methods, and integration tests that exercise the full MCP protocol in-memory.

## When to Use

- Adding automated tests to an MCP server
- Testing individual tool methods with mocked dependencies
- Writing integration tests that validate tool listing and invocation via MCP protocol
- Setting up CI test pipelines for MCP servers

## Stop Signals

- **No server yet?** → Use `mcp-csharp-create` first
- **Server not running?** → Use `mcp-csharp-debug`
- **Just need manual/interactive testing?** → Use `mcp-csharp-debug` for MCP Inspector

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| MCP server project path | Yes | Path to the server `.csproj` being tested |
| Test framework | Recommended | Default: xUnit. Also supports NUnit or MSTest |
| Transport type | Recommended | Determines integration test approach (stdio vs HTTP) |

## Workflow

### Step 1: Create the test project

```bash
dotnet new xunit -n <ServerName>.Tests
cd <ServerName>.Tests
dotnet add reference ../<ServerName>/<ServerName>.csproj
dotnet add package ModelContextProtocol
dotnet add package Moq
dotnet add package FluentAssertions
```

### Step 2: Write unit tests for tool methods

Test tool methods directly — fastest and most isolated:

```csharp
public class MyToolTests
{
    [Fact]
    public void Echo_ReturnsFormattedMessage()
    {
        var result = MyTools.Echo("Hello");
        result.Should().Be("Echo: Hello");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Echo_HandlesEdgeCases(string input)
    {
        var result = MyTools.Echo(input);
        result.Should().StartWith("Echo:");
    }
}
```

For tools with DI dependencies, mock the dependency:
```csharp
public class ApiToolTests
{
    [Fact]
    public async Task FetchData_ReturnsApiResponse()
    {
        var handler = new MockHttpMessageHandler("""{"id": 1}""");
        var httpClient = new HttpClient(handler);

        var result = await ApiTools.FetchData(httpClient, "resource-1");
        result.Should().Contain("id");
    }
}
```

### Step 3: Write integration tests with MCP client

Test the full MCP protocol using a client-server connection:

```csharp
using ModelContextProtocol.Client;

public class ServerIntegrationTests : IAsyncLifetime
{
    private McpClient _client = null!;

    public async Task InitializeAsync()
    {
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "TestClient",
            Command = "dotnet",
            Arguments = ["run", "--project", "../<ServerName>/<ServerName>.csproj"]
        });
        _client = await McpClient.CreateAsync(transport);
    }

    public async Task DisposeAsync() => await _client.DisposeAsync();

    [Fact]
    public async Task Server_ListsExpectedTools()
    {
        var tools = await _client.ListToolsAsync();
        tools.Should().Contain(t => t.Name == "echo");
    }

    [Fact]
    public async Task Tool_ReturnsExpectedResult()
    {
        var result = await _client.CallToolAsync("echo",
            new Dictionary<string, object?> { ["message"] = "Test" });
        var text = result.Content.OfType<TextContentBlock>().First().Text;
        text.Should().Contain("Test");
    }
}
```

**For the SDK's `ClientServerTestBase` (in-memory testing) and HTTP testing with `WebApplicationFactory`**, see [references/test-patterns.md](references/test-patterns.md).

### Step 4: Run tests

```bash
# Run all tests
dotnet test

# Run a specific test class
dotnet test --filter "FullyQualifiedName~MyToolTests"

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"
```

### Step 5: Write evaluations

Evaluations measure how well an LLM uses your tools. Good evaluation questions should be:
- **Read-only and non-destructive** — never modify data as a side effect
- **Deterministic** — have a single verifiable correct answer
- **Multi-step** — require the LLM to call multiple tools or reason across results

For the evaluation format, example questions, and detailed guidance, see [references/evaluations.md](references/evaluations.md).

## Validation

- [ ] Unit tests cover all tool methods, including edge cases
- [ ] Integration tests verify tool listing via `ListToolsAsync()`
- [ ] Integration tests verify tool invocation via `CallToolAsync()`
- [ ] All tests pass: `dotnet test`
- [ ] Tests run in CI without manual setup

## Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| Integration test hangs on `CreateAsync` | Server fails to start. Verify `dotnet build` succeeds first. For stdio, ensure no stdout logging |
| `StdioClientTransport` not finding project | Use the correct relative path to `.csproj` from the test project directory |
| Tests pass locally but fail in CI | Run `dotnet build` before test execution. Use `--no-build` only after an explicit build step |
| Mocking `HttpClient` is awkward | Mock `HttpMessageHandler`, not `HttpClient` directly. See [references/test-patterns.md](references/test-patterns.md) |
| Full test suite runs are slow | Use `--filter` for development. Run the full suite only for CI verification |

## Related Skills

- `mcp-csharp-create` — Create a new MCP server project
- `mcp-csharp-debug` — Running and interactive debugging
- `mcp-csharp-publish` — NuGet, Docker, Azure deployment

## Reference Files

- [references/test-patterns.md](references/test-patterns.md) — Complete test code examples: `ClientServerTestBase` in-memory pattern, `WebApplicationFactory` for HTTP, `MockHttpMessageHandler` helper, test categorization, coverage reporting. **Load when:** writing integration tests or need detailed mock patterns.
- [references/evaluations.md](references/evaluations.md) — Evaluation format, question design principles, and example eval questions. **Load when:** user asks about evaluations, eval questions, or measuring tool quality.

## More Info

- [xUnit documentation](https://xunit.net/docs/getting-started/netcore/cmdline) — Getting started with xUnit for .NET
