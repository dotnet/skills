# Configuration Examples

Complete code examples for configuring .NET agent applications using the Options pattern.

## appsettings.json Structure

```json
{
  "AzureOpenAI": {
    "Endpoint": "https://your-resource.openai.azure.com/",
    "ApiKey": "your-api-key",
    "Deployment": "gpt-4"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.Extensions.AI": "Debug",
      "Microsoft.Extensions.Http": "Warning"
    }
  },
  "OpenTelemetry": {
    "Enabled": true,
    "ServiceName": "MyAgentService"
  }
}
```

## Strongly-Typed Options Class

```csharp
public class AzureOpenAIOptions
{
    public const string SectionName = "AzureOpenAI";
    
    public string Endpoint { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string Deployment { get; set; } = "gpt-4";
}

public class OpenTelemetryOptions
{
    public const string SectionName = "OpenTelemetry";
    
    public bool Enabled { get; set; }
    public string ServiceName { get; set; } = "AgentService";
}
```

## Registering and Using Options

```csharp
using Microsoft.Extensions.Options;

// Register the options
builder.Services.Configure<AzureOpenAIOptions>(
    builder.Configuration.GetSection(AzureOpenAIOptions.SectionName));

builder.Services.Configure<OpenTelemetryOptions>(
    builder.Configuration.GetSection(OpenTelemetryOptions.SectionName));

// Use the options in service registration
builder.Services.AddSingleton<IChatClient>(services =>
{
    var options = services.GetRequiredService<IOptions<AzureOpenAIOptions>>().Value;
    
    if (string.IsNullOrEmpty(options.Endpoint))
        throw new InvalidOperationException("Azure OpenAI endpoint is not configured");
    
    if (string.IsNullOrEmpty(options.ApiKey))
        throw new InvalidOperationException("Azure OpenAI API key is not configured");
    
    return new AzureAIInferenceChatClient(
        new Uri(options.Endpoint),
        new ApiKeyCredential(options.ApiKey),
        options.Deployment);
});
```

## Options Validation

Add validation to ensure configuration is correct at startup:

```csharp
using System.ComponentModel.DataAnnotations;

public class AzureOpenAIOptions
{
    public const string SectionName = "AzureOpenAI";
    
    [Required, Url]
    public string Endpoint { get; set; } = string.Empty;
    
    [Required, MinLength(20)]
    public string ApiKey { get; set; } = string.Empty;
    
    [Required]
    public string Deployment { get; set; } = "gpt-4";
}

// Enable validation
builder.Services.AddOptions<AzureOpenAIOptions>()
    .BindConfiguration(AzureOpenAIOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart(); // Fails fast at startup if invalid
```

## User Secrets for Development

Store sensitive configuration in user secrets during development:

```bash
# Initialize user secrets
dotnet user-secrets init

# Set individual secrets
dotnet user-secrets set "AzureOpenAI:ApiKey" "your-api-key-here"
dotnet user-secrets set "AzureOpenAI:Endpoint" "https://your-resource.openai.azure.com/"

# List all secrets
dotnet user-secrets list

# Remove a secret
dotnet user-secrets remove "AzureOpenAI:ApiKey"

# Clear all secrets
dotnet user-secrets clear
```

User secrets are stored in:
- **Windows**: `%APPDATA%\Microsoft\UserSecrets\<user_secrets_id>\secrets.json`
- **Linux/macOS**: `~/.microsoft/usersecrets/<user_secrets_id>/secrets.json`

## Environment Variables

Use environment variables for configuration in production:

```bash
# Linux/macOS
export AzureOpenAI__Endpoint="https://your-resource.openai.azure.com/"
export AzureOpenAI__ApiKey="your-api-key"
export AzureOpenAI__Deployment="gpt-4"

# Windows PowerShell
$env:AzureOpenAI__Endpoint="https://your-resource.openai.azure.com/"
$env:AzureOpenAI__ApiKey="your-api-key"
$env:AzureOpenAI__Deployment="gpt-4"

# Windows Command Prompt
set AzureOpenAI__Endpoint=https://your-resource.openai.azure.com/
set AzureOpenAI__ApiKey=your-api-key
set AzureOpenAI__Deployment=gpt-4
```

Note: Use double underscores (`__`) in environment variables to represent nested configuration sections.

## Azure Key Vault Integration

For production applications, use Azure Key Vault:

```bash
dotnet add package Azure.Extensions.AspNetCore.Configuration.Secrets
dotnet add package Azure.Identity
```

```csharp
using Azure.Identity;

var builder = WebApplication.CreateBuilder(args);

if (builder.Environment.IsProduction())
{
    var keyVaultEndpoint = builder.Configuration["KeyVault:Endpoint"];
    if (!string.IsNullOrEmpty(keyVaultEndpoint))
    {
        builder.Configuration.AddAzureKeyVault(
            new Uri(keyVaultEndpoint),
            new DefaultAzureCredential());
    }
}

// Rest of your configuration...
```

## Configuration Priority Order

.NET configuration sources are applied in order, with later sources overriding earlier ones:

1. `appsettings.json`
2. `appsettings.{Environment}.json`
3. User Secrets (in Development environment only)
4. Environment Variables
5. Command-line arguments

Example of layered configuration:

```csharp
var builder = WebApplication.CreateBuilder(args);

// Order matters - later sources override earlier ones
builder.Configuration
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true)
    .AddEnvironmentVariables()
    .AddCommandLine(args);

if (builder.Environment.IsDevelopment())
{
    builder.Configuration.AddUserSecrets<Program>();
}
```

## Multiple Provider Configurations

Configure multiple AI providers in a single application:

```json
{
  "AzureOpenAI": {
    "Endpoint": "https://your-resource.openai.azure.com/",
    "ApiKey": "your-azure-key",
    "Deployment": "gpt-4"
  },
  "OpenAI": {
    "ApiKey": "your-openai-key",
    "Model": "gpt-4"
  },
  "Ollama": {
    "Endpoint": "http://localhost:11434",
    "Model": "llama2"
  }
}
```

```csharp
builder.Services.Configure<AzureOpenAIOptions>(
    builder.Configuration.GetSection("AzureOpenAI"));
builder.Services.Configure<OpenAIOptions>(
    builder.Configuration.GetSection("OpenAI"));
builder.Services.Configure<OllamaOptions>(
    builder.Configuration.GetSection("Ollama"));

// Register keyed services based on each provider
builder.Services.AddKeyedSingleton<IChatClient>("azure", services => { /* ... */ });
builder.Services.AddKeyedSingleton<IChatClient>("openai", services => { /* ... */ });
builder.Services.AddKeyedSingleton<IChatClient>("ollama", services => { /* ... */ });
```
