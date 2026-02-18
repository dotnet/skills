# C# MCP Server Testing Guide

## Overview

This guide provides comprehensive testing patterns for C# MCP servers, including unit testing, integration testing, mocking strategies, and evaluation creation.

---

## Project Structure for Testing

```
MyMcpServer.sln
├── MyMcpServer/
│   ├── MyMcpServer.csproj
│   ├── Program.cs
│   └── Tools/
│       └── MyTools.cs
└── MyMcpServer.Tests/
    ├── MyMcpServer.Tests.csproj
    ├── Unit/
    │   └── MyToolsTests.cs
    ├── Integration/
    │   └── ServerIntegrationTests.cs
    ├── Helpers/
    │   └── MockHttpMessageHandler.cs
    └── Fixtures/
        └── McpServerFixture.cs
```

---

## Test Project Setup

### Create Test Project

```bash
dotnet new xunit -n MyMcpServer.Tests
cd MyMcpServer.Tests
dotnet add reference ../MyMcpServer/MyMcpServer.csproj
```

### Add Testing Packages

```xml
<!-- MyMcpServer.Tests.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.*" />
    <PackageReference Include="xunit" Version="2.*" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.*" />
    <PackageReference Include="coverlet.collector" Version="6.*" />
    <PackageReference Include="FluentAssertions" Version="6.*" />
    <PackageReference Include="Moq" Version="4.*" />
    <PackageReference Include="ModelContextProtocol" Version="*-*" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\MyMcpServer\MyMcpServer.csproj" />
  </ItemGroup>
</Project>
```

---

## Unit Testing Patterns

### Testing Simple Tools

```csharp
using FluentAssertions;
using Xunit;

namespace MyMcpServer.Tests.Unit;

public class EchoToolTests
{
    [Fact]
    public void Echo_WithValidMessage_ReturnsFormattedEcho()
    {
        // Arrange
        var message = "Hello, World!";

        // Act
        var result = EchoTool.Echo(message);

        // Assert
        result.Should().Be("Echo: Hello, World!");
    }

    [Fact]
    public void Echo_WithEmptyMessage_ReturnsEmptyEcho()
    {
        // Act
        var result = EchoTool.Echo("");

        // Assert
        result.Should().Be("Echo: ");
    }

    [Fact]
    public void Echo_WithSpecialCharacters_PreservesCharacters()
    {
        // Arrange
        var message = "Hello <World> & \"Friends\"!";

        // Act
        var result = EchoTool.Echo(message);

        // Assert
        result.Should().Contain("<World>");
        result.Should().Contain("&");
    }
}
```

### Testing Parameterized Tools

```csharp
public class RandomNumberToolTests
{
    [Theory]
    [InlineData(1, 10)]
    [InlineData(0, 0)]
    [InlineData(-100, 100)]
    [InlineData(1, 1000000)]
    public void GetRandomNumber_ReturnsNumberInRange(int min, int max)
    {
        // Act
        var result = RandomNumberTools.GetRandomNumber(min, max);

        // Assert
        result.Should().BeGreaterThanOrEqualTo(min);
        result.Should().BeLessThanOrEqualTo(max);
    }

    [Fact]
    public void GetRandomNumber_WithSwappedMinMax_HandlesGracefully()
    {
        // This tests defensive programming
        // Act & Assert - should not throw
        var result = RandomNumberTools.GetRandomNumber(max: 1, min: 10);
        
        // Result should be in the valid range regardless of order
        result.Should().BeGreaterThanOrEqualTo(1);
        result.Should().BeLessThanOrEqualTo(10);
    }
}
```

### Testing Async Tools

