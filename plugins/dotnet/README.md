# dotnet

Core .NET and C# skills for coding agents.

## Includes

- Common .NET development skills
- A C# language server integration for `.cs` files

## LSP

This plugin declares a C# LSP server that is launched through the .NET CLI.
The LSP declaration is available to hosts that support the plugin `lspServers` extension. Codex
plugin installs expose this plugin's skills but do not load that host-specific LSP declaration.

Prerequisites:
- .NET 10 SDK installed
- `dotnet` available on PATH

## Skills

- [csharp-refactoring](skills/csharp-refactoring/SKILL.md)
- [setup-local-sdk](skills/setup-local-sdk/SKILL.md)
