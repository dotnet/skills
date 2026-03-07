---
name: onnx-runtime-inference
description: >
  USE FOR: Loading and running pre-trained ONNX models, hardware-accelerated inference (CPU, GPU, DirectML),
  custom model deployment, HuggingFace model inference, combining ONNX scoring with ML.NET pipelines.
  DO NOT USE FOR: Training models from scratch (use mlnet for classical ML and supported deep learning tasks; TorchSharp for custom architectures),
  LLM text generation (use meai-chat-integration or local-llm-inference),
  classical ML without a pre-trained model (use mlnet).
---

# ONNX Runtime Inference in .NET

This skill guides running pre-trained ONNX model inference in .NET.

## Choose Your Approach

Decide which approach fits your scenario:

- **Approach A: Standalone ONNX Runtime** — Use when you need maximum control over tensors and execution providers.
- **Approach B: ML.NET + ONNX Integration** — Use when you need to combine ONNX with ML.NET data transforms and pipelines.

---

## Approach A: Standalone ONNX Runtime

### Step A1: Install Packages

- `Microsoft.ML.OnnxRuntime` — CPU inference
- IF CUDA GPU → `Microsoft.ML.OnnxRuntime.Gpu` instead
- IF Windows GPU → `Microsoft.ML.OnnxRuntime.DirectML` instead

### Step A2: Create InferenceSession

> **SINGLETON** — Create once, reuse. Do not create per request.

```csharp
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

var session = new InferenceSession("model.onnx");
// Register as singleton in DI
```

### Step A3: Prepare Input Tensors

- Create `DenseTensor<float>` with the correct shape.
- Wrap in `NamedOnnxValue.CreateFromTensor("input_name", tensor)`.
- For detailed tensor operations, read `references/tensors.md`.

### Step A4: Run Inference

```csharp
var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor("input", inputTensor) };
using var results = session.Run(inputs);
var output = results.First().AsTensor<float>();
```

### Step A5: Configure Execution Providers

Only if you need GPU or specialized hardware acceleration:

```csharp
var options = new SessionOptions();
// IF CUDA: options.AppendExecutionProvider_CUDA();
// IF DirectML: options.AppendExecutionProvider_DML();
// IF TensorRT: options.AppendExecutionProvider_Tensorrt(new());
var session = new InferenceSession("model.onnx", options);
```

---

## Approach B: ML.NET + ONNX Integration

### Step B1: Install Packages

- `Microsoft.ML`
- `Microsoft.ML.OnnxRuntime`
- `Microsoft.ML.OnnxTransformer`

### Step B2: Build Pipeline with ONNX Scoring

```csharp
var mlContext = new MLContext(seed: 0);
var pipeline = mlContext.Transforms.ApplyOnnxModel(
    modelFile: "model.onnx",
    outputColumnNames: new[] { "output" },
    inputColumnNames: new[] { "input" });
var model = pipeline.Fit(emptyData);
```

### Step B3: Combine with ML.NET Transforms

Add tokenization, normalization, or post-processing transforms before/after ONNX scoring in the pipeline.

### Text Preprocessing for ONNX Models

When running NLP ONNX models (BERT, DistilBERT, MiniLM), use `Microsoft.ML.Tokenizers` to convert text to token IDs before inference:

```
dotnet add package Microsoft.ML.Tokenizers
```

```csharp
var tokenizer = BertTokenizer.Create("vocab.txt");
IReadOnlyList<int> ids = tokenizer.EncodeToIds(text);
// Feed ids into input tensor (see references/tensors.md)
```

---

## Validation

- Model loads without errors.
- Inference produces output tensors with expected shape.
- Execution provider correctly selected.
- Singleton pattern used for `InferenceSession`.

## Pitfalls

- **Creating InferenceSession per request** — Expensive; use singleton.
- **Wrong input tensor shape** — Check model's expected input with `session.InputMetadata`.
- **Not matching preprocessing to what the model expects** — Verify normalization, channel order, etc.
- **Missing execution provider packages** — Falls back to CPU silently.
- **Not disposing `IDisposableReadOnlyCollection` from `session.Run`** — Always use `using`.

## More Info

- https://onnxruntime.ai/docs/get-started/with-csharp.html