```csharp
public class AsyncToolTests
{
    [Fact]
    public async Task FetchData_WithValidUrl_ReturnsContent()
    {
        // Arrange
        var handler = new MockHttpMessageHandler("""
            {"status": "ok", "data": [1, 2, 3]}
            """);
        var httpClient = new HttpClient(handler);

        // Act
        var result = await ApiTools.FetchData(httpClient, "https://api.example.com/data");

        // Assert
        result.Should().Contain("ok");
        result.Should().Contain("data");
    }

    [Fact]
    public async Task FetchData_WithTimeout_ReturnsErrorMessage()
    {
        // Arrange
        var handler = new MockHttpMessageHandler(simulateTimeout: true);
        var httpClient = new HttpClient(handler);

        // Act
        var result = await ApiTools.FetchData(httpClient, "https://api.example.com/slow");

        // Assert
        result.Should().Contain("Error");
        result.Should().Contain("timeout", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FetchData_SupportsCancellation()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        var handler = new MockHttpMessageHandler(delay: TimeSpan.FromSeconds(10));
        var httpClient = new HttpClient(handler);

        // Act
        cts.CancelAfter(TimeSpan.FromMilliseconds(100));
        
        Func<Task> act = () => ApiTools.FetchData(
            httpClient, 
            "https://api.example.com/data",
            cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
```

---

## Mocking Helpers

### MockHttpMessageHandler

```csharp
// Helpers/MockHttpMessageHandler.cs
using System.Net;

namespace MyMcpServer.Tests.Helpers;

public class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly string _response;
    private readonly HttpStatusCode _statusCode;
    private readonly bool _simulateTimeout;
    private readonly TimeSpan _delay;
    private readonly Exception? _exception;

    public MockHttpMessageHandler(
        string response = "",
        HttpStatusCode statusCode = HttpStatusCode.OK,
        bool simulateTimeout = false,
        TimeSpan delay = default,
        Exception? exception = null)
    {
        _response = response;
        _statusCode = statusCode;
        _simulateTimeout = simulateTimeout;
        _delay = delay;
        _exception = exception;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (_exception != null)
            throw _exception;

        if (_simulateTimeout)
            throw new TaskCanceledException("Request timed out");

        if (_delay > TimeSpan.Zero)
            await Task.Delay(_delay, cancellationToken);

        return new HttpResponseMessage
        {
            StatusCode = _statusCode,
            Content = new StringContent(_response)
        };
    }
}
```

### MockLogger

```csharp
// Helpers/MockLogger.cs
using Microsoft.Extensions.Logging;

namespace MyMcpServer.Tests.Helpers;

public class MockLogger<T> : ILogger<T>
{
    public List<string> LogMessages { get; } = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull 
        => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        LogMessages.Add($"[{logLevel}] {formatter(state, exception)}");
    }
}
```

---

## Integration Testing

### MCP Server Test Fixture

```csharp
// Fixtures/McpServerFixture.cs
using ModelContextProtocol.Client;

namespace MyMcpServer.Tests.Fixtures;

public class McpServerFixture : IAsyncLifetime
{
    public McpClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "TestClient",
            Command = "dotnet",
            Arguments = ["run", "--project", GetProjectPath(), "--no-build"],
            Environment = new Dictionary<string, string>
            {
                ["DOTNET_ENVIRONMENT"] = "Testing"
            }
        });

        Client = await McpClient.CreateAsync(transport);
    }

    public async Task DisposeAsync()
    {
        await Client.DisposeAsync();
    }

    private static string GetProjectPath()
    {
        // Navigate from test bin directory to project
        var testDir = AppContext.BaseDirectory;
        return Path.Combine(testDir, "..", "..", "..", "..", 
            "MyMcpServer", "MyMcpServer.csproj");
    }
}
```

### Integration Tests

