---
name: mlnet
description: >
  USE FOR: Classical ML on structured data (classification, regression, clustering, anomaly
  detection, recommendation, time-series forecasting). Deep learning tasks (image classification,
  object detection, NER, QA, text classification, sentence similarity) via TorchSharp/TensorFlow
  integration. Also for extending ML.NET pipelines with custom transforms.
  DO NOT USE FOR: Natural language generation/understanding with LLMs (use meai-chat-integration),
  running pre-trained ONNX models standalone without ML.NET (use onnx-runtime-inference).
  For custom neural network architectures beyond what ML.NET trainers provide, use
  TorchSharp directly (see references/torchsharp.md).
---

# ML.NET Model Training & Deployment

Train and deploy ML.NET models for classical ML and deep learning tasks in .NET.

## What ML.NET Covers

| Category | Tasks | Powered By |
|---|---|---|
| **Classical ML** | Classification, regression, clustering, anomaly detection, recommendation, time-series forecasting | Built-in trainers (SDCA, LightGBM, FastTree, etc.) |
| **Deep Learning** | Image classification, object detection, NER, QA, text classification, sentence similarity | TorchSharp (`Microsoft.ML.TorchSharp`) |
| **Pre-trained Models** | TensorFlow model scoring, ONNX model scoring | TensorFlow.NET (⚠️ pinned to TF 2.3.1), ONNX Runtime |
| **AutoML** | Automated model/hyperparameter selection across all supported tasks | `Microsoft.ML.AutoML` |

## Inputs

| Input | Description |
|-------|-------------|
| Task description | What the model should predict or detect |
| Data description | CSV file, database source, or in-memory collection |
| Target column | The column the model predicts (label) |
| Existing project context | Current solution, target framework, existing dependencies |

## Workflow

### Step 1 — Install Packages

Install the core package and any task-specific packages:

```
dotnet add package Microsoft.ML
```

- **IF recommendation:** also `dotnet add package Microsoft.ML.Recommender`
- **IF time-series:** also `dotnet add package Microsoft.ML.TimeSeries`
- **IF image classification, object detection, NER, QA, text classification, or sentence similarity:** also `dotnet add package Microsoft.ML.TorchSharp` and the appropriate `libtorch` runtime package (e.g., `libtorch-cpu` or `libtorch-cuda-12.1`)
- **IF consuming a TensorFlow model:** also `dotnet add package Microsoft.ML.TensorFlow` — ⚠️ TensorFlow support in ML.NET is pinned to TF 2.3.1 (via TensorFlow.NET 0.20.1). This works for scoring legacy TF models but may not support models from newer TF versions. For new deep learning work, prefer the TorchSharp-backed trainers above.
- **IF consuming an ONNX model within ML.NET:** also `dotnet add package Microsoft.ML.OnnxTransformer`
- **IF text featurization for classical ML:** included in the core `Microsoft.ML` package

### Step 2 — Create MLContext

ALWAYS create with a seed for reproducibility:

```csharp
var mlContext = new MLContext(seed: 0);
```

### Step 3 — Load Data

**IF CSV:**

```csharp
IDataView data = mlContext.Data.LoadFromTextFile<ModelInput>(
    path: "data.csv",
    hasHeader: true,
    separatorChar: ',');
```

**IF DataFrame needed for complex data preparation:** read `references/dataframe.md` before proceeding.

**IF in-memory collection:**

```csharp
IDataView data = mlContext.Data.LoadFromEnumerable(records);
```

### Step 4 — Select Task & Build Pipeline

Choose the trainer based on the task:

**Classical ML (structured data)**

| Task | Trainer |
|------|---------|
| Binary yes/no classification | `mlContext.BinaryClassification.Trainers.SdcaLogisticRegression()` |
| Multi-category classification | `mlContext.MulticlassClassification.Trainers.SdcaMaximumEntropy()` |
| Numeric prediction (regression) | `mlContext.Regression.Trainers.Sdca()` |
| Grouping (clustering) | `mlContext.Clustering.Trainers.KMeans(numberOfClusters: N)` |
| Anomaly detection | `mlContext.AnomalyDetection.Trainers.RandomizedPca()` |
| Recommendation | `mlContext.Recommendation().Trainers.MatrixFactorization()` |
| Time-series forecasting | `mlContext.Forecasting.ForecastBySsa()` |

**Deep Learning (TorchSharp-backed)**

