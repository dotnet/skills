# Microsoft.ML.Tokenizers — Token Counting and Text Encoding for .NET

Read this reference when the user needs to count tokens for context window management, truncate text to a token budget, encode or decode text for LLM or ONNX model preprocessing, or size chunks for RAG ingestion.

## What It Is

`Microsoft.ML.Tokenizers` is a pure .NET tokenization library — no Python runtime, no native binaries. It converts text to token IDs and back, matching the exact tokenization behavior of popular LLMs. It is part of the ML.NET ecosystem but works standalone in any .NET application.

## Install

```
dotnet add package Microsoft.ML.Tokenizers
```

For model-specific tokenizer data (vocabulary files), install the relevant data package:

```
dotnet add package Microsoft.ML.Tokenizers.Data.O200kBase   # GPT-4o
dotnet add package Microsoft.ML.Tokenizers.Data.Cl100kBase   # GPT-4, GPT-3.5
```

## Supported Tokenizers

| Class | Algorithm | Models | Factory |
|---|---|---|---|
| `TiktokenTokenizer` | Tiktoken (BPE) | GPT-4o, GPT-4, GPT-3.5-turbo | `TiktokenTokenizer.CreateForModel("gpt-4o")` |
| `BpeTokenizer` | Byte-level BPE | GPT-2, RoBERTa, custom BPE | `BpeTokenizer.Create(vocabPath, mergesPath)` |
| `SentencePieceTokenizer` | SentencePiece (Unigram) | T5, XLNet, mBART | `SentencePieceTokenizer.Create(modelPath)` |
| `LlamaTokenizer` | SentencePiece (BPE) | Llama 2, Llama 3, Mistral | `LlamaTokenizer.Create(modelPath)` |
| `BertTokenizer` | WordPiece | BERT, DistilBERT, MiniLM | `BertTokenizer.Create(vocabPath)` |
| `WordPieceTokenizer` | WordPiece | Custom WordPiece models | `WordPieceTokenizer.Create(vocabPath)` |
| `CodeGenTokenizer` | BPE | CodeGen, Codex | `CodeGenTokenizer.Create(vocabPath, mergesPath)` |
| `Phi2Tokenizer` | BPE | Phi-2 | `Phi2Tokenizer.Create(vocabPath, mergesPath)` |

All inherit from the abstract `Tokenizer` base class.

## Core Operations

### Count Tokens (Most Common)

Use `CountTokens()` to check whether text fits a model's context window. This is the fast path — it avoids allocating the full token ID list.

```csharp
using Microsoft.ML.Tokenizers;

var tokenizer = TiktokenTokenizer.CreateForModel("gpt-4o");
int count = tokenizer.CountTokens(text);

if (count > maxContextTokens)
{
    // Truncate or summarize
}
```

> Always use `CountTokens()` instead of `EncodeToIds(text).Count` when you only need the count.

### Truncate to a Token Budget

`GetIndexByTokenCount` returns the character index where the token count reaches the limit. Use it to truncate without splitting mid-token.

```csharp
var tokenizer = TiktokenTokenizer.CreateForModel("gpt-4o");
int maxTokens = 4096;

int charIndex = tokenizer.GetIndexByTokenCount(text, maxTokens);
var truncated = text.AsSpan()[..charIndex];
```

### Encode Text to Token IDs

Use when you need the actual token IDs — for example, when preparing inputs for ONNX models or inspecting tokenization behavior.

```csharp
var tokenizer = TiktokenTokenizer.CreateForModel("gpt-4o");

// Token IDs only
IReadOnlyList<int> ids = tokenizer.EncodeToIds(text);

// Full token details (string value, ID, character offsets)
IReadOnlyList<EncodedToken> tokens = tokenizer.EncodeToTokens(text);
foreach (var token in tokens)
{
    Console.WriteLine($"'{token.Value}' -> ID {token.Id} (offset {token.Offset})");
}
```

### Decode Token IDs to Text

```csharp
string decoded = tokenizer.Decode(ids);
```

## How This Library Is Used Across Skills

| Skill | Use Case | Key Method |
|---|---|---|
| `meai-chat-integration` | Count tokens before sending prompts to stay within context window; truncate long inputs | `CountTokens()`, `GetIndexByTokenCount()` |
| `data-ingestion-pipeline` | Size chunks by token count (not character count) to match embedding model limits | `CountTokens()` |
| `onnx-runtime-inference` | Preprocess text inputs for ONNX models (BERT tokenization, vocabulary encoding) | `EncodeToIds()` via `BertTokenizer` |
| `rag-pipeline` | Token budget management for prompt assembly during retrieval-augmented generation | `CountTokens()`, `GetIndexByTokenCount()` |

## Key Points

- **Always use token counts, not character counts.** Characters do not map reliably to tokens. A 4-character word might be 1 token or 3 depending on the model's vocabulary.
- **Match the tokenizer to your model.** GPT-4o uses `o200k_base`, GPT-4/3.5 uses `cl100k_base`, Llama uses SentencePiece BPE. Using the wrong tokenizer gives inaccurate counts.
- **`CountTokens()` is the fast path.** Use it when you only need the count. It avoids allocating the token ID list.
- **Pure .NET, no dependencies.** Runs anywhere .NET runs — no Python runtime, no native binaries, no platform-specific code.
- **Thread-safe.** `Tokenizer` instances are immutable after creation. Create once, reuse across requests.

## More Information

- <https://www.nuget.org/packages/Microsoft.ML.Tokenizers>
- <https://github.com/dotnet/machinelearning> (source lives in the ML.NET repo under `src/Microsoft.ML.Tokenizers`)
