---
name: data-ingestion-pipeline
description: >
  Guides building a document ingestion pipeline for RAG using
  Microsoft.Extensions.DataIngestion.
  USE FOR: Reading documents (Markdown, PDF, Word), chunking text, enriching
  chunks (summaries, keywords, sentiment), writing to vector stores. Building
  the ingestion half of a RAG system.
  DO NOT USE FOR: Vector search/storage setup (use vector-data-search), LLM chat
  integration (use meai-chat-integration), end-to-end RAG (use rag-pipeline),
  tabular data processing (use mlnet).
---

# Data Ingestion Pipeline

This skill guides an agent through building a document ingestion pipeline for retrieval-augmented generation (RAG) using `Microsoft.Extensions.DataIngestion`. The pipeline reads documents (Markdown, PDF, Word), splits them into chunks, optionally enriches chunks with summaries or keywords, generates embeddings, and writes the results to a vector store.

## When to Use

- Building the ingestion half of a RAG system that reads documents into a vector store
- Reading Markdown, PDF, Word, PowerPoint, or HTML documents into a processing pipeline
- Chunking documents for embedding and retrieval
- Enriching chunks with summaries, keywords, sentiment, or classifications before storage
- Writing embedded chunks to a vector store for later search

## When Not to Use

- **Vector search/storage setup only** — use the `vector-data-search` skill instead
- **LLM chat integration** — use the `meai-chat-integration` skill instead
- **End-to-end RAG** (ingestion + retrieval + chat) — use the `rag-pipeline` skill instead
- **Tabular or structured data processing** — use the `mlnet` skill instead

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Document source | Yes | Path or location of documents to ingest (Markdown, PDF, Word, etc.) |
| Document format | Yes | File type(s) to read — determines which reader package to install |
| Embedding model | Yes | The embedding model to use for vectorizing chunks (e.g., `text-embedding-3-small`) |
| Vector store | Yes | Target vector store and connector (e.g., Azure AI Search, Qdrant, in-memory) |
| Chunking strategy | Recommended | How to split documents — by headers, sections, or semantic similarity |
| Enrichments | Optional | Which enrichers to apply: summary, keywords, sentiment, classification |

## Workflow

> **Commit strategy:** Commit after each step produces a working pipeline stage. This keeps the pipeline buildable and testable incrementally.

### Step 1: Install packages

Install the core pipeline package and the reader for your document format:

```
dotnet add package Microsoft.Extensions.DataIngestion
```

Choose a reader based on document format:

- **IF Markdown** → `dotnet add package Microsoft.Extensions.DataIngestion.Markdig`
- **IF PDF, Word, PowerPoint, HTML, or other formats** → `dotnet add package Microsoft.Extensions.DataIngestion.MarkItDown`

For embedding enrichment, add the AI abstractions and your provider package:

```
dotnet add package Microsoft.Extensions.AI
```

For writing to a vector store, add the vector data abstractions and your connector:

```
dotnet add package Microsoft.Extensions.VectorData.Abstractions
```

### Step 2: Configure the document reader

Set up the reader that converts raw files into `IngestionDocument` objects:

- **Markdig reader**: Reads `.md` files, preserving markdown structure for downstream chunking.
- **MarkItDown reader**: Reads PDF, DOCX, PPTX, HTML, and other formats by converting them to markdown first, then producing `IngestionDocument` objects.

### Step 3: Choose a chunking strategy

Select a chunker based on the structure of your documents:

- **IF documents have clear headers/sections** → `HeaderChunker` — splits on markdown headers, keeping each section as a chunk.
- **IF documents have page or section boundaries** → `SectionChunker` — splits on explicit boundaries.
- **IF you need semantic coherence within chunks** → `SemanticChunker` — groups semantically related paragraphs together.

Configure token limits and overlap:

- Set `MaxTokensPerChunk` to fit your embedding model's context window. For most embedding models, **256–512 tokens per chunk** works well.
- Use `Microsoft.ML.Tokenizers` to count tokens accurately:
  ```csharp
  var tokenizer = TiktokenTokenizer.CreateForModel("text-embedding-3-small");
  var tokenCount = tokenizer.CountTokens(chunkText);
  ```
- Configure **10–20% overlap** between chunks to preserve context at boundaries.

### Step 4: Add processors/enrichers (optional)

Add one or more enrichers to augment chunks before storage:

- **SummaryEnricher** — generates a short summary per chunk using `IChatClient`.
- **KeywordEnricher** — extracts keywords from each chunk.
- **SentimentEnricher** — scores each chunk's sentiment.
- **ClassificationEnricher** — classifies chunks into predefined categories.

Each enricher runs as a pipeline processor and attaches metadata to the chunk.

### Step 5: Configure the writer

Set up `VectorStoreWriter` to write chunks with embeddings to a vector store collection:

- The writer needs an `IEmbeddingGenerator` to generate embeddings for each chunk.
- The writer needs a `VectorStoreCollection` configured for your target vector store.

### Step 6: Build and run the pipeline

Assemble the pipeline using the builder, then execute it:

```csharp
var pipeline = new IngestionPipelineBuilder()
    .AddReader(reader)
    .AddChunker(chunker)
    .AddProcessor(enricher)    // optional, repeat for multiple enrichers
    .AddWriter(writer)
    .Build();

var result = await pipeline.RunAsync(documents);
```

### Step 7: Handle errors

`IngestionResult` may indicate partial success. Check for failures and handle them:

- Inspect `result.FailedDocuments` for documents that could not be processed.
- Log failures with enough detail to diagnose the issue (file path, stage where failure occurred).
- Retry transient failures (e.g., embedding API rate limits) with exponential backoff.

## Validation

- [ ] Documents are read successfully by the configured reader
- [ ] Chunks have the expected size (within `MaxTokensPerChunk` limit)
- [ ] Enrichments are populated on chunks (summaries, keywords, etc., if configured)
- [ ] Chunks are written to the vector store with embeddings
- [ ] A search query against the vector store returns relevant chunks
- [ ] `result.FailedDocuments` is empty or failures are handled

## Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| Chunks too large — exceed embedding model context window | Set `MaxTokensPerChunk` to stay within the model's limit (typically 256–512 tokens). Use `Microsoft.ML.Tokenizers` to validate token counts. |
| Chunks too small — lose context and produce poor retrieval results | Increase `MaxTokensPerChunk` or switch to `SemanticChunker` to keep related content together. |
| No overlap between chunks — information lost at chunk boundaries | Configure 10–20% overlap so context is preserved across chunk boundaries. |
| Using character count instead of token count for chunk sizing | Always use token-based sizing. Character counts do not map reliably to tokens. Use `TiktokenTokenizer.CreateForModel()` to count tokens accurately. |
| Not embedding chunks before writing to vector store | Ensure the `VectorStoreWriter` is configured with an `IEmbeddingGenerator`. Without embeddings, chunks cannot be searched by similarity. |
| Not handling partial pipeline failures | Always check `result.FailedDocuments` after `RunAsync`. Partial failures are silent if not inspected. Retry transient errors and log permanent failures. |

## More Info

- [Data ingestion in .NET AI](https://learn.microsoft.com/dotnet/ai/conceptual/data-ingestion) — conceptual overview of the ingestion pipeline
