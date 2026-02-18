---
name: mcp-csharp-debug
description: Guide for running and debugging MCP servers built with the C# SDK. Covers local execution, VS/VS Code debugging, MCP Inspector integration, and GitHub Copilot Agent Mode testing.
license: Complete terms in LICENSE.txt
---

# MCP C# Server Debugging Guide

## Overview

Run, debug, and test your C# MCP server locally. This skill covers IDE configuration, breakpoint debugging, logging setup, and integration testing with MCP Inspector and GitHub Copilot.

---

# Process

## 🚀 Running Your MCP Server

### Local Execution

**stdio transport:**
```bash
cd MyMcpServer
dotnet run
```

**HTTP transport:**
```bash
cd MyMcpServer
dotnet run
# Server runs on http://localhost:3001 by default
```

### Build and Run

```bash
# Build first
dotnet build

# Run the built executable
dotnet run --no-build
```

---

## ⚡ Auto-Generate mcp.json

If your project doesn't have an `mcp.json` configuration file, generate one automatically based on your project:

### Auto-Generation Process

**Step 1: Detect Project Type**

Check the `.csproj` file to determine transport type:

```powershell
# Check for HTTP transport (ASP.NET Core package reference)
$csproj = Get-Content *.csproj -Raw
if ($csproj -match 'ModelContextProtocol\.AspNetCore') {
    $transport = "http"
} else {
    $transport = "stdio"
}
```

**Step 2: Detect IDE Context**

Determine where to place the config file:

```powershell
# Detect VS Code
$isVSCode = (Test-Path ".vscode") -or ($env:TERM_PROGRAM -eq "vscode") -or ($env:VSCODE_CLI)

if ($isVSCode) {
    $configPath = ".vscode/mcp.json"
    New-Item -ItemType Directory -Path ".vscode" -Force | Out-Null
} elseif (Test-Path "*.sln") {
    $configPath = ".mcp.json"  # Solution root for Visual Studio
} else {
    $configPath = ".mcp.json"  # Project root for CLI/other
}
```

**Step 3: Generate Configuration**

For **stdio** transport:
```powershell
$projectFile = (Get-ChildItem *.csproj | Select-Object -First 1).Name
$projectName = [System.IO.Path]::GetFileNameWithoutExtension($projectFile)

$config = @"
{
  "servers": {
    "$projectName": {
      "type": "stdio",
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "$projectFile"
      ]
    }
  }
}
"@

$config | Out-File -FilePath $configPath -Encoding utf8
Write-Host "Created $configPath for stdio server"
```

For **HTTP** transport:
```powershell
$projectFile = (Get-ChildItem *.csproj | Select-Object -First 1).Name
$projectName = [System.IO.Path]::GetFileNameWithoutExtension($projectFile)

# Try to detect port from Program.cs or use default
$port = "3001"
$programCs = Get-Content "Program.cs" -Raw -ErrorAction SilentlyContinue
if ($programCs -match 'localhost:(\d+)') {
    $port = $matches[1]
}

$config = @"
{
  "servers": {
    "$projectName": {
      "type": "http",
      "url": "http://localhost:$port"
    }
  }
}
"@

$config | Out-File -FilePath $configPath -Encoding utf8
Write-Host "Created $configPath for HTTP server on port $port"
```

### Quick One-Liner

Generate mcp.json with sensible defaults:

```powershell
# Auto-detect and generate mcp.json
$proj = (Get-ChildItem *.csproj)[0].Name; $name = $proj -replace '\.csproj$',''; $isHttp = (Get-Content $proj -Raw) -match 'AspNetCore'; $path = if(Test-Path .vscode){".vscode/mcp.json"}else{".mcp.json"}; if($isHttp){'{"servers":{"'+$name+'":{"type":"http","url":"http://localhost:3001"}}}'}else{'{"servers":{"'+$name+'":{"type":"stdio","command":"dotnet","args":["run","--project","'+$proj+'"]}}}'}|Out-File $path -Encoding utf8; "Created $path"
```

### Verify Configuration

After generating, verify the file was created correctly:

```powershell
# Display the generated config
Get-Content $configPath | ConvertFrom-Json | ConvertTo-Json -Depth 10
```

---

## 🔧 IDE Configuration

### Visual Studio Code

#### 1. Create MCP Configuration

Create `.vscode/mcp.json` in your workspace:

**For stdio transport:**
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

**For HTTP transport:**
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

#### 2. Create Launch Configuration

Create `.vscode/launch.json`:

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

### Visual Studio

#### 1. Configure MCP Server

1. Open GitHub Copilot Chat (top right icon)
2. Click the **Select Tools** wrench icon
3. Click the **+** icon to add a custom MCP server
4. Fill in the configuration:
   - **Destination**: Solution or Global
   - **Server ID**: Your server name
   - **Type**: stdio or HTTP
   - **Command** (stdio): `dotnet run --project path/to/project.csproj`
   - **URL** (HTTP): `http://localhost:3001`

This creates a `.mcp.json` file in your solution or global config.

#### 2. Debug Configuration

1. Right-click your project → **Properties**
2. Go to **Debug** → **General** → **Open debug launch profiles UI**
3. Configure environment variables as needed
4. Set breakpoints and press **F5** to debug

---

## 📋 Testing with GitHub Copilot

### Agent Mode Testing

