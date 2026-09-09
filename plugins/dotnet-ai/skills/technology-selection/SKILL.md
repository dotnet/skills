---
name: technology-selection
description: "Guides technology selection and implementation of AI and ML features in .NET 8+ applications using ML.NET, Microsoft.Extensions.AI (MEAI), Microsoft Agent Framework (MAF), GitHub Copilot SDK, ONNX Runtime, and OllamaSharp. Covers the full spectrum from classic ML through modern LLM orchestration to local inference. Use when adding classification, regression, clustering, anomaly detection, recommendation, LLM integration (text generation, summarization, reasoning), RAG pipelines with vector search, agentic workflows with tool calling, Copilot extensions, or custom model inference via ONNX Runtime to a .NET project. DO NOT USE FOR projects targeting .NET Framework (requires .NET 8+), the task is pure data engineering or ETL with no ML/AI component, or the project needs a custom deep learning training loop (use Python with PyTorch/TensorFlow, then export to ONNX for .NET inference)."
license: MIT
---

# .NET AI and Machine Learning

Pick the right technology first, then deliver **only what the task asks for**. If the task asks for
a plan, comparison, or architecture (or says "do not write code"), produce that — do not scaffold,
build, or run code unprompted.

## Step 1: Classify the task (decision tree)

State which branch applies and why, then choose that technology.

