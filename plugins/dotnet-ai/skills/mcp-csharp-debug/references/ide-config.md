# IDE Configuration

Complete configuration for debugging C# MCP servers in VS Code and Visual Studio.

## VS Code Configuration

### mcp.json (MCP Server Registration)

Create `.vscode/mcp.json` to register your server with VS Code and GitHub Copilot:

**stdio transport:**
```json
{
  "servers": {
    "MyMcpServer": {
      "type": "stdio",
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "MyMcpServer/MyMcpServer.csproj"
      ],
      "env": {
        "API_KEY": "${input:api_key}"
      }
    }
  },
  "inputs": [
    {
      "type": "promptString",
      "id": "api_key",
      "description": "API key for the service",
      "password": true
    }
  ]
}
```

**HTTP transport:**
```json
{
  "servers": {
    "MyMcpServer": {
      "type": "http",
      "url": "http://localhost:3001",
      "headers": {}
    }
  }
}
```

### launch.json (Debugger Configuration)

Create `.vscode/launch.json` for F5 debugging:

```json
{
  "version": "0.2.0",
  "configurations": [
    {
      "name": "Debug MCP Server",
      "type": "coreclr",
      "request": "launch",
      "program": "${workspaceFolder}/MyMcpServer/bin/Debug/net10.0/MyMcpServer.dll",
      "args": [],
      "cwd": "${workspaceFolder}/MyMcpServer",
      "console": "integratedTerminal",
      "stopAtEntry": false,
      "env": {
        "DOTNET_ENVIRONMENT": "Development",
        "API_KEY": "your-dev-api-key"
      }
    }
  ]
}
```

### Attach to Running Process

For debugging a server started by VS Code's MCP integration:

```json
{
  "name": "Attach to MCP Server",
  "type": "coreclr",
  "request": "attach",
  "processName": "MyMcpServer"
}
```

### Environment Variable Patterns

| Pattern | Usage |
|---------|-------|
| `"API_KEY": "literal-value"` | Direct value (dev only) |
| `"API_KEY": "${input:api_key}"` | Prompt user for value |
| `"API_KEY": "${env:API_KEY}"` | Read from system environment |

## Visual Studio Configuration

### 1. Register MCP Server

1. Open **GitHub Copilot Chat** (top right icon)
2. Click **Select Tools** (wrench icon)
3. Click **+** to add a custom MCP server
4. Configure:
   - **Destination**: Solution or Global
   - **Server ID**: Your server name
   - **Type**: stdio or HTTP
   - For stdio: **Command**: `dotnet run --project path/to/project.csproj`
   - For HTTP: **URL**: `http://localhost:3001`

This creates a `.mcp.json` file in your solution root or global config.

### 2. Debug with F5

1. Right-click your MCP server project → **Set as Startup Project**
2. Open **Properties** → **Debug** → **General** → **Open debug launch profiles UI**
3. Add environment variables as needed
4. Set breakpoints in tool methods
5. Press **F5** to start debugging

### 3. Conditional Breakpoints

Right-click a breakpoint → **Conditions**:
- **Condition**: `query == "test"` — break only for specific input
- **Hit Count**: `>= 5` — break after N invocations
- **Filter**: `ProcessName == "MyMcpServer"` — filter by process

## Auto-Detect and Generate mcp.json

Script to auto-detect transport and generate config:

```powershell
$proj = (Get-ChildItem *.csproj | Select-Object -First 1)
$name = $proj.BaseName
$isHttp = (Get-Content $proj.FullName -Raw) -match 'ModelContextProtocol\.AspNetCore'
$configDir = if (Test-Path .vscode) { ".vscode" } else { "." }
$configPath = Join-Path $configDir "mcp.json"

if ($isHttp) {
    $port = "3001"
    $programCs = Get-Content "Program.cs" -Raw -ErrorAction SilentlyContinue
    if ($programCs -match 'localhost:(\d+)') { $port = $matches[1] }
    $config = @{ servers = @{ $name = @{ type = "http"; url = "http://localhost:$port" } } }
} else {
    $config = @{ servers = @{ $name = @{ type = "stdio"; command = "dotnet"; args = @("run", "--project", $proj.Name) } } }
}

New-Item -ItemType Directory -Path $configDir -Force | Out-Null
$config | ConvertTo-Json -Depth 5 | Out-File $configPath -Encoding utf8
Write-Host "Created $configPath for $( if ($isHttp) {'HTTP'} else {'stdio'} ) server"
```

## Logging Configuration

### HTTP Transport

Standard console logging for HTTP MCP servers:

```csharp
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(
    builder.Environment.IsDevelopment() ? LogLevel.Debug : LogLevel.Information);
```

> **Note:** For stdio transport, do NOT use standard console logging — it writes to stdout and corrupts the JSON-RPC protocol. See the main skill guide (Step 6) for stdio-specific logging configuration.
