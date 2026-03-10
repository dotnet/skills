---
name: mcp-csharp-debug
description: >
  Run and debug C# MCP servers locally. Covers IDE configuration, MCP Inspector testing,
  GitHub Copilot Agent Mode integration, logging setup, and troubleshooting.
  USE FOR: running MCP servers locally with dotnet run, configuring VS Code or Visual Studio
  for MCP debugging, testing tools with MCP Inspector, testing with GitHub Copilot Agent Mode,
  diagnosing tool registration issues, setting up mcp.json configuration, debugging MCP
  protocol messages, configuring logging for stdio and HTTP servers.
  DO NOT USE FOR: creating new MCP servers (use mcp-csharp-create), writing automated tests
  (use mcp-csharp-test), publishing or deploying to production (use mcp-csharp-publish).
---

# C# MCP Server Debugging

Run, debug, and interactively test C# MCP servers. Covers local execution, IDE debugging with breakpoints, MCP Inspector for protocol-level testing, and GitHub Copilot Agent Mode integration.

## When to Use

- Running an MCP server locally for the first time
- Configuring VS Code or Visual Studio to debug an MCP server
- Testing tools interactively with MCP Inspector
- Verifying tools appear in GitHub Copilot Agent Mode
- Diagnosing issues: tools not discovered, protocol errors, server crashes
- Setting up `mcp.json` or `.mcp.json` configuration

## Stop Signals

- **No project yet?** → Use [mcp-csharp-create](../mcp-csharp-create/SKILL.md) first
- **Need automated tests?** → Use [mcp-csharp-test](../mcp-csharp-test/SKILL.md)
- **Production deployment issue?** → Use [mcp-csharp-publish](../mcp-csharp-publish/SKILL.md)

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Project path | Yes | Path to the `.csproj` file or project directory |
| Transport type | Recommended | `stdio` or `http` — detect from `.csproj` if not specified |
| IDE | Recommended | VS Code or Visual Studio — detect from environment if not specified |

**Agent behavior:** Detect transport type by checking the `.csproj` for a `PackageReference` to `ModelContextProtocol.AspNetCore`. If present → HTTP, otherwise → stdio.

## Workflow

### Step 1: Run the server locally

**stdio transport:**
```bash
cd <ProjectDir>
dotnet run
```
The process starts and waits for JSON-RPC messages on stdin. No output on stdout means it's working correctly.

**HTTP transport:**
```bash
cd <ProjectDir>
dotnet run
# Server listens on http://localhost:3001 (or configured port)
```

### Step 2: Generate MCP configuration

Detect the IDE and transport, then create the appropriate config file.

**For VS Code** — create `.vscode/mcp.json`:

stdio:
```json
{
  "servers": {
    "<ProjectName>": {
      "type": "stdio",
      "command": "dotnet",
      "args": ["run", "--project", "<path/to/ProjectFile.csproj>"]
    }
  }
}
```

HTTP:
```json
{
  "servers": {
    "<ProjectName>": {
      "type": "http",
      "url": "http://localhost:3001"
    }
  }
}
```

**For Visual Studio** — create `.mcp.json` at solution root (same JSON structure).

**For detailed IDE-specific configuration** (launch.json, environment variables, secrets), see [references/ide-config.md](references/ide-config.md).

### Step 3: Test with MCP Inspector

The MCP Inspector provides a UI for testing tools, viewing schemas, and inspecting protocol messages.

**stdio server:**
```bash
npx @modelcontextprotocol/inspector dotnet run --project <path/to/ProjectFile.csproj>
```

**HTTP server:**
1. Start your server: `dotnet run`
2. Run Inspector: `npx @modelcontextprotocol/inspector`
3. Connect to `http://localhost:3001`

**Inspector capabilities:**
- List all registered tools, prompts, and resources
- Call tools with custom parameters and see results
- View request/response JSON-RPC messages
- Inspect tool schemas and descriptions

**For detailed Inspector usage and troubleshooting**, see [references/mcp-inspector.md](references/mcp-inspector.md).

### Step 4: Test with GitHub Copilot Agent Mode

1. Open GitHub Copilot Chat → switch to **Agent** mode
2. Click **Select Tools** (wrench icon) → verify your server and tools are listed
3. Test with a prompt that should trigger your tool
4. Approve tool execution when prompted

**If tools don't appear — troubleshoot tool discovery:**

1. **Rebuild first** — stale builds are the #1 cause:
   ```bash
   dotnet build
   ```
   Then restart the MCP server (click Stop → Start in VS Code, or restart `dotnet run`).

2. **Check `[McpServerToolType]` on the class:**
   ```csharp
   [McpServerToolType]  // ← Required on the class
   public class MyTools { ... }
   ```

