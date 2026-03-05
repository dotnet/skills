---
name: local-llm-inference
description: >
  USE FOR: Running LLMs locally in .NET without cloud API calls, privacy-sensitive or air-gapped
  scenarios, reducing inference costs, offline development, on-device inference with Ollama or
  Foundry Local.
  DO NOT USE FOR: Cloud-based LLM calls (use meai-chat-integration), ONNX model inference for
  non-LLM models (use onnx-runtime-inference), classical ML tasks (use mlnet).
---

# Local LLM Inference

Run large language models entirely on-device in .NET applications — no cloud dependencies, no API
keys, no network calls after model download. This skill covers two approaches that both wire through
Microsoft.Extensions.AI `IChatClient` for provider-agnostic application code.

## When to Use

- Running LLMs locally without cloud API calls
- Privacy-sensitive or air-gapped environments
- Reducing inference costs by avoiding per-token billing
- Offline development and testing with real models

## When Not to Use

- Cloud-based LLM calls → use **meai-chat-integration**
- ONNX model inference for non-LLM models → use **onnx-runtime-inference**
- ML.NET classical/traditional ML → use **mlnet**

## Decision Tree

| Criteria | Ollama | Foundry Local |
|---|---|---|
| Hosting | Separate server process | On-device service with OpenAI-compatible API |
| Model management | `ollama pull` | `foundry model run` with curated catalog |
| Hardware optimization | Manual (select model variant) | Automatic (selects optimal execution provider for CPU/GPU/NPU) |
| Model switching | Hot-switch via API | Load/unload via SDK or CLI |
| Platforms | Windows, macOS, Linux | Windows, macOS |

- **IF** using Ollama → follow the **Ollama** workflow below
- **IF** using Foundry Local → follow the **Foundry Local** workflow below

Both approaches produce an `IChatClient` — application code is identical regardless of provider.

## Workflow — Ollama

### Step 1: Install Ollama and Pull a Model

Install Ollama from https://ollama.ai, then pull a model:

```bash
ollama pull phi3:mini
```

### Step 2: Install OllamaSharp NuGet Package

```
dotnet add package OllamaSharp
```

### Step 3: Register as IChatClient

```csharp
builder.Services.AddChatClient(
    new OllamaApiClient("http://localhost:11434", "phi3:mini")
        .AsIChatClient());
```

### Step 4: Use Through IChatClient

The `IChatClient` API is identical to cloud providers — swap the backing implementation without
changing application code.

## Workflow — Foundry Local

### Step 1: Install Foundry Local CLI

```bash
# Windows
winget install Microsoft.FoundryLocal

# macOS
brew tap microsoft/foundrylocal
brew install foundrylocal
```

Verify: `foundry --version`

### Step 2: Install NuGet Packages

```
dotnet add package Microsoft.AI.Foundry.Local
dotnet add package OpenAI
```

### Step 3: Initialize Foundry Local and Load a Model

```csharp
using Microsoft.AI.Foundry.Local;
using OpenAI;

await FoundryLocalManager.CreateAsync(config, logger);
var mgr = FoundryLocalManager.Instance;
var catalog = await mgr.GetCatalogAsync();
var model = await catalog.GetModelAsync("phi-4-mini")
    ?? throw new Exception("Model not found");
await model.DownloadAsync();
await model.LoadAsync();
await mgr.StartWebServiceAsync();
```

> Browse available models: `foundry model list`

### Step 4: Wire to IChatClient via OpenAI SDK

Foundry Local exposes an OpenAI-compatible endpoint. Use the `OpenAI` NuGet package and wrap as `IChatClient`:

```csharp
var client = new OpenAIClient(
    new System.ClientModel.ApiKeyCredential("notneeded"),
    new OpenAIClientOptions { Endpoint = new Uri(endpoint + "/v1") });

builder.Services.AddChatClient(
    client.GetChatClient(model.Id).AsIChatClient());
```

### Step 5: Use Through IChatClient

Application code is identical to the Ollama path and cloud providers.

## Validation

- [ ] Model loads without errors
- [ ] Model generates coherent text responses
- [ ] No outbound network calls are made during inference (after model download)
- [ ] Response quality is acceptable for the target use case
- [ ] Memory usage stays within hardware limits

## Common Pitfalls

| Pitfall | Guidance |
|---|---|
| Model too large for available RAM | 7B Q4 needs ~4 GB, 13B needs ~8 GB — size accordingly |
| Expecting cloud-model quality from small local models | Local models are capable but not equivalent to GPT-4-class models for complex tasks |
| Using full-precision models | Always use quantized models — full precision needs ~4× more memory |
| Not checking hardware compatibility | Foundry Local auto-selects execution providers; for Ollama, verify GPU support manually |

## More Info

- [Foundry Local](https://learn.microsoft.com/azure/foundry-local/get-started)
- [Ollama](https://ollama.ai)