```csharp
// Integration/ServerIntegrationTests.cs
using ModelContextProtocol.Protocol;
using MyMcpServer.Tests.Fixtures;

namespace MyMcpServer.Tests.Integration;

public class ServerIntegrationTests : IClassFixture<McpServerFixture>
{
    private readonly McpServerFixture _fixture;

    public ServerIntegrationTests(McpServerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Server_ListsAllExpectedTools()
    {
        // Act
        var tools = await _fixture.Client.ListToolsAsync();

        // Assert
        tools.Should().NotBeEmpty();
        tools.Select(t => t.Name).Should().Contain("echo");
        tools.Select(t => t.Name).Should().Contain("get_random_number");
    }

    [Fact]
    public async Task Tools_HaveDescriptions()
    {
        // Act
        var tools = await _fixture.Client.ListToolsAsync();

        // Assert
        foreach (var tool in tools)
        {
            tool.Description.Should().NotBeNullOrEmpty(
                $"Tool '{tool.Name}' should have a description");
        }
    }

    [Fact]
    public async Task Echo_ReturnsCorrectResult()
    {
        // Arrange
        var testMessage = "Integration Test Message";

        // Act
        var result = await _fixture.Client.CallToolAsync(
            "echo",
            new Dictionary<string, object?> { ["message"] = testMessage });

        // Assert
        result.Content.Should().NotBeEmpty();
        var textContent = result.Content.OfType<TextContentBlock>().First();
        textContent.Text.Should().Contain(testMessage);
    }

    [Fact]
    public async Task CallTool_WithInvalidTool_ReturnsError()
    {
        // Act
        Func<Task> act = () => _fixture.Client.CallToolAsync(
            "nonexistent_tool",
            new Dictionary<string, object?>());

        // Assert
        await act.Should().ThrowAsync<McpProtocolException>();
    }

    [Fact]
    public async Task CallTool_WithMissingRequiredParam_ReturnsError()
    {
        // Act - calling echo without required 'message' parameter
        Func<Task> act = () => _fixture.Client.CallToolAsync(
            "echo",
            new Dictionary<string, object?>());

        // Assert
        await act.Should().ThrowAsync<McpProtocolException>()
            .WithMessage("*message*");
    }
}
```

---

## Testing Error Handling

```csharp
public class ErrorHandlingTests
{
    [Fact]
    public void Tool_WithNullInput_HandlesGracefully()
    {
        // Act
        var result = MyTools.ProcessData(null!);

        // Assert
        result.Should().Contain("Error");
        result.Should().Contain("required", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Tool_WhenApiThrows_ReturnsActionableError()
    {
        // Arrange
        var handler = new MockHttpMessageHandler(
            statusCode: System.Net.HttpStatusCode.TooManyRequests);
        var httpClient = new HttpClient(handler);

        // Act
        var result = await ApiTools.FetchData(httpClient, "test-resource");

        // Assert
        result.Should().Contain("Error");
        result.Should().Contain("rate limit", StringComparison.OrdinalIgnoreCase);
        result.Should().Contain("retry", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Tool_WithPathTraversal_RejectsRequest()
    {
        // Arrange
        var maliciousPath = "../../../etc/passwd";

        // Act
        var result = FileTools.ReadFile(maliciousPath);

        // Assert
        result.Should().Contain("Error");
        result.Should().Contain("denied", StringComparison.OrdinalIgnoreCase);
    }
}
```

---

## Testing with Dependency Injection

```csharp
public class DiToolTests
{
    [Fact]
    public async Task ToolWithDI_UsesInjectedServices()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddHttpClient();
        services.AddLogging();
        services.AddSingleton<IMyService, MockMyService>();
        
        var provider = services.BuildServiceProvider();
        var httpClient = provider.GetRequiredService<IHttpClientFactory>().CreateClient();
        var logger = provider.GetRequiredService<ILogger<MyTools>>();
        var myService = provider.GetRequiredService<IMyService>();

        // Act
        var result = await MyTools.ProcessWithServices(
            httpClient, 
            logger,
            myService,
            "test-input");

        // Assert
        result.Should().NotBeNullOrEmpty();
    }
}

public class MockMyService : IMyService
{
    public Task<string> DoWorkAsync(string input) 
        => Task.FromResult($"Processed: {input}");
}
```

---

## Evaluation Testing

### Evaluation Runner

