---
name: mcp-csharp-test
description: Guide for testing MCP servers built with the C# SDK. Covers unit testing, integration testing, end-to-end testing with MCP clients, and creating evaluations for LLM effectiveness.
license: Complete terms in LICENSE.txt
---

# MCP C# Server Testing Guide

## Overview

Test your C# MCP server at multiple levels: unit tests for individual tools, integration tests for the full server, and evaluations to measure LLM effectiveness when using your tools.

---

# Process

## 🧪 Testing Approach

### Test Pyramid for MCP Servers

```
        ┌─────────────────┐
        │   Evaluations   │  ← LLM effectiveness tests
        │   (Few, Slow)   │
        ├─────────────────┤
        │  Integration    │  ← Full MCP protocol tests
        │    Tests        │
        ├─────────────────┤
        │   Unit Tests    │  ← Individual tool methods
        │  (Many, Fast)   │
        └─────────────────┘
```

---

## 🔬 Unit Testing

### Setup

Create a test project alongside your MCP server:

```bash
# Create test project
dotnet new xunit -n MyMcpServer.Tests

# Add reference to your MCP server
cd MyMcpServer.Tests
dotnet add reference ../MyMcpServer/MyMcpServer.csproj

# Add testing packages
dotnet add package Moq
dotnet add package FluentAssertions
```

### Testing Static Tool Methods

For tools without dependencies, test directly:

```csharp
using FluentAssertions;
using Xunit;

public class EchoToolTests
{
    [Fact]
    public void Echo_ReturnsFormattedMessage()
    {
        // Arrange
        var message = "Hello, World!";

        // Act
        var result = EchoTool.Echo(message);

        // Assert
        result.Should().Be("Echo: Hello, World!");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Echo_HandlesEmptyInput(string input)
    {
        // Act
        var result = EchoTool.Echo(input);

        // Assert
        result.Should().StartWith("Echo:");
    }
}
```

### Testing Tools with Dependencies

Use dependency injection mocking:

```csharp
using Moq;
using Xunit;

public class ApiToolTests
{
    [Fact]
    public async Task FetchData_ReturnsApiResponse()
    {
        // Arrange
        var mockHttp = new Mock<HttpClient>();
        var expectedData = "{\"id\": 1, \"name\": \"Test\"}";
        
        // Setup mock (use HttpMessageHandler for real scenarios)
        var handler = new MockHttpMessageHandler(expectedData);
        var httpClient = new HttpClient(handler);

        // Act
        var result = await ApiTools.FetchData(httpClient, "resource-1");

        // Assert
        result.Should().Contain("Test");
    }

    [Fact]
    public async Task FetchData_HandlesHttpError()
    {
        // Arrange
        var handler = new MockHttpMessageHandler(
            statusCode: System.Net.HttpStatusCode.NotFound);
        var httpClient = new HttpClient(handler);

        // Act
        var result = await ApiTools.FetchData(httpClient, "nonexistent");

        // Assert
        result.Should().Contain("Error");
    }
}

// Helper for mocking HttpClient
public class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly string _response;
    private readonly System.Net.HttpStatusCode _statusCode;

    public MockHttpMessageHandler(
        string response = "", 
        System.Net.HttpStatusCode statusCode = System.Net.HttpStatusCode.OK)
    {
        _response = response;
        _statusCode = statusCode;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, 
        CancellationToken cancellationToken)
    {
        return Task.FromResult(new HttpResponseMessage
        {
            StatusCode = _statusCode,
            Content = new StringContent(_response)
        });
    }
}
```

### Testing Input Validation

```csharp
public class SearchToolTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(101)]
    public void Search_ClampsInvalidLimit(int invalidLimit)
    {
        // Act
        var result = SearchTools.Search("query", limit: invalidLimit);

        // Assert - should not throw, should clamp to valid range
        result.Should().NotBeNull();
    }

    [Fact]
    public void Search_RejectsMaliciousInput()
    {
        // Arrange
        var maliciousQuery = "'; DROP TABLE users; --";

        // Act
        var result = SearchTools.Search(maliciousQuery);

        // Assert - should sanitize or reject
        result.Should().NotContain("DROP TABLE");
    }
}
```

---

## 🔗 Integration Testing

### Testing Full MCP Server

Create integration tests that use the MCP client:

