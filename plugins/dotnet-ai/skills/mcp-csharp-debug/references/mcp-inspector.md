# MCP Inspector

Interactive debugging tool for testing MCP servers. Provides a web UI for listing tools, calling them with custom parameters, and inspecting protocol messages.

## Installation

Requires Node.js (npm/npx). No global install needed:

```bash
npx @modelcontextprotocol/inspector
```

## Connecting to a Server

### stdio Server

Pass the server command directly:
```bash
npx @modelcontextprotocol/inspector dotnet run --project <path/to/Project.csproj>
```

The Inspector launches the server process and communicates via stdin/stdout.

### HTTP Server

1. Start the server separately:
   ```bash
   cd <ProjectDir>
   dotnet run
   ```

2. Launch Inspector and connect to the server URL:
   ```bash
   npx @modelcontextprotocol/inspector
   ```

3. In the Inspector UI, enter the server URL (e.g., `http://localhost:3001`)

### File-Based Server (.NET 10+ only)

For single-file servers using `#:package` directives:
```bash
npx @modelcontextprotocol/inspector ./Program.cs
```

## Features

### Tool Testing

1. Click **Tools** tab to see all registered tools
2. View each tool's JSON schema (parameters, types, descriptions)
3. Enter parameter values and click **Call** to execute
4. View the return value and any error details

### Prompt Testing

1. Click **Prompts** tab to see registered prompts
2. Fill in prompt arguments
3. View the generated messages

### Resource Browsing

1. Click **Resources** tab to list registered resources
2. Read resource contents directly in the UI

### Protocol Inspection

- View raw JSON-RPC request/response messages
- Inspect headers and metadata
- See timing information for each request

## Troubleshooting

| Problem | Checks |
|---------|--------|
| Inspector won't start | Verify Node.js is installed (`node --version`). Try clearing npx cache: `npx clear-npx-cache`. |
| Can't connect to stdio server | Verify server builds (`dotnet build`). Check no stdout writes (logging must go to stderr). Run `dotnet run` directly — hanging for input is correct. |
| Can't connect to HTTP server | Verify server is running and port is correct. Check firewall/proxy. Test with `curl http://localhost:<port>/`. |
| Tool call returns error | Check Inspector's protocol view for full error. Common issues: missing required parameters, serialization errors, unhandled exceptions. Add logging and check stderr (stdio) or console (HTTP). |
