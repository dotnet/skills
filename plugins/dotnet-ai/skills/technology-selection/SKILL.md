---
name: technology-selection
description: >
  META/ROUTER skill — routes developers to the correct .NET AI/ML skill
  based on their task type. Use this when you are unsure which .NET AI
  technology or library to apply. Do NOT use this for projects targeting
  .NET Framework (requires .NET 8+), pure data engineering/ETL with no
  ML/AI component, or custom deep learning training loops beyond what
  ML.NET and TorchSharp provide.
---

# Technology Selection

Route to the right .NET AI/ML skill. Answer the questions below; follow the first matching rule.

## Inputs

| Input | Required | Description |
|---|---|---|
| `task_description` | yes | What the developer wants to build or solve |
| `data_type` | no | structured/tabular, text, image, audio, mixed |
| `deployment_target` | no | cloud, edge, local/offline |
| `target_framework` | no | .NET version (must be .NET 8+) |

## Library Stack (reference only)

- **Abstraction** — Microsoft.Extensions.AI (MEAI): always the foundation for LLM work.
- **Provider SDK** — OpenAI, Azure.AI.OpenAI, OllamaSharp, GitHub.Copilot.SDK, etc.
- **Runtime** — Microsoft Agent Framework (Microsoft.Agents.AI, prerelease): agent lifecycle, sessions, multi-agent.
- **Harness** — GitHub Copilot SDK: batteries-included agent runtime with session persistence, MCP, safe outputs.

## Decision Tree

### 1 — Structured Data / Classical ML?

IF the task involves classification, regression, clustering, anomaly detection,
recommendation, or time-series forecasting on structured/tabular data
THEN invoke `/mlnet`.

> ⚠️ Do NOT use an LLM for tasks ML.NET handles well (classification on structured
> data, regression, clustering). LLMs are slower and more expensive for these tasks.

### 1b — Image Classification, Object Detection, NER, QA, or Text Classification?

IF the task involves image classification, object detection, named entity recognition,
question answering, text classification, or sentence similarity AND the goal is to
train or fine-tune a model in .NET (not just call an LLM API)
THEN invoke `/mlnet` (deep learning tasks via TorchSharp).

> Use ML.NET when you need a trained model you own and deploy — not a hosted LLM API call.

### 2 — Natural Language Generation / Chat / Reasoning?

IF the task involves text generation, summarization, reasoning, or chat
THEN invoke `/meai-chat-integration`.

### 3 — Text Embeddings?

IF the task involves semantic similarity or producing vector representations of text
THEN invoke `/meai-embeddings`.

### 4 — Vector Storage & Search?

IF the task involves storing, indexing, or querying vector embeddings
THEN invoke `/vector-data-search`.

### 5 — Document Ingestion for RAG?

IF the task involves document loading, chunking, or enrichment to feed a retrieval pipeline
THEN invoke `/data-ingestion-pipeline`.

### 6 — End-to-End RAG Pipeline?

IF the task requires retrieval-augmented generation combining ingestion, embeddings,
vector search, and chat completion
THEN invoke `/rag-pipeline` (composes `/meai-chat-integration` + `/meai-embeddings`
+ `/vector-data-search` + `/data-ingestion-pipeline`).

### 7 — Multi-Step Agent Workflows?

IF the task involves autonomous multi-step reasoning, tool calling, agent loops,
multi-agent orchestration, or durable agent sessions
THEN invoke `/agentic-workflow` (Microsoft Agent Framework — AIAgent, AgentSession, AsAIFunction).

For production scenarios needing persistence, horizontal scaling, or human-in-the-loop,
`/agentic-workflow` covers durable agents backed by external storage.

### 8 — Pre-Trained ONNX Model Inference?

IF the task involves running a pre-trained ONNX model for inference
THEN invoke `/onnx-runtime-inference`.

### 9 — Local / Offline LLM Inference?

IF the deployment target is local or offline and the task needs an LLM
THEN invoke `/local-llm-inference` (Ollama or Foundry Local).

### 10 — GitHub Copilot SDK?

IF the task involves any of these:
- Building a GitHub Copilot extension for IDE/CLI
- Using CopilotClient as a zero-config LLM backend (no API key management)
- Leveraging Copilot harness features (session persistence, MCP servers, safe outputs)
- Prototyping agents quickly with `gh` CLI auth
THEN invoke `/copilot-sdk-integration`.

> ⚠️ If the task requires Entra ID / managed identity auth, horizontal scaling,
> or custom agent loop control — use `/agentic-workflow` with Azure.AI.OpenAI instead.
> See the Bridge Pattern in `/copilot-sdk-integration` for the prototype-to-production path.

### 11 — Zero-Config Prototyping / No API Keys?

IF the developer wants to get started quickly without managing API keys
and has a GitHub Copilot subscription
THEN invoke `/copilot-sdk-integration` (Path A: CopilotClient as LLM Backend).

### 12 — Prototype-to-Production Bridge?

IF the developer wants to prototype with Copilot and later deploy to Azure
THEN invoke `/agentic-workflow` + `/copilot-sdk-integration`.
Use CopilotClient during development, swap to Azure.AI.OpenAI for production.
The AIAgent abstraction keeps agent code unchanged across backends.

### 13 — Hybrid: Structured ML + Natural Language?

IF the task requires BOTH structured ML predictions AND natural language capabilities
THEN invoke `/mlnet` + `/meai-chat-integration`.

### Anti-Patterns

- **Using `Microsoft.SemanticKernel` for new projects** → Use `Microsoft.Extensions.AI` + `Microsoft.Agents.AI` instead. Semantic Kernel is superseded by these newer abstractions for LLM orchestration and tool calling.
- **Using Copilot SDK for production enterprise apps requiring Entra ID** → Copilot SDK BYOK only supports static credentials. Use MAF + `Azure.AI.OpenAI` with `DefaultAzureCredential` for enterprise Azure deployments.
- **Assuming Copilot SDK scales horizontally** → Copilot session state is per-machine (local filesystem). For multi-instance deployments, use MAF durable agents with external storage.
- **Using an LLM for structured data tasks** → Use ML.NET. LLMs are slower and more expensive for classification, regression, and clustering on structured data.

## Cross-References

| Skill | Invoke |
|---|---|
| ML.NET (classical ML) | `/mlnet` |
| MEAI Chat Integration | `/meai-chat-integration` |
| MEAI Embeddings | `/meai-embeddings` |
| Vector Data Search | `/vector-data-search` |
| Data Ingestion Pipeline | `/data-ingestion-pipeline` |
| RAG Pipeline | `/rag-pipeline` |
| Agentic Workflow (MAF) | `/agentic-workflow` |
| ONNX Runtime Inference | `/onnx-runtime-inference` |
| Local LLM Inference | `/local-llm-inference` |
| Copilot SDK Integration | `/copilot-sdk-integration` |
