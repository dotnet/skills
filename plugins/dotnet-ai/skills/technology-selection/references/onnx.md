# ONNX Runtime inference

Use when a trained model is already available in ONNX format and the .NET application only needs
production inference. Train or convert the model outside the application.

## Package

```xml
<PackageReference Include="Microsoft.ML.OnnxRuntime" Version="1.*" />
```

Use the GPU-specific package only when the deployment target and execution provider require it.

## Guardrails

1. Validate model input names, element types, dimensions, and output names at startup.
2. Create and warm one `InferenceSession` through DI; do not reload the model per request.
3. Normalize and tokenize input exactly as the model expects.
4. Dispose inference results and other native-memory-backed values promptly.
5. Bound input sizes and batch sizes, and measure latency and memory on the deployment hardware.
6. Pin and record the model artifact version with its preprocessing contract.

## Minimal shape

```csharp
builder.Services.AddSingleton(_ => new InferenceSession(modelPath));

var input = NamedOnnxValue.CreateFromTensor("input", tensor);
using var results = session.Run([input]);
```
