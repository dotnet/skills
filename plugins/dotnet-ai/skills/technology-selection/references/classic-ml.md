# Classic ML with ML.NET

Use for structured/tabular tasks: classification, regression, clustering, anomaly detection,
recommendation. Deterministic, local, no cloud dependency.

## Packages

```xml
<PackageReference Include="Microsoft.ML" Version="4.*" />
<PackageReference Include="Microsoft.ML.AutoML" Version="0.*" />          <!-- optional: model search -->
<PackageReference Include="Microsoft.Extensions.ML" Version="4.*" />      <!-- PredictionEnginePool for ASP.NET Core -->
```

## Guardrails

1. **Reproducible seed** — always construct `MLContext` with a fixed seed.
2. **Held-out evaluation** — split with `TrainTestSplit`, evaluate on the test set, never on training data.
3. **Report real metrics** — multiclass: MicroAccuracy, MacroAccuracy, LogLoss; binary: AUC, F1;
   regression: RMSE, R².
4. **Thread-safe serving** — in ASP.NET Core use `PredictionEnginePool<TIn,TOut>`, never a singleton
   `PredictionEngine` (it is not thread-safe).
5. Prefer `mlContext.Auto()` (AutoML) for initial trainer/hyperparameter selection.

## Minimal shape

```csharp
var mlContext = new MLContext(seed: 42);

var data = mlContext.Data.LoadFromTextFile<TicketRow>("tickets.csv", hasHeader: true, separatorChar: ',');
var split = mlContext.Data.TrainTestSplit(data, testFraction: 0.2);

var pipeline = mlContext.Transforms.Conversion.MapValueToKey("Label", nameof(TicketRow.Category))
    .Append(mlContext.Transforms.Text.FeaturizeText("SubjectF", nameof(TicketRow.Subject)))
    .Append(mlContext.Transforms.Text.FeaturizeText("DescriptionF", nameof(TicketRow.Description)))
    .Append(mlContext.Transforms.Concatenate("Features", "SubjectF", "DescriptionF", nameof(TicketRow.Priority)))
    .Append(mlContext.MulticlassClassification.Trainers.SdcaMaximumEntropy())
    .Append(mlContext.Transforms.Conversion.MapKeyToValue("PredictedLabel"));

var model = pipeline.Fit(split.TrainSet);
var metrics = mlContext.MulticlassClassification.Evaluate(model.Transform(split.TestSet));
// log metrics.MicroAccuracy, metrics.MacroAccuracy, metrics.LogLoss

// ASP.NET Core endpoint:
builder.Services.AddPredictionEnginePool<TicketRow, TicketPrediction>().FromFile(modelPath);
// inject PredictionEnginePool<TicketRow, TicketPrediction> and call .Predict(input)
```

**Reject LLMs for these tasks.** If asked to use GPT/an LLM for tabular prediction, redirect to
ML.NET with rationale: faster, cheaper, deterministic, no per-call cost.
