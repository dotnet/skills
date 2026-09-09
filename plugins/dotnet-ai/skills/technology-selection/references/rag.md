# RAG, embeddings, and document ingestion

Use for semantic search and retrieval-augmented Q&A over documents. Two concerns: **ingestion**
(parse → chunk → embed → store) and **query** (embed question → vector search → ground the answer).

## Packages

```xml
<PackageReference Include="Microsoft.Extensions.AI" Version="9.*" />                       <!-- IEmbeddingGenerator, IChatClient -->
<PackageReference Include="Microsoft.Extensions.VectorData.Abstractions" Version="9.*" />  <!-- MEVD -->
<PackageReference Include="Microsoft.Extensions.AI.DataIngestion" Version="9.*-*" />       <!-- preview: parse/chunk/embed/upsert -->
<!-- + a vector provider, e.g. pgvector for PostgreSQL, Azure AI Search, Qdrant, Redis -->
```

## Guardrails

1. **Abstractions** — `IEmbeddingGenerator` for embeddings,
   `Microsoft.Extensions.VectorData.Abstractions` (MEVD) for the store, `IChatClient` for generation.
2. **Semantic chunking** — chunk on paragraph/semantic boundaries, not naive fixed-size cuts.
   Use `Microsoft.Extensions.AI.DataIngestion` (or equivalent parse/chunk) for PDFs/markdown.
3. **Relevance threshold** — filter retrieved chunks by a **minimum similarity score**; don't feed
   low-scoring noise to the model.
4. **Source attribution** — track which document chunks contributed to each answer.
5. **Cache embeddings** — persist embeddings; never re-embed the corpus on every query. Batch
   embedding calls during ingestion.

## Minimal query shape (provider-specific pseudocode)

```csharp
var queryEmbedding = await embeddingGenerator.GenerateAsync(question, ct);
var hits = await SearchProviderAsync(queryEmbedding, top: 5, cancellationToken: ct);
var grounded = hits.Where(h => h.Score >= 0.75);   // minimum similarity threshold
// build prompt with the grounded chunks + their source ids for attribution, then IChatClient.GetResponseAsync
```
