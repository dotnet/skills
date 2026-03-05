---
name: rag-pipeline
description: >
  USE FOR: Building document Q&A systems, grounding LLM responses in a document corpus,
  end-to-end RAG (ingest documents → embed → store → search → generate grounded responses),
  chatbots that answer from your own data.
  DO NOT USE FOR: Simple LLM chat without document grounding (use meai-chat-integration),
  classical ML on structured data (use mlnet), agentic workflows beyond simple RAG (use agentic-workflow).
---

# RAG Pipeline

Build a complete end-to-end Retrieval-Augmented Generation pipeline in .NET: ingest documents, generate embeddings, store in a vector database, retrieve relevant chunks, and generate grounded LLM responses with source attribution.

This skill is **self-contained** — it includes all guidance needed to build the full pipeline without loading other skills.

## Inputs

| Input | Description |
|-------|-------------|
| Document corpus | Directory of files to ingest (Markdown, PDF, Word, etc.) |
| LLM provider preference | OpenAI, Azure OpenAI, Ollama, etc. |
| Vector store preference | InMemory (dev), Azure AI Search, Qdrant, etc. |
| Target framework | .NET 8+ recommended |

## Workflow

### Step 1 — Install Packages

Add the required NuGet packages:

**Core:**

```shell
dotnet add package Microsoft.Extensions.AI
```

**LLM / Embedding provider** (pick one):

```shell
# OpenAI
dotnet add package OpenAI

# Azure OpenAI
dotnet add package Azure.AI.OpenAI
```

**Vector data:**

```shell
dotnet add package Microsoft.Extensions.VectorData.Abstractions

# Dev / prototyping
dotnet add package Microsoft.Extensions.VectorData.InMemory

# Production — install the connector for your chosen store
# e.g. Microsoft.Extensions.VectorData.AzureAISearch, etc.
```

**Data ingestion + document readers:**

```shell
dotnet add package Microsoft.Extensions.DataIngestion

# Markdown support
dotnet add package Markdig

# PDF / Word support
dotnet add package MarkItDown
```

### Step 2 — Define the Data Model for Chunks

Create a record class annotated with vector-store attributes:

```csharp
using Microsoft.Extensions.VectorData;

public class DocumentChunk
{
    [VectorStoreKey]
    public string Id { get; set; }

    [VectorStoreData]
    public string Content { get; set; }

    [VectorStoreData(IsFilterable = true)]
    public string Source { get; set; }

    [VectorStoreVector(Dimensions: 1536, DistanceFunction.CosineSimilarity)]
    public ReadOnlyMemory<float> Embedding { get; set; }
}
```

> **Note:** Set `Dimensions` to match the embedding model output (e.g., 1536 for OpenAI `text-embedding-3-small`).

### Step 3 — Ingest Documents

Configure and run the ingestion pipeline:

1. **Choose a reader** based on file types:
   - `Markdig` for Markdown files.
   - `MarkItDown` for PDF, Word, and other binary document formats.

2. **Configure a chunker:**
   - `HeaderChunker` — best for structured documents with clear headings.
   - `SemanticChunker` — best for unstructured prose.
   - Set `MaxTokensPerChunk` to 256–512 (typical range).
   - Set overlap to 10–20% to preserve context at chunk boundaries.

3. **Configure a `VectorStoreWriter`** with an `IEmbeddingGenerator` so chunks are embedded automatically during ingestion.

4. **Build and run the `IngestionPipeline`:**

```csharp
// Pseudo-structure — adapt to your chosen reader and chunker
var pipeline = new IngestionPipelineBuilder()
    .WithReader(reader)           // Markdig or MarkItDown reader
    .WithChunker(chunker)         // HeaderChunker or SemanticChunker
    .WithEmbeddingGenerator(embeddingGenerator)
    .WithVectorStore(collection)
    .Build();

await pipeline.RunAsync(documentsDirectory);
```

### Step 4 — Set Up the Vector Store

Create the collection and verify ingestion:

```csharp
// Ensure the collection exists
await collection.EnsureCollectionExistsAsync();

// Verify chunks were stored
var testEmbedding = await embeddingGenerator.GenerateAsync("test query");
var verifyResults = await collection.SearchAsync(testEmbedding[0].Vector, top: 1);
// Should return at least one result
```

### Step 5 — Build Retrieval + Generation

Wire up the full question-answering flow:

```csharp
async Task<string> AskQuestion(
    string question,
    IChatClient chatClient,
    IEmbeddingGenerator<string, Embedding<float>> embedder,
    VectorStoreCollection<string, DocumentChunk> collection)
{
    // 1. Embed the question
    var questionEmbedding = await embedder.GenerateAsync(question);

    // 2. Search for relevant chunks
    var results = await collection.SearchAsync(questionEmbedding[0].Vector, top: 5);
    var relevantChunks = results.Where(r => r.Score >= 0.7f); // minimum similarity threshold

    // 3. Build grounded prompt
    var context = string.Join("\n\n",
        relevantChunks.Select(r =>
            $"[Source: {r.Record.Source}]\n{r.Record.Content}"));

    var systemPrompt = $"""
        Answer the user's question based ONLY on the provided context.
        If the context doesn't contain the answer, say "I don't have information about that."
        Always cite your sources.

        Context:
        {context}
        """;

    // 4. Generate grounded response
    var messages = new List<ChatMessage>
    {
        new(ChatRole.System, systemPrompt),
        new(ChatRole.User, question)
    };

    var response = await chatClient.GetResponseAsync(messages);
    return response.Text;
}
```

### Step 6 — Source Attribution

Track which chunks contributed to each answer:

- Include the source file name and section/heading in every chunk's `Source` property during ingestion.
- When building the grounded prompt (Step 5), each chunk is already tagged with `[Source: …]`.
- Instruct the LLM in the system prompt to cite those sources in its response.

### Step 7 — Verify the Pipeline

Run these checks to confirm the pipeline works end-to-end:

1. **Ingest test documents** — confirm chunks are created without errors.
2. **Ask a question the documents CAN answer** — verify the response is grounded in the actual content (not hallucinated).
3. **Verify source citations** — confirm the response references the correct source documents.
4. **Ask a question the documents CANNOT answer** — verify the model responds with "I don't have information about that" rather than hallucinating.

## Validation Checklist

- [ ] Documents ingested and chunked correctly
- [ ] Chunks stored with embeddings in vector store
- [ ] Retrieval returns relevant chunks for test queries
- [ ] LLM response is grounded in retrieved context
- [ ] Source attribution present in responses
- [ ] Out-of-scope questions handled gracefully (no hallucination)

## Pitfalls

| Pitfall | Why It Matters |
|---------|----------------|
| No minimum similarity score threshold | Irrelevant chunks get included, confusing the LLM. |
| Missing source attribution | Cannot verify whether the response is actually grounded. |
| Too few chunks retrieved | Misses relevant context the LLM needs. |
| Too many chunks retrieved | Dilutes the signal with noise, may exceed context window. |
| System prompt doesn't instruct LLM to stay grounded | Model may hallucinate instead of using the provided context. |
| Re-embedding documents on every query | Wastes compute and time — cache/persist embeddings in the vector store. |
| Chunks too large | May exceed context limits and reduce retrieval precision. |
| Chunks too small | Lose semantic meaning and context around the information. |

## More Info

- [RAG overview](https://learn.microsoft.com/dotnet/ai/conceptual/rag)
- [Data ingestion](https://learn.microsoft.com/dotnet/ai/conceptual/data-ingestion)
- [Vector databases](https://learn.microsoft.com/dotnet/ai/conceptual/vector-databases)