| Task type | Technology | Why |
|-----------|-----------|-----|
| Structured/tabular: classification, regression, clustering, anomaly detection, recommendation | **ML.NET** (`Microsoft.ML`) | Deterministic (fixed seed), no cloud dependency, purpose-built |
| NL understanding, generation, summarization, reasoning (single prompt → response, no tools) | **LLM via Microsoft.Extensions.AI** (`IChatClient`) | Language capability, no orchestration needed |
| Agentic: multi-step tool/function calling, agent loops, multi-agent | **Microsoft Agent Framework** (`Microsoft.Agents.AI`) on **Microsoft.Extensions.AI** | Needs orchestration, tool dispatch, iteration control `IChatClient` lacks |
| GitHub Copilot extensions / custom dev-workflow agents | **GitHub Copilot SDK** (`GitHub.Copilot.SDK`) | Integrates with the Copilot agent runtime |
| Run a pre-trained/custom model in production | **ONNX Runtime** (`Microsoft.ML.OnnxRuntime`) | Hardware-accelerated, format-agnostic inference |
| Local/offline LLM inference | **OllamaSharp** ([Ollama models](https://ollama.com/search)) | Privacy-sensitive, air-gapped, cost-constrained |
| Semantic search, RAG, embedding storage | **Microsoft.Extensions.VectorData.Abstractions** (MEVD) + a provider (Azure AI Search, Milvus, MongoDB, pgvector, Pinecone, Qdrant, Redis, SQL) | Provider-agnostic vector search |
| Ingest, chunk, load documents into a vector store | **Microsoft.Extensions.AI.DataIngestion** (preview) + MEVD | Parses, chunks, embeds, upserts |
| Both structured predictions AND NL reasoning | **Hybrid**: ML.NET scoring + LLM reasoning layer | ML.NET is reproducible; LLM adds explanation |

**Critical rule:** Do NOT use an LLM for tasks ML.NET handles well (tabular classification,
regression, clustering) — LLMs are slower, costlier, and non-deterministic for these.

## Step 1b: Pick the library layer

| Layer | Library | Use when |
|-------|---------|----------|
| **Abstraction** | `Microsoft.Extensions.AI` (MEAI) | Always the foundation. Use `IChatClient` directly for prompt-response and simple, bounded function invocation. |
| **Provider SDK** | `Azure.AI.OpenAI` / `OpenAI` / `Azure.AI.Inference` / `OllamaSharp` | Concrete provider behind MEAI via `AddChatClient`. |
| **Orchestration** | `Microsoft.Agents.AI` (prerelease) | Multi-step tool use, durable agent loops, and multi-agent workflows. |
| **Copilot** | `GitHub.Copilot.SDK` | Building Copilot-platform extensions only. |

Rules: start with MEAI; put the provider behind it via `AddChatClient` (don't call the provider in
business logic); use `Microsoft.Agents.AI` for multi-step or durable agent workflows rather than
hand-rolling an agent loop; never mix a raw `HttpClient`-to-OpenAI call with MEAI in the same
workflow. Do **not** use Accord.NET (archived). For new projects, prefer MEAI and Agent Framework
unless existing Semantic Kernel features or investments are a requirement. Register AI/ML services
via DI; load secrets from user-secrets / env / Key Vault — never hardcode keys.

## Step 2: Cover the branch essentials, then decide depth

Every answer — plan or implementation — must address the guardrails for the selected branch:

- **ML.NET** — `new MLContext(seed: …)` (reproducible); `TrainTestSplit` + evaluate on the held-out
  set; report real metrics (MicroAccuracy/MacroAccuracy/LogLoss, AUC/F1, or RMSE/R²); serve with
  `PredictionEnginePool<TIn,TOut>` (never a singleton `PredictionEngine`).
- **LLM (MEAI)** — depend on `IChatClient` registered via `AddChatClient` (provider behind it);
  set `Temperature` and `MaxOutputTokens` in `ChatOptions`; add retry/timeout
  (`RetryingChatClient`/Polly); pin a dated model; load keys from user-secrets / env / Key Vault —
  **never hardcode an `sk-…` key**; validate non-deterministic output against a schema with a
  fallback.
- **Agentic (Agent Framework)** — orchestrate with `Microsoft.Agents.AI` on `IChatClient` (never a
  hand-rolled loop); set `MaximumIterations` and a token/cost ceiling; define each tool with a clear
  schema (`AIFunctionFactory.Create`); log each step (never raw sensitive content).
- **RAG / embeddings** — semantic **chunking** (not fixed-size); `IEmbeddingGenerator` and **cache
  the embeddings** (don't re-embed per query); store/query with
  `Microsoft.Extensions.VectorData.Abstractions` (MEVD) + the provider the user asked for (e.g.
  pgvector); filter by a **minimum similarity score**; keep **source attribution** for each answer.
  Honor the UI/storage the user specified; use only real, existing NuGet packages.

**Then choose depth:**

- **Plan / comparison / architecture only** (or "do not write code"): answer from this file alone
  using the essentials above. **Do NOT open a reference** — the branch essentials here are
  sufficient for a selection or plan. For RAG plans, cover chat, ingestion/chunking, embeddings,
  vector storage, source attribution, and the requested UI/storage.
- **Writing implementation code**: read the matching reference(s) for packages and implementation
  guidance (read only the selected branch; for Hybrid, read both Classic ML.NET and LLM):
  - Classic ML.NET → [`references/classic-ml.md`](references/classic-ml.md)
  - LLM integration (MEAI) → [`references/llm.md`](references/llm.md)
  - Agentic (Agent Framework) → [`references/agentic.md`](references/agentic.md)
  - RAG / embeddings / ingestion → [`references/rag.md`](references/rag.md)
  - GitHub Copilot extensions → [`references/copilot.md`](references/copilot.md)
  - ONNX Runtime inference → [`references/onnx.md`](references/onnx.md)
  - Local/offline LLM with Ollama → [`references/ollama.md`](references/ollama.md)

## Validation

- [ ] Selection follows the decision tree — no LLM for tasks ML.NET handles
- [ ] Only what was asked is produced (plan-only requests get a plan, not code)
- [ ] AI/ML services registered via DI; config via `IOptions<T>`; keys from secure sources
- [ ] Branch guardrails (Step 2 essentials, plus the reference when implementing) are satisfied
- [ ] After implementing, build and run existing tests

## Anti-Patterns to Reject

| Anti-pattern | Redirect |
|-------------|----------|
| LLM for tabular classification | Use **ML.NET** — faster, cheaper, deterministic |
| LLM calls without retry/timeout | Add `RetryingChatClient` or Polly retry |
| API keys in committed `appsettings.json` | user-secrets / env / Key Vault |
| Accord.NET, or defaulting to Semantic Kernel without a requirement | ML.NET; prefer MEAI + `Microsoft.Agents.AI` for new work |
| Hand-rolled multi-step tool loops with `IChatClient` | `Microsoft.Agents.AI` (`MaximumIterations`, tool dispatch) |
| Agent Framework for a single prompt→response | `IChatClient` directly |
| Raw `HttpClient`/OpenAI SDK in business logic alongside MEAI | one abstraction layer; depend on `IChatClient` |
| `PredictionEngine` singleton in ASP.NET Core | `PredictionEnginePool<TIn,TOut>` (not thread-safe) |
| RAG without chunking or relevance filtering | semantic chunking + minimum similarity score |
| Building custom neural nets in .NET from scratch | pre-trained via ONNX Runtime or an LLM API |
