---
name: vector-data-search
description: |
  USE FOR: Storing vector embeddings, semantic similarity search, filtered vector search, managing vector store collections, choosing a vector database connector
  DO NOT USE FOR: Generating embeddings (use meai-embeddings), document ingestion/chunking (use data-ingestion-pipeline), end-to-end RAG (use rag-pipeline), classical ML on structured data (use mlnet)
---

# Vector Data Storage & Semantic Search

Add vector storage and semantic search to a .NET application using `Microsoft.Extensions.VectorData`.

## Workflow

### Step 1 · Install Packages

Always install the abstractions package:

```
dotnet add package Microsoft.Extensions.VectorData.Abstractions
```

Then choose a connector based on your scenario:

| Scenario | Package |
|---|---|
| Prototyping / dev | `Microsoft.SemanticKernel.Connectors.InMemory` |
| Local persistence | `Microsoft.SemanticKernel.Connectors.SqliteVec` (+ `Microsoft.Data.Sqlite`) |
| Azure cloud | `Microsoft.SemanticKernel.Connectors.AzureAISearch` or `Microsoft.SemanticKernel.Connectors.CosmosNoSql` |
| Self-hosted Postgres | `Microsoft.SemanticKernel.Connectors.Postgres` (requires pgvector extension) |
| Qdrant | `Microsoft.SemanticKernel.Connectors.Qdrant` |
| Redis | `Microsoft.SemanticKernel.Connectors.Redis` |

> **Provenance note:** Connector packages use the `Microsoft.SemanticKernel.Connectors.*` namespace but they depend ONLY on `Microsoft.Extensions.VectorData.Abstractions`. There is no dependency on Semantic Kernel itself. This is a packaging artifact from the migration.

### Step 2 · Define a Data Model

```csharp
public class DocumentChunk
{
    [VectorStoreKey]
    public string Id { get; set; }

    [VectorStoreData(IsFilterable = true)]
    public string Source { get; set; }

    [VectorStoreData]
    public string Content { get; set; }

    [VectorStoreVector(Dimensions: 1536, DistanceFunction.CosineSimilarity)]
    public ReadOnlyMemory<float> Embedding { get; set; }
}
```

### Step 3 · Create a Collection and Upsert Records

```csharp
// Create a vector store (swap InMemoryVectorStore for the connector of your choice)
var store = new InMemoryVectorStore();

// Get a typed collection handle
var collection = store.GetCollection<string, DocumentChunk>("documents");

// Ensure the backing collection/table exists
await collection.EnsureCollectionExistsAsync();

// Upsert a record
await collection.UpsertAsync(record);
```

### Step 4 · Perform a Vector Search

```csharp
var results = await collection.SearchAsync(queryEmbedding, top: 5);
```

- Use filter expressions with properties marked `IsFilterable = true` to narrow results.
- Set a minimum score threshold to discard irrelevant matches.

### Step 5 · Register in Dependency Injection

Register the vector store and collection in `IServiceCollection` so they are available throughout the application.

## Validation

- Collection is created successfully.
- Records are upserted without error.
- Search returns semantically relevant results.
- Filtered search correctly narrows the result set.

## Pitfalls

- **Dimension mismatch** – The `Dimensions` value in `[VectorStoreVector]` must match the embedding model's output size.
- **Missing distance function** – Not setting `DistanceFunction` explicitly; defaults vary by connector.
- **Unfilterable properties** – Forgetting `IsFilterable = true` on properties used in filter expressions.
- **Collection creation on every request** – Call `EnsureCollectionExistsAsync` at startup, not per-request.
- **Unhandled exceptions** – Catch `VectorStoreOperationException` for transient and configuration errors.

## More Information

<https://learn.microsoft.com/dotnet/ai/conceptual/vector-databases>
