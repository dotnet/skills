---
name: meai-embeddings
description: Generate text embeddings in .NET using Microsoft.Extensions.AI IEmbeddingGenerator for semantic similarity, vector search, RAG, clustering by meaning, and document deduplication. Do not use for tabular data features (use mlnet), storing or searching vectors (use vector-data-search), or end-to-end RAG pipelines (use rag-pipeline).
---

# MEAI Embeddings

Generate and use text embeddings in .NET through the `IEmbeddingGenerator<string, Embedding<float>>` abstraction from Microsoft.Extensions.AI, with support for OpenAI, Azure OpenAI, Azure AI Inference, and Ollama providers.

## When to Use

- Generating text embeddings for semantic similarity comparisons
- Producing vectors for storage in a vector database
- Building the embedding step of a RAG pipeline
- Clustering documents by meaning
- Deduplicating documents based on semantic content

## When Not to Use

- Extracting features from tabular data (use the `mlnet` skill)
- Storing and searching vectors in a database (use the `vector-data-search` skill)
- Building an end-to-end RAG pipeline (use the `rag-pipeline` skill)

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Text to embed | Yes | One or more strings to generate embeddings for |
| Embedding provider | Yes | The provider SDK: OpenAI, Azure.AI.OpenAI, Azure.AI.Inference, or OllamaSharp |
| Embedding model | Yes | Model name (e.g., `text-embedding-3-small`, `text-embedding-3-large`) |
| API key / endpoint | Yes | Provider credentials, loaded from configuration |

## Workflow

### Step 1: Install packages

Add the abstraction package and one provider package:

```bash
dotnet add package Microsoft.Extensions.AI
```

Then add the provider package for your backend:

```bash
# OpenAI
dotnet add package OpenAI

# Azure OpenAI
dotnet add package Azure.AI.OpenAI

# Azure AI Inference
dotnet add package Azure.AI.Inference

# Ollama
dotnet add package OllamaSharp
```

`Microsoft.Extensions.AI` includes middleware such as `UseRateLimiting()` — no extra package is needed for rate limiting.

### Step 2: Register IEmbeddingGenerator in DI

Use `AddEmbeddingGenerator` to register the generator with optional middleware:

```csharp
builder.Services.AddEmbeddingGenerator(b => b
    .UseRateLimiting()
    .UseOpenTelemetry()
    .UseLogging()
    .Use(new OpenAIClient(apiKey)
        .GetEmbeddingClient(model)
        .AsIEmbeddingGenerator()));
```

Replace the inner provider call for other backends:

```csharp
// Azure OpenAI
new AzureOpenAIClient(new Uri(endpoint), new DefaultAzureCredential())
    .GetEmbeddingClient(model)
    .AsIEmbeddingGenerator()

// Azure AI Inference
new EmbeddingsClient(new Uri(endpoint), new AzureKeyCredential(key))
    .AsIEmbeddingGenerator()

// Ollama
new OllamaApiClient(new Uri("http://localhost:11434"), model)
    .AsIEmbeddingGenerator()
```

### Step 3: Generate embeddings

Inject `IEmbeddingGenerator<string, Embedding<float>>` and generate embeddings:

```csharp
// Single text
var embedding = await generator.GenerateAsync("text to embed");
ReadOnlyMemory<float> vector = embedding[0].Vector;

// Batch of texts
var embeddings = await generator.GenerateAsync(["first text", "second text", "third text"]);
for (int i = 0; i < embeddings.Count; i++)
{
    ReadOnlyMemory<float> v = embeddings[i].Vector;
}
```

Always prefer batch generation over calling `GenerateAsync` once per string — it reduces HTTP round-trips and is more efficient.

### Step 4: Choose embedding model dimensions

| Model | Dimensions | Notes |
|-------|-----------|-------|
| `text-embedding-3-small` | 1536 | Lower cost, good for most use cases |
| `text-embedding-3-large` | 3072 | Higher accuracy, larger storage footprint |

Some providers allow reducing dimensions via an API parameter to save storage. Ensure the dimensionality you choose matches your vector store index configuration.

### Step 5: Verify

- [ ] Embeddings have the expected dimensionality for the chosen model
- [ ] Semantically similar texts produce high cosine similarity scores
- [ ] Rate limiting middleware prevents HTTP 429 errors under load

## Validation

- [ ] `IEmbeddingGenerator<string, Embedding<float>>` is registered via DI and can be resolved
- [ ] API keys and endpoints are loaded from configuration (not hard-coded)
- [ ] Batch generation is used when embedding multiple texts (not one-at-a-time calls)
- [ ] Rate limiting is configured for production workloads

## Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| Generating embeddings one at a time instead of batching | Pass a list to `GenerateAsync` to embed multiple texts in a single call |
| Embedding model dimensions don't match the vector store index | Verify the model's output dimensions match your vector store's configured dimensions |
| Mixing embeddings from different models in the same collection | Always use a single model per collection; re-embed all documents when switching models |
| Not configuring rate limiting and hitting provider quotas | Add `.UseRateLimiting()` in the middleware pipeline to throttle requests |

## More Info

See https://learn.microsoft.com/dotnet/ai/microsoft-extensions-ai for the full Microsoft.Extensions.AI reference.