3. **Check `[McpServerTool]` on each tool method:**
   - The method must be `public`.
   - It can be `static` or instance. For instance methods, ensure the containing type is discoverable/registered (for example via `WithTools<MyTools>()` or `WithToolsFromAssembly()`) so DI can construct it.
   ```csharp
   [McpServerTool, Description("Does something")]
   public string DoSomething(string input) => input;
   ```

4. **Verify tool registration in Program.cs** — use one of:
   ```csharp
   .WithTools<MyTools>()           // register specific class
   .WithToolsFromAssembly()        // scan entire assembly for [McpServerToolType]
   ```

5. **Check `mcp.json`** points to the correct project path

6. If still not appearing, reference the tool explicitly: `Using #tool_name, do X`

### Step 5: Set up breakpoint debugging

1. Set breakpoints in your tool methods
2. Launch with the debugger:
   - **VS Code:** F5 (requires `launch.json` — see [references/ide-config.md](references/ide-config.md))
   - **Visual Studio:** F5 or right-click project → Debug → Start
3. Trigger the tool (via Inspector, Copilot, or test client)
4. Execution pauses at breakpoints

**Critical:** Build in Debug configuration. Breakpoints won't hit in Release builds.

### Step 6: Configure logging

**Critical for stdio transport:** Any output to stdout (including `Console.WriteLine`) **corrupts the MCP JSON-RPC protocol** and causes garbled responses or crashes. All logging and diagnostic output must go to stderr.

**stdio transport** — log to stderr only:
```csharp
builder.Logging.AddConsole(options =>
    options.LogToStandardErrorThreshold = LogLevel.Trace);
```

**HTTP transport** — standard console logging:
```csharp
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(
    builder.Environment.IsDevelopment() ? LogLevel.Debug : LogLevel.Information);
```

**In tool methods** — inject `ILogger<T>` via constructor:
```csharp
[McpServerToolType]
public class MyTools(ILogger<MyTools> logger)
{
    [McpServerTool, Description("Processes data")]
    public string ProcessData(string input)
    {
        logger.LogDebug("Processing: {Input}", input);
        return DoProcessing(input);
    }
}
```

## Validation

- [ ] Server starts without errors via `dotnet run`
- [ ] MCP Inspector connects and lists all expected tools
- [ ] Tool calls via Inspector return expected results
- [ ] Breakpoints hit when debugging in IDE
- [ ] Tools appear in GitHub Copilot Agent Mode tool list
- [ ] stdio: no logging output on stdout (stderr only)

## Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| Tools not appearing in Copilot or Inspector | **Rebuild first:** `dotnet build`, then restart the server. If still missing, verify `[McpServerToolType]` on class, `[McpServerTool]` on methods, and `WithTools<T>()` or `WithToolsFromAssembly()` in Program.cs |
| stdio server produces garbled output | `Console.WriteLine()` or logging is writing to stdout. All output **must** go to stderr. Set `LogToStandardErrorThreshold = LogLevel.Trace` on the console logger |
| "Command not found" when starting server | .NET 10+ SDK not installed. Check with `dotnet --version` |
| HTTP server returns 404 at MCP endpoint | Missing `app.MapMcp()` in Program.cs |
| Breakpoints not hit | Building in Release mode. Rebuild in Debug: `dotnet build -c Debug`, then restart |
| Environment variables not passed to server | Add `"env"` section to `mcp.json`. For secrets in VS Code, use `"${input:var_id}"` syntax |
| MCP Inspector can't connect to HTTP server | Server not running, or wrong port. Check `dotnet run` output for the listening URL |
| Stale tools after code changes | Always `dotnet build` and restart the server after changing tool methods or attributes |

## Related Skills

- [mcp-csharp-create](../mcp-csharp-create/SKILL.md) — Create a new MCP server project
- [mcp-csharp-test](../mcp-csharp-test/SKILL.md) — Automated tests and evaluations
- [mcp-csharp-publish](../mcp-csharp-publish/SKILL.md) — NuGet, Docker, Azure deployment

## Reference Files

- [references/mcp-inspector.md](references/mcp-inspector.md) — Detailed MCP Inspector usage: installation, connecting to servers, feature walkthrough, troubleshooting. **Load when:** user needs detailed Inspector guidance or is having connection issues.
- [references/ide-config.md](references/ide-config.md) — Complete VS Code and Visual Studio configuration: mcp.json templates, launch.json, environment variables, conditional breakpoints. **Load when:** setting up IDE debugging or configuring environment-specific settings.

## More Info

- [MCP Inspector](https://www.npmjs.com/package/@modelcontextprotocol/inspector) — Interactive debugging tool for MCP servers
- [VS Code MCP documentation](https://code.visualstudio.com/docs/copilot/chat/mcp-servers) — Configuring MCP servers in VS Code