1. **Open GitHub Copilot Chat**
   - VS Code: Click Copilot icon or use `Ctrl+Shift+I`
   - Visual Studio: Click Copilot icon in top right

2. **Switch to Agent Mode**
   - Look for the mode selector and choose "Agent"

3. **Verify Tools are Available**
   - Click the **Select Tools** icon
   - Confirm your MCP server and its tools are listed

4. **Test with a Prompt**
   ```
   Give me a random number between 1 and 100.
   ```

5. **Approve Tool Execution**
   - Copilot will ask for permission to run the tool
   - Select **Continue** or configure auto-approval

### Troubleshooting Agent Mode

If your tool isn't being used:

1. **Verify tool appears in the tools list**
2. **Reference the tool explicitly in your prompt:**
   ```
   Using #get_random_number, give me a random number between 1 and 100.
   ```
3. **Check MCP server is running:**
   - Look for the "Start" button above your MCP server in settings
   - If not started, click to start it

---

## 🔍 MCP Inspector

The MCP Inspector is a debugging tool for testing MCP servers:

### Installation and Usage

```bash
# Run MCP Inspector
npx @modelcontextprotocol/inspector
```

### Connecting to Your Server

**stdio server:**
```bash
npx @modelcontextprotocol/inspector dotnet run --project MyMcpServer/MyMcpServer.csproj
```

**HTTP server:**
1. Start your server: `dotnet run`
2. Open Inspector and connect to `http://localhost:3001`

### Inspector Features

- **List Tools**: View all registered tools and their schemas
- **Call Tools**: Test tool execution with custom parameters
- **View Logs**: See request/response details
- **Debug Protocol**: Inspect raw MCP messages

---

## 📝 Logging Configuration

### stdio Transport Logging

**Critical**: Log to stderr only (stdout is for MCP protocol):

```csharp
var builder = Host.CreateApplicationBuilder(args);

builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

// Optional: Add file logging
builder.Logging.AddFile("logs/mcp-server-{Date}.log");
```

### HTTP Transport Logging

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

// Set log level
builder.Logging.SetMinimumLevel(
    builder.Environment.IsDevelopment() 
        ? LogLevel.Debug 
        : LogLevel.Information);
```

### Logging in Tools

```csharp
[McpServerToolType]
public class MyTools
{
    private readonly ILogger<MyTools> _logger;

    public MyTools(ILogger<MyTools> logger)
    {
        _logger = logger;
    }

    [McpServerTool, Description("Processes data")]
    public string ProcessData(string input)
    {
        _logger.LogDebug("Processing input: {Input}", input);
        
        try
        {
            var result = DoProcessing(input);
            _logger.LogInformation("Processing completed successfully");
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Processing failed for input: {Input}", input);
            throw;
        }
    }
}
```

---

## 🐛 Breakpoint Debugging

### VS Code

1. Open your tool file
2. Click in the gutter to set a breakpoint (red dot)
3. Press **F5** to start debugging
4. Trigger the tool (via Copilot, Inspector, or test client)
5. Execution pauses at the breakpoint

### Visual Studio

1. Set breakpoints by clicking in the left margin
2. Press **F5** to start debugging
3. Use the **Debug** menu for step controls:
   - **F10**: Step Over
   - **F11**: Step Into
   - **Shift+F11**: Step Out

### Conditional Breakpoints

Right-click a breakpoint to add conditions:

```csharp
// Break only when query is "test"
[McpServerTool]
public string Search(string query)  // Set conditional breakpoint: query == "test"
{
    // ...
}
```

---

## 🔧 Common Issues and Solutions

### Issue: "Command not found" or server won't start

**Solution**: Ensure .NET 10+ SDK is installed:
```bash
dotnet --version
# Should show 10.0.x or higher
```

### Issue: Tool not appearing in Copilot

**Solutions**:
1. Verify the tool has `[McpServerTool]` attribute
2. Check the class has `[McpServerToolType]` attribute
3. Ensure `.WithToolsFromAssembly()` is called in Program.cs
4. Rebuild the project: `dotnet build`

### Issue: stdio server outputs garbage

**Cause**: Logging to stdout instead of stderr

**Solution**:
```csharp
builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});
```

### Issue: HTTP server returns 404

**Solutions**:
1. Ensure `app.MapMcp()` is called
2. Check the URL path (usually just `/`)
3. Verify the server is running on the correct port

### Issue: Environment variables not working

**Solutions**:
1. Check `.mcp.json` has the `env` section
2. Verify variable names match exactly
3. For VS Code, use `${input:variable_id}` syntax for secrets

### Issue: Breakpoints not hit

**Solutions**:
1. Ensure building in Debug configuration: `dotnet build -c Debug`
2. Verify source maps are enabled (default in Debug)
3. Check you're debugging the correct process

---

**Load [📋 Debugging Guide](./references/debugging_guide.md) for detailed troubleshooting steps.**

---

## Related Skills

- **mcp-csharp-create** - Creating your MCP server
- **mcp-csharp-test** - Testing and evaluation
- **mcp-csharp-publish** - Publishing and deployment

---

# Reference Files

## 📚 Documentation Library

- [📋 Debugging Guide](./references/debugging_guide.md) - Detailed troubleshooting and advanced debugging
- **MCP Inspector**: `npx @modelcontextprotocol/inspector`
- **VS Code Debugging**: https://code.visualstudio.com/docs/csharp/debugging
