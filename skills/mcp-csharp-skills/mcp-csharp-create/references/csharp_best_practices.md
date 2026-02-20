# C# MCP Server Best Practices

## Quick Reference

### Server Naming
- **Format**: `{Service}McpServer` (PascalCase)
- **Examples**: `GitHubMcpServer`, `SlackMcpServer`, `JiraMcpServer`

### Tool Naming
- Use snake_case with service prefix
- Format: `{service}_{action}_{resource}`
- Examples: `github_create_issue`, `slack_send_message`, `jira_list_tasks`

### Response Formats
- Support both JSON and Markdown formats
- JSON for programmatic processing
- Markdown for human readability

### Pagination
- Always respect `limit` parameter
- Return `has_more`, `next_offset`, `total_count`
- Default to 20-50 items

---

## Tool Naming and Design

### Naming Conventions

1. **Use snake_case**: `search_users`, `create_project`, `get_channel_info`
2. **Include service prefix**: Anticipate multi-server environments
   - Use `slack_send_message` instead of `send_message`
   - Use `github_create_issue` instead of `create_issue`
3. **Be action-oriented**: Start with verbs (get, list, search, create, update, delete)
4. **Be specific**: Avoid generic names that could conflict

```csharp
// Good - specific and prefixed
[McpServerTool(Name = "github_create_issue")]
public static string CreateIssue(...) { }

// Bad - too generic
[McpServerTool(Name = "create")]
public static string Create(...) { }
```

### Tool Descriptions

Descriptions must be clear, specific, and include:
- What the tool does
- Parameter explanations
- Example usage
- Error conditions

```csharp
[McpServerTool, Description("""
    Searches for GitHub issues by query string.
    
    Searches across title, body, and comments. Supports GitHub search syntax
    like 'is:open', 'label:bug', 'author:username'.
    
    Returns up to 'limit' results starting from 'offset'.
    Use 'has_more' in the response to determine if more results exist.
    """)]
public static async Task<string> SearchIssues(
    [Description("Search query using GitHub search syntax")] string query,
    [Description("Maximum results to return (1-100, default: 20)")] int limit = 20,
    [Description("Number of results to skip for pagination")] int offset = 0,
    CancellationToken cancellationToken = default)
{
    // Implementation
}
```

### XML Documentation Comments

For `partial` methods, XML comments are automatically converted to descriptions:

```csharp
[McpServerToolType]
public static partial class SearchTools
{
    /// <summary>
    /// Searches for users by name or email.
    /// </summary>
    /// <param name="query">The search query string</param>
    /// <param name="limit">Maximum results to return</param>
    /// <returns>JSON array of matching users</returns>
    [McpServerTool]
    public static partial string SearchUsers(string query, int limit = 20);
}
```

---

## Response Formats

### Supporting Multiple Formats

Provide a `response_format` parameter to let callers choose:

```csharp
public enum ResponseFormat
{
    Json,
    Markdown
}

[McpServerTool, Description("Lists all projects")]
public static string ListProjects(
    [Description("Output format: 'Json' or 'Markdown'")] ResponseFormat format = ResponseFormat.Markdown,
    [Description("Maximum projects to return")] int limit = 20)
{
    var projects = GetProjects(limit);
    
    return format switch
    {
        ResponseFormat.Json => FormatAsJson(projects),
        ResponseFormat.Markdown => FormatAsMarkdown(projects),
        _ => FormatAsMarkdown(projects)
    };
}

private static string FormatAsJson(List<Project> projects)
{
    return JsonSerializer.Serialize(new
    {
        total = projects.Count,
        projects = projects.Select(p => new
        {
            id = p.Id,
            name = p.Name,
            description = p.Description,
            created_at = p.CreatedAt
        })
    }, new JsonSerializerOptions { WriteIndented = true });
}

private static string FormatAsMarkdown(List<Project> projects)
{
    var sb = new StringBuilder();
    sb.AppendLine($"# Projects ({projects.Count})");
    sb.AppendLine();
    
    foreach (var project in projects)
    {
        sb.AppendLine($"## {project.Name} ({project.Id})");
        sb.AppendLine($"- **Created**: {project.CreatedAt:yyyy-MM-dd}");
        if (!string.IsNullOrEmpty(project.Description))
            sb.AppendLine($"- **Description**: {project.Description}");
        sb.AppendLine();
    }
    
    return sb.ToString();
}
```

### Markdown Format Guidelines

- Use headers (`#`, `##`) for structure
- Use lists for multiple items
- Show display names with IDs: `John Doe (U123456)`
- Convert timestamps to human-readable format
- Omit verbose metadata

### JSON Format Guidelines

- Include all available fields
- Use consistent field naming (snake_case)
- Include pagination metadata
- Structure for programmatic parsing

---

## Pagination

### Implementation Pattern