```csharp
using ModelContextProtocol.Client;
using Xunit;

public class McpServerIntegrationTests : IAsyncLifetime
{
    private McpClient _client = null!;

    public async Task InitializeAsync()
    {
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "TestClient",
            Command = "dotnet",
            Arguments = ["run", "--project", "../MyMcpServer/MyMcpServer.csproj"]
        });

        _client = await McpClient.CreateAsync(transport);
    }

    public async Task DisposeAsync()
    {
        await _client.DisposeAsync();
    }

    [Fact]
    public async Task Server_RegistersExpectedTools()
    {
        // Act
        var tools = await _client.ListToolsAsync();

        // Assert
        tools.Should().Contain(t => t.Name == "echo");
        tools.Should().Contain(t => t.Name == "get_random_number");
    }

    [Fact]
    public async Task EchoTool_ReturnsExpectedResult()
    {
        // Act
        var result = await _client.CallToolAsync(
            "echo",
            new Dictionary<string, object?> { ["message"] = "Test" });

        // Assert
        var textContent = result.Content.OfType<TextContentBlock>().First();
        textContent.Text.Should().Contain("Test");
    }

    [Fact]
    public async Task RandomNumberTool_ReturnsNumberInRange()
    {
        // Act
        var result = await _client.CallToolAsync(
            "get_random_number",
            new Dictionary<string, object?> 
            { 
                ["min"] = 10, 
                ["max"] = 20 
            });

        // Assert
        var text = result.Content.OfType<TextContentBlock>().First().Text;
        var number = int.Parse(text);
        number.Should().BeInRange(10, 20);
    }
}
```

### Testing HTTP Transport

```csharp
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

public class HttpServerIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HttpServerIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task HealthEndpoint_ReturnsOk()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/health");

        // Assert
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task McpEndpoint_AcceptsRequests()
    {
        // Arrange
        var client = _factory.CreateClient();
        var initRequest = new
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

        // Act
        var response = await client.PostAsJsonAsync("/", initRequest);

        // Assert
        response.EnsureSuccessStatusCode();
    }
}
```

---

## 📊 Evaluation Creation

Evaluations test how effectively LLMs can use your MCP server to accomplish tasks.

### Evaluation Guidelines

Create 10 evaluation questions that are:

- **Independent**: Not dependent on other questions
- **Read-only**: Only use non-destructive operations
- **Complex**: Require multiple tool calls
- **Realistic**: Based on real use cases
- **Verifiable**: Have a single, clear answer
- **Stable**: Answer won't change over time

### Evaluation Process

1. **Explore Available Data**
   - List all tools and understand their capabilities
   - Use read-only tools to explore the data landscape

2. **Create Complex Questions**
   - Questions should require reasoning and multiple tool calls
   - Avoid questions with obvious, single-tool answers

3. **Verify Answers**
   - Manually solve each question to confirm the answer
   - Ensure the answer is stable and deterministic

### Evaluation XML Format

```xml
<?xml version="1.0" encoding="UTF-8"?>
<evaluation>
  <metadata>
    <server_name>MyMcpServer</server_name>
    <version>1.0.0</version>
    <created_date>2026-02-11</created_date>
  </metadata>
  
  <qa_pair>
    <question>
      Using the user search tool, find all users in the "engineering" team 
      who joined after 2024. What is the email domain most commonly used 
      by these users?
    </question>
    <answer>company.com</answer>
    <difficulty>medium</difficulty>
    <required_tools>search_users, list_teams</required_tools>
  </qa_pair>
  
  <qa_pair>
    <question>
      Find the project with the most active contributors in the last month.
      What is the project's internal ID?
    </question>
    <answer>proj-42</answer>
    <difficulty>hard</difficulty>
    <required_tools>list_projects, get_project_stats</required_tools>
  </qa_pair>
  
  <!-- More qa_pairs... -->
</evaluation>
```

### Example Evaluation Questions

**Good questions:**
- "Find the user who has created the most issues in the 'backend' repository this year. What is their username?"
- "Which team has the highest average code review turnaround time? Return the team name."
- "How many open issues are labeled 'critical' and assigned to users in the 'security' team?"

**Bad questions:**
- "What is 2 + 2?" (doesn't use tools)
- "List all users" (too simple, no reasoning required)
- "Create a new issue..." (not read-only)
- "What's the current weather?" (answer changes)

---

## 🏃 Running Tests

### Run All Tests

```bash
cd MyMcpServer.Tests
dotnet test
```

### Run with Coverage

```bash
dotnet add package coverlet.collector

dotnet test --collect:"XPlat Code Coverage"

# View coverage report
dotnet tool install --global dotnet-reportgenerator-globaltool
reportgenerator -reports:TestResults/**/coverage.cobertura.xml -targetdir:coveragereport
```

### Run Specific Tests

```bash
# Run tests matching a pattern
dotnet test --filter "FullyQualifiedName~EchoTool"

# Run only unit tests
dotnet test --filter "Category=Unit"

# Run integration tests
dotnet test --filter "Category=Integration"
```

---

**Load [📋 Testing Guide](./references/testing_guide.md) for detailed patterns and examples.**

---

## Related Skills

- **mcp-csharp-create** - Creating your MCP server
- **mcp-csharp-debug** - Running and debugging
- **mcp-csharp-publish** - Publishing and deployment

---

# Reference Files

## 📚 Documentation Library

- [📋 Testing Guide](./references/testing_guide.md) - Complete testing patterns and examples
- **xUnit Documentation**: https://xunit.net/docs/getting-started/netcore/cmdline
- **FluentAssertions**: https://fluentassertions.com/
- **Moq**: https://github.com/moq/moq