```csharp
// Evaluations/EvaluationRunner.cs
using System.Xml.Linq;

namespace MyMcpServer.Tests.Evaluations;

public class EvaluationRunner
{
    private readonly McpClient _client;

    public EvaluationRunner(McpClient client)
    {
        _client = client;
    }

    public async Task<EvaluationResult> RunEvaluation(string xmlPath)
    {
        var doc = XDocument.Load(xmlPath);
        var qaPairs = doc.Descendants("qa_pair").ToList();
        var results = new List<QuestionResult>();

        foreach (var qa in qaPairs)
        {
            var question = qa.Element("question")?.Value ?? "";
            var expectedAnswer = qa.Element("answer")?.Value ?? "";

            // This would integrate with an LLM to answer the question
            // using the MCP tools
            var actualAnswer = await GetAnswerUsingTools(question);

            results.Add(new QuestionResult
            {
                Question = question,
                ExpectedAnswer = expectedAnswer,
                ActualAnswer = actualAnswer,
                IsCorrect = NormalizeAnswer(actualAnswer) == NormalizeAnswer(expectedAnswer)
            });
        }

        return new EvaluationResult
        {
            TotalQuestions = results.Count,
            CorrectAnswers = results.Count(r => r.IsCorrect),
            Results = results
        };
    }

    private async Task<string> GetAnswerUsingTools(string question)
    {
        // Integration with LLM would go here
        // For testing, this could use a mock LLM or predefined answers
        throw new NotImplementedException("Integrate with LLM");
    }

    private static string NormalizeAnswer(string answer)
    {
        return answer.Trim().ToLowerInvariant();
    }
}

public record EvaluationResult
{
    public int TotalQuestions { get; init; }
    public int CorrectAnswers { get; init; }
    public double Accuracy => TotalQuestions > 0 
        ? (double)CorrectAnswers / TotalQuestions 
        : 0;
    public List<QuestionResult> Results { get; init; } = new();
}

public record QuestionResult
{
    public string Question { get; init; } = "";
    public string ExpectedAnswer { get; init; } = "";
    public string ActualAnswer { get; init; } = "";
    public bool IsCorrect { get; init; }
}
```

### Sample Evaluation File

```xml
<?xml version="1.0" encoding="UTF-8"?>
<evaluation>
  <metadata>
    <server_name>MyMcpServer</server_name>
    <version>1.0.0</version>
  </metadata>

  <qa_pair>
    <question>
      Generate a random number between 50 and 60, then echo that number.
      What is the first digit of the echoed message after "Echo: "?
    </question>
    <answer>5</answer>
    <difficulty>easy</difficulty>
    <required_tools>get_random_number, echo</required_tools>
  </qa_pair>

  <qa_pair>
    <question>
      Search for users with "admin" in their name. How many results are returned
      when limiting to 5 results?
    </question>
    <answer>5</answer>
    <difficulty>medium</difficulty>
    <required_tools>search_users</required_tools>
  </qa_pair>
</evaluation>
```

---

## Code Coverage

### Generate Coverage Report

```bash
# Run tests with coverage
dotnet test --collect:"XPlat Code Coverage" --results-directory ./coverage

# Generate HTML report
dotnet tool install --global dotnet-reportgenerator-globaltool
reportgenerator \
  -reports:coverage/**/coverage.cobertura.xml \
  -targetdir:coverage/report \
  -reporttypes:Html

# Open the report
start coverage/report/index.html
```

### Coverage Thresholds

Add to your CI pipeline:

```yaml
- name: Check Code Coverage
  run: |
    # Extract coverage percentage and fail if below threshold
    COVERAGE=$(grep -oP 'line-rate="\K[^"]+' coverage/coverage.cobertura.xml | head -1)
    if (( $(echo "$COVERAGE < 0.80" | bc -l) )); then
      echo "Coverage $COVERAGE is below 80% threshold"
      exit 1
    fi
```

---

## Test Categories

Use traits to categorize tests:

```csharp
[Trait("Category", "Unit")]
public class UnitTests { }

[Trait("Category", "Integration")]
public class IntegrationTests { }

[Trait("Category", "Slow")]
public class SlowTests { }
```

Run specific categories:

```bash
# Only unit tests
dotnet test --filter "Category=Unit"

# Exclude slow tests
dotnet test --filter "Category!=Slow"
```

---

## Quality Checklist

### Unit Tests
- [ ] All public tool methods have tests
- [ ] Edge cases are covered (null, empty, boundary values)
- [ ] Error conditions are tested
- [ ] Async cancellation is tested

### Integration Tests
- [ ] Server starts and responds
- [ ] All tools are listed correctly
- [ ] Tool execution returns expected results
- [ ] Error responses are appropriate

### Evaluations
- [ ] 10 diverse questions created
- [ ] Questions are read-only
- [ ] Questions require multiple tool calls
- [ ] Answers are verified manually
- [ ] Answers are stable over time

### Coverage
- [ ] Overall coverage > 80%
- [ ] Critical paths have 100% coverage
- [ ] Error handlers are covered