```csharp
[McpServerTool, Description("Lists items with pagination support")]
public static string ListItems(
    [Description("Maximum items to return (1-100)")] int limit = 20,
    [Description("Number of items to skip")] int offset = 0)
{
    // Validate and clamp limit
    limit = Math.Clamp(limit, 1, 100);
    offset = Math.Max(0, offset);
    
    var allItems = GetAllItems();
    var totalCount = allItems.Count;
    var items = allItems.Skip(offset).Take(limit).ToList();
    
    var result = new
    {
        total = totalCount,
        count = items.Count,
        offset = offset,
        items = items,
        has_more = offset + items.Count < totalCount,
        next_offset = offset + items.Count < totalCount 
            ? offset + items.Count 
            : (int?)null
    };
    
    return JsonSerializer.Serialize(result, new JsonSerializerOptions 
    { 
        WriteIndented = true 
    });
}
```

### Response Schema

```json
{
  "total": 150,
  "count": 20,
  "offset": 0,
  "items": [...],
  "has_more": true,
  "next_offset": 20
}
```

---

## Character Limits and Truncation

Prevent overwhelming responses with a character limit:

```csharp
public static class Constants
{
    public const int CharacterLimit = 25000;
}

[McpServerTool]
public static string SearchLargeDataset(string query, int limit = 50)
{
    var results = PerformSearch(query, limit);
    var json = JsonSerializer.Serialize(results);
    
    if (json.Length > Constants.CharacterLimit)
    {
        // Reduce results and retry
        var reducedLimit = limit / 2;
        results = PerformSearch(query, reducedLimit);
        
        var response = new
        {
            truncated = true,
            truncation_message = $"Response truncated from {limit} to {reducedLimit} items. " +
                "Use 'offset' parameter or add filters to see more results.",
            results = results
        };
        
        return JsonSerializer.Serialize(response);
    }
    
    return json;
}
```

---

## Error Handling

### Actionable Error Messages

Error messages should guide users toward solutions:

```csharp
[McpServerTool]
public static string GetResource(string resourceId)
{
    if (string.IsNullOrWhiteSpace(resourceId))
    {
        return "Error: Resource ID is required. Use 'list_resources' to see available IDs.";
    }
    
    try
    {
        var resource = FetchResource(resourceId);
        if (resource == null)
        {
            return $"Error: Resource '{resourceId}' not found. " +
                "Verify the ID is correct or use 'search_resources' to find resources by name.";
        }
        return JsonSerializer.Serialize(resource);
    }
    catch (UnauthorizedAccessException)
    {
        return "Error: Permission denied. Ensure your API key has read access to this resource.";
    }
    catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
    {
        return "Error: Rate limit exceeded. Wait 60 seconds before retrying.";
    }
    catch (Exception ex)
    {
        return $"Error: Unexpected error - {ex.Message}. Please try again or contact support.";
    }
}
```

### Error Response Pattern

For structured error responses:

```csharp
public record ErrorResponse(
    bool IsError,
    string ErrorCode,
    string Message,
    string? Suggestion = null
);

public static string CreateError(string code, string message, string? suggestion = null)
{
    return JsonSerializer.Serialize(new ErrorResponse(true, code, message, suggestion));
}

// Usage
return CreateError(
    "NOT_FOUND",
    $"Issue #{issueNumber} not found",
    "Use 'github_list_issues' to see available issues"
);
```

---

## Security Best Practices

### Environment Variables

Never hardcode secrets:

```csharp
// Good - read from environment
var apiKey = Environment.GetEnvironmentVariable("API_KEY")
    ?? throw new InvalidOperationException("API_KEY environment variable is required");

// Bad - hardcoded
var apiKey = "sk-abc123..."; // NEVER DO THIS
```

### .NET User Secrets (Local Development)

Use the Secret Manager for local dev instead of putting secrets in source-controlled files:

```bash
# Initialize user secrets for the project
dotnet user-secrets init

# Set a secret
dotnet user-secrets set "Api:Key" "sk-your-dev-key"
dotnet user-secrets set "Api:BaseUrl" "https://api.example.com"
```

