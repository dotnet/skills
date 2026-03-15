# TorchSharp — Custom Neural Networks in .NET

Read this reference when the user needs to go beyond ML.NET's built-in trainers — custom neural network architectures, training loops, or PyTorch-style development in C#/F#.

## What TorchSharp Is

TorchSharp is a standalone .NET library providing bindings to PyTorch's LibTorch C++ backend. It exposes PyTorch's tensor operations, neural network modules, optimizers, and data loading to .NET developers. It is **not part of ML.NET** — though ML.NET's deep learning trainers (image classification, object detection, NER, QA, text classification, sentence similarity) are powered by TorchSharp internally.

## When to Use TorchSharp Directly (vs ML.NET)

| Scenario | Use |
|---|---|
| Standard tasks (image classification, object detection, NER, QA, text classification) | ML.NET's TorchSharp-backed trainers (high-level pipeline API) |
| Custom neural network architectures | TorchSharp directly |
| Research / experimentation with model design | TorchSharp directly |
| Loading TorchScript models exported from Python | TorchSharp directly (`torch.jit`) |
| Computer vision utilities and pre-trained vision models | `TorchSharp.PyBridge` + `TorchVision` |
| Audio processing and models | `TorchAudio` |

## Install

```
dotnet add package TorchSharp
```

Add a LibTorch runtime backend (pick one):

| Scenario | Package |
|---|---|
| CPU only | `dotnet add package libtorch-cpu` |
| CUDA 11.x GPU | `dotnet add package libtorch-cuda-11.8` |
| CUDA 12.x GPU | `dotnet add package libtorch-cuda-12.1` |

> ⚠️ The `libtorch` packages are large (~2GB for CUDA). CPU-only is sufficient for inference and small-scale training.

## Define a Custom Model

Inherit from `torch.nn.Module` to define custom architectures:

```csharp
using TorchSharp;
using static TorchSharp.torch;
using static TorchSharp.torch.nn;

public class SimpleClassifier : Module<Tensor, Tensor>
{
    private readonly Module<Tensor, Tensor> layers;

    public SimpleClassifier(int inputSize, int hiddenSize, int numClasses)
        : base("SimpleClassifier")
    {
        layers = Sequential(
            Linear(inputSize, hiddenSize),
            ReLU(),
            Dropout(0.5),
            Linear(hiddenSize, numClasses));

        RegisterComponents();
    }

    public override Tensor forward(Tensor input)
    {
        return layers.forward(input);
    }
}
```

> ⚠️ ALWAYS call `RegisterComponents()` in the constructor. Without it, parameters won't be tracked by the optimizer.

## Training Loop

TorchSharp uses an explicit training loop (PyTorch-style), not ML.NET's `pipeline.Fit()` pattern:

```csharp
var model = new SimpleClassifier(inputSize: 784, hiddenSize: 128, numClasses: 10);
var optimizer = torch.optim.Adam(model.parameters(), lr: 0.001);
var loss_fn = torch.nn.CrossEntropyLoss();

for (int epoch = 0; epoch < numEpochs; epoch++)
{
    model.train();
    foreach (var (data, target) in trainLoader)
    {
        optimizer.zero_grad();
        var output = model.forward(data);
        var loss = loss_fn.forward(output, target);
        loss.backward();
        optimizer.step();
    }
}
```

## Load TorchScript Models (From Python)

Load models exported from Python via `torch.jit.save()` without needing a Python runtime:

```csharp
var model = torch.jit.load("model.pt");
model.eval();

using var input = torch.randn(1, 3, 224, 224);
using var output = model.forward(input);
```

## Save and Load .NET Models

```csharp
// Save
model.save("model_weights.dat");

// Load
var loaded = new SimpleClassifier(784, 128, 10);
loaded.load("model_weights.dat");
```

## Relationship to ML.NET

- ML.NET's `Microsoft.ML.TorchSharp` package uses TorchSharp internally to provide high-level trainers (image classification, object detection, NER, QA, text classification, sentence similarity).
- If the built-in ML.NET trainers cover your task, prefer them — they integrate with the ML.NET pipeline API, AutoML, and Model Builder.
- Use TorchSharp directly when you need full control over the model architecture, training loop, or loss function.

## Key Namespaces

| Namespace | Purpose |
|---|---|
| `TorchSharp.torch` | Core tensor operations, device management |
| `TorchSharp.torch.nn` | Neural network modules (Linear, Conv2d, ReLU, etc.) |
| `TorchSharp.torch.optim` | Optimizers (Adam, SGD, etc.) |
| `TorchSharp.torch.jit` | TorchScript model loading |
| `TorchSharp.torch.utils.data` | Dataset and DataLoader abstractions |

## More Information

- <https://github.com/dotnet/TorchSharp>
- <https://github.com/dotnet/TorchSharpExamples>
