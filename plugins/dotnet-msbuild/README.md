# dotnet-msbuild

Skills for MSBuild failure diagnosis, performance optimization, code quality, and modernization.
The plugin also exposes the `binlog` MCP server to supported hosts.

Files under `agents/` are GitHub Copilot custom-agent definitions. Codex plugin installs expose the
skills and the Codex-compatible `binlog` MCP declaration, but do not install those `.agent.md` files
as native Codex agents.

## Binlog startup diagnostics

The [troubleshoot-binlog-mcp](skills/troubleshoot-binlog-mcp/SKILL.md) skill diagnoses
`CONNECTION_CLOSED`, missing binlog tools, and failures to start `Microsoft.AITools.BinlogMcp`.
Ask, for example, "The dotnet-msbuild binlog MCP won't connect; help me diagnose it."
It captures the launch error in the affected workspace, checks SDK and NuGet settings,
and suggests recovery without bypassing organizational feed policy.

This is an **on-demand skill**, not a session-start hook. It adds no executable hook code,
startup probe, or host-specific diagnostic payload. The MCP launch command is unchanged.
An agent can select the skill when the user reports a problem or a binlog workflow encounters
an unavailable server, provided the host has loaded the plugin's skills. A connection error
hidden from the agent cannot trigger the skill, so an automatic warning at session start is
not guaranteed.

The skill distinguishes package absence from feed/authentication problems, SDK mismatches,
and timeouts. Package search alone is not a startup test: `dnx` also applies source mapping
and can use cached tools or unlisted versions. Recovery is verified by reconnecting the
host and discovering the binlog MCP tools, not just by finding the package.

Focused evaluation scenarios live in `tests/dotnet-msbuild/troubleshoot-binlog-mcp/`.