| Task | Trainer | Package |
|------|---------|---------|
| Image classification | `mlContext.MulticlassClassification.Trainers.ImageClassification()` | `Microsoft.ML.TorchSharp` |
| Object detection | `mlContext.MulticlassClassification.Trainers.ObjectDetection()` | `Microsoft.ML.TorchSharp` |
| Text classification | `mlContext.MulticlassClassification.Trainers.TextClassification()` | `Microsoft.ML.TorchSharp` |
| Named entity recognition | `mlContext.MulticlassClassification.Trainers.NamedEntityRecognition()` | `Microsoft.ML.TorchSharp` |
| Question answering | `mlContext.MulticlassClassification.Trainers.QuestionAnswer()` | `Microsoft.ML.TorchSharp` |
| Sentence similarity | `mlContext.Regression.Trainers.SentenceSimilarity()` | `Microsoft.ML.TorchSharp` |

Prepend transforms to the pipeline before the trainer:

```csharp
var pipeline = mlContext.Transforms.Text.FeaturizeText("TextFeatures", "TextColumn")
    .Append(mlContext.Transforms.Categorical.OneHotEncoding("CategoryEncoded", "CategoryColumn"))
    .Append(mlContext.Transforms.Concatenate("Features", "TextFeatures", "CategoryEncoded", "NumericColumn"))
    .Append(mlContext.Transforms.NormalizeMinMax("Features"))
    .Append(trainer);
```

Use text featurization (`FeaturizeText`), one-hot encoding, concatenation into a single `Features` column, and normalization as needed.

### Step 5 — Train

```csharp
var model = pipeline.Fit(trainingData);
```

### Step 6 — Evaluate

Split data into training and test sets:

```csharp
var split = mlContext.Data.TrainTestSplit(data, testFraction: 0.2);
var model = pipeline.Fit(split.TrainSet);
var predictions = model.Transform(split.TestSet);
```

Use task-appropriate metrics:

| Task | Metric call | Key metric |
|------|-------------|------------|
| Binary classification | `mlContext.BinaryClassification.Evaluate(predictions)` | Accuracy, AUC |
| Multiclass classification | `mlContext.MulticlassClassification.Evaluate(predictions)` | MicroAccuracy, LogLoss |
| Regression | `mlContext.Regression.Evaluate(predictions)` | RSquared, MAE |
| Clustering | `mlContext.Clustering.Evaluate(predictions)` | AverageDistance |
| Anomaly detection | `mlContext.AnomalyDetection.Evaluate(predictions)` | AUC |

### Step 7 — Save & Use

Save the trained model:

```csharp
mlContext.Model.Save(model, trainingData.Schema, "model.zip");
```

Create a prediction engine for inference:

```csharp
var engine = mlContext.Model.CreatePredictionEngine<ModelInput, ModelOutput>(model);
var result = engine.Predict(new ModelInput { /* ... */ });
```

> ⚠️ `PredictionEngine` is NOT thread-safe. In ASP.NET Core, use `PredictionEnginePool`:

```csharp
// In Startup/Program.cs:
builder.Services.AddPredictionEnginePool<ModelInput, ModelOutput>()
    .FromFile("model.zip");

// In a controller/service:
public class PredictionController(PredictionEnginePool<ModelInput, ModelOutput> pool)
{
    public ModelOutput Predict(ModelInput input) => pool.Predict(input);
}
```

Pre-warm the pool at startup to avoid cold-start latency on the first request.

### Step 8 — Custom Transforms (Conditional)

IF the user needs a custom pipeline step (domain-specific featurizer, external embedding lookup, custom encoder), read `references/custom-transforms.md` before proceeding.

### Step 9 — Custom Neural Networks with TorchSharp (Conditional)

IF the user needs a custom neural network architecture beyond what ML.NET's built-in trainers provide, read `references/torchsharp.md` before proceeding. TorchSharp provides full PyTorch bindings for .NET and can be used standalone or alongside ML.NET.

## Validation

- [ ] Model trains without errors
- [ ] Evaluation metrics are reasonable for the task
- [ ] Prediction engine produces output on sample input
- [ ] Model saved to disk (`model.zip` exists)

## Pitfalls

- **Not setting MLContext seed** — results become non-reproducible across runs.
- **Using an LLM for classification on structured data** — slower and more expensive than ML.NET for tasks like classification, regression, and clustering on structured data.
- **Not evaluating on a held-out test set** — overfitting goes undetected.
- **Forgetting to concatenate features** — all feature columns must be combined into a single `Features` column before training.
- **Not normalizing numeric features** — distance-based algorithms (KMeans, PCA) perform poorly on unnormalized data.
- **Using Accord.NET for new projects** — Accord.NET is archived and unmaintained. Use ML.NET instead.

## More Info

https://learn.microsoft.com/dotnet/machine-learning/
