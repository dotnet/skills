# System.Numerics.Tensors for ONNX Input/Output

Tensor types needed when working with ONNX Runtime's standalone API (Approach A). Use this reference when creating and manipulating input/output tensors.

## Key Types

### DenseTensor\<T\> (Microsoft.ML.OnnxRuntime.Tensors)

ONNX Runtime's tensor type for creating inputs.

```csharp
// Create from data and dimensions
var data = new float[] { 1f, 2f, 3f, 4f };
var dimensions = new[] { 1, 4 };
var tensor = new DenseTensor<float>(data, dimensions);
```

Common shapes:
- `[1, 3, 224, 224]` — images (batch, channels, height, width)
- `[1, sequenceLength]` — text token IDs

### Tensor\<T\> (System.Numerics.Tensors, .NET 10+)

.NET's native tensor type. Heap-allocated N-dimensional array with rich slicing and reshape operations.

```csharp
var tensor = Tensor.Create<float>(data, shape);
```

### TensorSpan\<T\> (System.Numerics.Tensors)

Mutable zero-copy view for in-place operations on tensor data. `ref struct` — cannot be stored in fields.

### TensorPrimitives (System.Numerics.Tensors)

SIMD-accelerated math for pre/post-processing:

```csharp
TensorPrimitives.Softmax(input, output);              // Post-process logits
TensorPrimitives.CosineSimilarity(a, b);               // Similarity
TensorPrimitives.Multiply(a, scalar, result);           // Scaling
```

## Common Patterns

### Normalize Image

Scale pixel values to `[0, 1]`, subtract mean, divide by std:

```csharp
var pixels = new float[1 * 3 * 224 * 224];
// Load and scale pixels to [0, 1]
// Subtract channel means: [0.485, 0.456, 0.406]
// Divide by channel stds: [0.229, 0.224, 0.225]
var input = new DenseTensor<float>(pixels, new[] { 1, 3, 224, 224 });
```

### Post-process Classification

Apply softmax to logits and find argmax:

```csharp
var logits = output.ToArray();
var probabilities = new float[logits.Length];
TensorPrimitives.Softmax(logits, probabilities);
int predictedClass = TensorPrimitives.IndexOfMax(probabilities);
```

### Extract Embeddings

Take mean or CLS pooling of model output:

```csharp
// CLS pooling: use the first token's embedding
var cls = outputTensor.Buffer.Span.Slice(0, hiddenSize).ToArray();

// Mean pooling: average across the sequence dimension
var embedding = new float[hiddenSize];
for (int i = 0; i < sequenceLength; i++)
    TensorPrimitives.Add(embedding, tokenEmbeddings.AsSpan(i * hiddenSize, hiddenSize), embedding);
TensorPrimitives.Divide(embedding, sequenceLength, embedding);
```
