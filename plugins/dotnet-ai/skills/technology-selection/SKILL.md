---
name: technology-selection
description: "Guides technology selection and implementation of AI and ML features in .NET 8+ applications using ML.NET, Microsoft.Extensions.AI (MEAI), Microsoft Agent Framework (MAF), GitHub Copilot SDK, ONNX Runtime, and OllamaSharp. Covers the full spectrum from classic ML through modern LLM orchestration to local inference. Use when adding classification, regression, clustering, anomaly detection, recommendation, LLM integration (text generation, summarization, reasoning), RAG pipelines with vector search, agentic workflows with tool calling, Copilot extensions, or custom model inference via ONNX Runtime to a .NET project. DO NOT USE FOR projects targeting .NET Framework (requires .NET 8+), the task is pure data engineering or ETL with no ML/AI component, or the project needs a custom deep learning training loop (use Python with PyTorch/TensorFlow, then export to ONNX for .NET inference)."
license: MIT
---

# .NET AI and Machine Learning

Choose the right technology for an AI/ML task, then implement it with the
standard guardrails. Always state which decision-tree branch applies and why.
For a vague request, present a plan/architecture before writing code.

## Pick the technology

| Task | Technology |
|------|-----------|
| Structured/tabular: classification, regression, clustering, anomaly detection, recommendation | **ML.NET** (`Microsoft.ML`) — reproducible, no cloud, purpose-built |
| Single prompt → response over unstructured text (no tools) | **LLM via `Microsoft.Extensions.AI`** (`IChatClient`) |
| Tool/function calling, multi-step reasoning, agent loops, multi-agent | **Microsoft Agent Framework** (`Microsoft.Agents.AI`, prerelease), built on MEAI |
| GitHub Copilot extensions / dev-workflow tools | **GitHub Copilot SDK** (`GitHub.Copilot.SDK`) |
| Run a pre-trained/fine-tuned custom model | **ONNX Runtime** (`Microsoft.ML.OnnxRuntime`) |
| Local/offline LLM inference | **OllamaSharp** |
| Semantic search / RAG / embeddings | **`Microsoft.Extensions.VectorData.Abstractions`** + a provider connector (Azure AI Search, pgvector, Qdrant, Redis, etc.) |
| Ingest/chunk/load documents into a vector store | **`Microsoft.Extensions.AI.DataIngestion`** (preview) + MEVD |
| Both structured predictions AND reasoning | **Hybrid**: ML.NET scoring + LLM explanation, loosely coupled |

Do not use an LLM for tasks ML.NET handles (tabular classification, regression,
clustering) — LLMs are slower, costlier, and non-deterministic there.

## Layer the libraries (never skip a layer)

1. **Abstraction — `Microsoft.Extensions.AI` (MEAI):** always the foundation.
   Use `IChatClient`/`IEmbeddingGenerator` directly only for simple
   prompt-in/response-out with no tools.
2. **Provider SDK — `OpenAI` / `Azure.AI.OpenAI` / `Azure.AI.Inference` / `OllamaSharp`:**
   the concrete implementation, wired into MEAI via `AddChatClient`. Do not call
   it directly from business logic.
3. **Orchestration — `Microsoft.Agents.AI`:** required whenever the task involves
   tools, agent loops, multi-step/multi-agent reasoning, or durable workflows. Do
   not hand-roll tool-dispatch loops on `IChatClient`. Install with
   `dotnet add package Microsoft.Agents.AI --prerelease`.
4. **Copilot — `GitHub.Copilot.SDK`:** only for Copilot-platform extensions.

Never mix a raw `HttpClient` provider call with MEAI/Agent Framework in the same
workflow. Do not use `Microsoft.SemanticKernel` or `Accord.NET` for new work
(superseded / archived). Register every AI/ML client through DI (never
instantiate in business logic); use `IOptions<T>` for configuration.

## Guardrails by branch

**ML.NET** — set a seed (`new MLContext(seed: 42)`); split with `TrainTestSplit`
and evaluate on held-out data; log task-appropriate metrics (e.g. MicroAccuracy,
MacroAccuracy, LogLoss); prefer AutoML (`mlContext.Auto()`) first; in ASP.NET Core
serve via `PredictionEnginePool<TIn, TOut>` (never a singleton `PredictionEngine`
— not thread-safe).

**LLM integration** — depend on `IChatClient`, registered via `AddChatClient`;
set `Temperature` and `MaxOutputTokens` explicitly in `ChatOptions`; add retry
with backoff (`RetryingChatClient` or Polly); load API keys from configuration /
user-secrets / Key Vault (never hardcoded); pin a dated model version
(e.g. `gpt-4o-2024-08-06`).

**Agentic** — use `Microsoft.Agents.AI` on top of `Microsoft.Extensions.AI`; cap
loops with `MaximumIterations`; enforce a token/cost ceiling; define explicit
tool/function schemas with descriptions; log agent steps for observability (never
log raw message content — PII/secrets); prefer single-agent-with-tools over
multi-agent.

**RAG** — cache embeddings (don't re-embed per query); use semantic (not
fixed-size) chunking; set a minimum similarity score to drop low-relevance chunks;
include source attribution; use `IEmbeddingGenerator` for embeddings and
`Microsoft.Extensions.VectorData.Abstractions` + provider connector (e.g. pgvector
for PostgreSQL) for storage/query.

**Non-determinism** — tell the developer LLM output varies even at temperature 0;
validate output against a schema with a fallback path; pin model versions;
recommend a golden-dataset eval harness for prompts that will be iterated.

## Verify

`dotnet build -c Release -warnaserror` clean; run tests; for ML.NET confirm
metrics meet the bar and the model round-trips; for LLM confirm structured-output
parsing handles malformed responses; for RAG confirm retrieval filters
irrelevant chunks.