Access secrets via configuration (they're automatically loaded in Development):

```csharp
var builder = Host.CreateApplicationBuilder(args);

// User secrets are loaded automatically when DOTNET_ENVIRONMENT=Development
var apiKey = builder.Configuration["Api:Key"]
    ?? throw new InvalidOperationException("Api:Key is not configured");
```

### appsettings.Development.json

Use for **non-secret** development overrides only. Never put actual secrets here since this file is typically source-controlled:

```json
// appsettings.Development.json - OK for non-sensitive config
{
  "Api": {
    "BaseUrl": "https://api-staging.example.com",
    "TimeoutSeconds": 30
  },
  "Logging": {
    "LogLevel": {
      "Default": "Debug"
    }
  }
}
```

```csharp
// Program.cs - configuration is layered automatically
var builder = Host.CreateApplicationBuilder(args);

// Priority (highest wins): User Secrets > env vars > appsettings.{Environment}.json > appsettings.json
builder.Services.Configure<ApiSettings>(builder.Configuration.GetSection("Api"));
```

> **Rule of thumb**: Use `appsettings.Development.json` for URLs, timeouts, and feature flags. Use `dotnet user-secrets` or environment variables for API keys, tokens, and connection strings.

### Input Validation

Validate and sanitize all inputs:

```csharp
[McpServerTool]
public static string ReadFile(
    [Description("Path to the file")] string path)
{
    // Validate path to prevent directory traversal
    var fullPath = Path.GetFullPath(path);
    var allowedDirectory = Path.GetFullPath("./data");
    
    if (!fullPath.StartsWith(allowedDirectory))
    {
        return "Error: Access denied. File must be within the data directory.";
    }
    
    if (!File.Exists(fullPath))
    {
        return $"Error: File not found: {path}";
    }
    
    return File.ReadAllText(fullPath);
}
```

### URL Validation

```csharp
[McpServerTool]
public static async Task<string> FetchUrl(
    HttpClient httpClient,
    [Description("URL to fetch")] string url)
{
    if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
    {
        return "Error: Invalid URL format.";
    }
    
    if (uri.Scheme != "https" && uri.Scheme != "http")
    {
        return "Error: Only HTTP and HTTPS URLs are supported.";
    }
    
    // Prevent access to internal networks
    if (uri.Host == "localhost" || uri.Host.StartsWith("192.168.") || uri.Host.StartsWith("10."))
    {
        return "Error: Access to internal networks is not allowed.";
    }
    
    return await httpClient.GetStringAsync(uri);
}
```

---

## Logging Best Practices

### stdio Servers

**Critical**: All logging must go to stderr:

```csharp
var builder = Host.CreateApplicationBuilder(args);

builder.Logging.AddConsole(options =>
{
    // Log everything to stderr
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});
```

### Structured Logging

Use structured logging for better observability:

```csharp
[McpServerToolType]
public class ToolsWithLogging
{
    private readonly ILogger<ToolsWithLogging> _logger;
    
    public ToolsWithLogging(ILogger<ToolsWithLogging> logger)
    {
        _logger = logger;
    }
    
    [McpServerTool]
    public string ProcessRequest(string requestId)
    {
        _logger.LogInformation("Processing request {RequestId}", requestId);
        
        try
        {
            var result = DoWork(requestId);
            _logger.LogInformation("Request {RequestId} completed successfully", requestId);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Request {RequestId} failed", requestId);
            throw;
        }
    }
}
```

---

## Tool Annotations

Provide hints about tool behavior:

```csharp
// Read-only tool
[McpServerTool(
    Name = "github_list_issues",
    ReadOnlyHint = true,
    DestructiveHint = false,
    IdempotentHint = true,
    OpenWorldHint = true)]
public static string ListIssues(...) { }

// Destructive tool
[McpServerTool(
    Name = "github_delete_issue",
    ReadOnlyHint = false,
    DestructiveHint = true,
    IdempotentHint = true,
    OpenWorldHint = true)]
public static string DeleteIssue(...) { }
```

| Annotation | Default | Description |
|------------|---------|-------------|
| `ReadOnlyHint` | false | Tool does not modify state |
| `DestructiveHint` | true | Tool may perform destructive updates |
| `IdempotentHint` | false | Repeated calls have same effect |
| `OpenWorldHint` | true | Tool interacts with external services |

---

## Code Organization

### Recommended Project Structure

```
MyMcpServer/
├── MyMcpServer.csproj
├── Program.cs
├── Constants.cs
├── Tools/
│   ├── SearchTools.cs
│   ├── CrudTools.cs
│   └── UtilityTools.cs
├── Prompts/
│   └── CodePrompts.cs
├── Resources/
│   └── FileResources.cs
├── Services/
│   ├── ApiClient.cs
│   └── DataService.cs
├── Models/
│   ├── User.cs
│   └── Project.cs
└── server.json
```

### Shared Utilities

Extract common functionality:

```csharp
// Services/ApiClient.cs
public class ApiClient
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    
    public ApiClient(HttpClient httpClient, IConfiguration config)
    {
        _httpClient = httpClient;
        _baseUrl = config["Api:BaseUrl"] ?? "https://api.example.com";
    }
    
    public async Task<T?> GetAsync<T>(string endpoint, CancellationToken ct = default)
    {
        var response = await _httpClient.GetAsync($"{_baseUrl}/{endpoint}", ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(ct);
    }
}

// Program.cs
builder.Services.AddHttpClient<ApiClient>();
```

---

## Quality Checklist

### Naming
- [ ] Server follows `{Service}McpServer` pattern
- [ ] Tools use snake_case with service prefix
- [ ] Tool names are action-oriented and specific

### Descriptions
- [ ] All tools have `[Description]` attributes
- [ ] All parameters have `[Description]` attributes
- [ ] Descriptions explain what the tool does and how to use it

### Response Handling
- [ ] Support for both JSON and Markdown formats
- [ ] Pagination implemented for list operations
- [ ] Character limits prevent oversized responses

### Error Handling
- [ ] Error messages are actionable
- [ ] Errors suggest next steps
- [ ] Exceptions are caught and handled gracefully

### Security
- [ ] No hardcoded secrets
- [ ] Input validation on all parameters
- [ ] Path traversal prevention for file operations
- [ ] URL validation for network operations

### Logging
- [ ] stdio servers log to stderr only
- [ ] Structured logging with appropriate levels
- [ ] Sensitive data not logged
