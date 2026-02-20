# MSBuild Copilot Extension

A GitHub Copilot Extension that provides MSBuild and .NET build expertise across all Copilot Chat surfaces — GitHub.com, VS Code, and Visual Studio.

Users invoke it with `@msbuild` in any Copilot Chat:

```
@msbuild help me fix error CS0246
@msbuild why is my build slow?
@msbuild review my csproj for anti-patterns
@msbuild modernize this legacy project
```

## Architecture

This is a **serverless MVP** (Option A from the [design doc](../../../docs/copilot-extension-design.md)):

- **Runtime**: Node.js (deployable to Azure Functions, AWS Lambda, Vercel, or any serverless platform)
- **Knowledge**: MSBuild skill content compiled into system prompts
- **No binlog analysis**: Knowledge-based assistance only (binlog support planned for v2)

```
User in Copilot Chat (@msbuild ...)
        │
        ▼
GitHub Copilot Platform
        │
        ▼ (routes @msbuild mentions)
This Extension (serverless function)
        │
        ├── Domain check (is this MSBuild-related?)
        ├── Intent classification (error? perf? style? modernization?)
        ├── Knowledge lookup (compiled skill content)
        └── Response generation (via Copilot API)
```

## Project Structure

```
copilot-extension/
├── README.md               # This file
├── package.json             # Node.js project
├── src/
│   ├── index.js             # Entry point — handles Copilot webhook
│   ├── domain-check.js      # MSBuild domain relevance detection
│   ├── intent-classifier.js # Routes to the right knowledge area
│   └── knowledge/           # Compiled skill content (generated, gitignored)
│       ├── build-errors.md      # From common-build-errors + sourcegen-analyzer-failures
│       ├── performance.md       # From build-perf-baseline + build-perf-diagnostics
│       ├── style-guide.md       # From msbuild-style-guide + msbuild-antipatterns
│       └── modernization.md     # From msbuild-modernization + directory-build-organization
└── app.yml                  # GitHub App manifest for registration
```

## Setup

### 1. Install Dependencies

```bash
cd copilot-extension
npm install
```

### 2. Compile Knowledge

Extract and compile skill content into optimized knowledge files:

```bash
# From repo root
node src/dotnet-msbuild/build.js

# Or from copilot-extension/
npm run compile-knowledge
```

### 3. Register as a GitHub App

Use the `app.yml` manifest to create a GitHub App with Copilot Extension capabilities:

1. Go to **GitHub Settings → Developer settings → GitHub Apps → New GitHub App**
2. Use the values from `app.yml` as guidance
3. Enable **Copilot Extension** capabilities
4. Set the webhook URL to your deployed function URL
5. Install the app on your organization

### 4. Deploy

Deploy the serverless function to your platform of choice:

```bash
# Local development
npm start

# Azure Functions
func azure functionapp publish <app-name>

# Vercel
vercel deploy

# AWS Lambda (via Serverless Framework, SAM, etc.)
```

### 5. Set Environment Variables

```bash
GITHUB_APP_ID=<your-app-id>
GITHUB_APP_PRIVATE_KEY=<base64-encoded-private-key>
GITHUB_WEBHOOK_SECRET=<your-webhook-secret>
```

## Development

```bash
# Run locally
npm start

# Run tests
npm test

# Recompile knowledge after skill changes
node src/dotnet-msbuild/build.js
```

## How It Works

1. User types `@msbuild <question>` in Copilot Chat
2. GitHub routes the message to this extension's webhook
3. The extension:
   - Verifies the webhook signature
   - Checks domain relevance (is this actually MSBuild-related?)
   - Classifies intent (build error? performance? style? modernization?)
   - Loads the relevant knowledge context
   - Returns the knowledge as system context for Copilot to generate a response

## Extending

- **Add knowledge**: Update skills in `src/dotnet-msbuild/skills/`, then run `node src/dotnet-msbuild/build.js`
- **Add intents**: Edit `src/intent-classifier.js` to recognize new categories
- **Add tools**: For v2, add MCP server integration for binlog analysis

## Related

- [Design Document](../../../docs/copilot-extension-design.md) — Full design rationale
- [Skills Repository](../) — Source knowledge base
- [GitHub Copilot Extensions Docs](https://docs.github.com/copilot/building-copilot-extensions)
