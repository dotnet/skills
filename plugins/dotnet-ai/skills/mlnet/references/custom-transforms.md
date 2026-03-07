# Building Custom ML.NET Transforms

Four approaches to extending ML.NET pipelines with custom `IEstimator<ITransformer>` / `ITransformer` implementations. Use this reference when the agent needs to add domain-specific transforms such as embeddings, encoders, or feature engineering steps.

## Key Problem

ML.NET's internal base classes (`RowToRowTransformerBase`, `OneToOneTransformerBase`) have `private protected` constructors — they are **inaccessible from external projects**. You cannot subclass them outside the ML.NET repository.

## Decision Tree

```
Need a custom transform?
├─ Prototyping / simple logic? ──────────────────────► Approach A (CustomMapping Lambda)
├─ Production code, external project? ───────────────► Approach B (Facade + CustomMapping)
├─ CustomMapping POCO binding too limited? ──────────► Approach C (Direct IEstimator/ITransformer)
└─ Contributing to ML.NET itself / using a fork? ───► Approach D (Source Contribution)
```

---

## Approach A — CustomMapping Lambda (Quick Start)

Minimal code. Good for prototyping. Preserves lazy evaluation.

```csharp
var pipeline = mlContext.Transforms.CustomMapping<InputRow, OutputRow>(
    (input, output) =>
    {
        output.TransformedFeature = input.RawFeature * 2.0f;
    },
    contractName: "MyTransform");
```

**Advantages:** Single line to add to a pipeline, preserves ML.NET's lazy row-by-row evaluation.

**Limitations:** No clean public API surface, no resource lifecycle management, limited save/load support without a `CustomMappingFactory`.

---

## Approach B — Production Facade + CustomMapping (Recommended)

Wrap `CustomMapping` inside a proper `IEstimator<ITransformer>` facade. Best balance of API quality and framework integration for external projects.

```csharp
public class MyTransformEstimator : IEstimator<ITransformer>
{
    private readonly MLContext _mlContext;
    private readonly Lazy<ExpensiveResource> _resource;

    public MyTransformEstimator(MLContext mlContext, string modelPath)
    {
        _mlContext = mlContext ?? throw new ArgumentNullException(nameof(mlContext));
        // Lazy<T> ensures resource is loaded once, thread-safe by default
        _resource = new Lazy<ExpensiveResource>(() => ExpensiveResource.Load(modelPath));
    }

    public ITransformer Fit(IDataView input)
    {
        var resource = _resource.Value;
        var pipeline = _mlContext.Transforms.CustomMapping<InputRow, OutputRow>(
            (src, dst) =>
            {
                dst.Embedding = resource.Encode(src.Text);
            },
            contractName: "MyTransform");
        return pipeline.Fit(input);
    }

    public SchemaShape GetOutputSchema(SchemaShape inputSchema)
    {
        // Validate input schema and describe output
        return inputSchema;
    }
}
```

**Save/Load pattern** — register a `CustomMappingFactory<TSrc, TDst>`:

```csharp
[CustomMappingFactoryAttribute("MyTransform")]
public class MyTransformFactory : CustomMappingFactory<InputRow, OutputRow>
{
    public override Action<InputRow, OutputRow> GetMapping()
    {
        return (input, output) =>
        {
            output.Embedding = DefaultResource.Encode(input.Text);
        };
    }
}
```

**Thread safety:** `Lazy<T>` is thread-safe by default (`LazyThreadSafetyMode.ExecutionAndPublication`). Safe for concurrent prediction engines.

---

## Approach C — Direct IEstimator/ITransformer

Implement `IEstimator<ITransformer>` and `ITransformer` from scratch. Use when `CustomMapping` POCO binding is insufficient — for example, dynamic schemas, multi-column mapping, or variable-length output.

```csharp
public class MyTransformer : ITransformer
{
    public bool IsRowToRowMapper => true;

    public DataViewSchema GetOutputSchema(DataViewSchema inputSchema)
    {
        var builder = new DataViewSchema.Builder();
        builder.AddColumns(inputSchema);
        builder.AddColumn("NewFeature", NumberDataViewType.Single);
        return builder.ToSchema();
    }

    public IRowToRowMapper GetRowToRowMapper(DataViewSchema inputSchema)
    {
        throw new NotImplementedException("Implement for row-by-row mapping");
    }

    public IDataView Transform(IDataView input)
    {
        // Materialize and transform data
        // WARNING: This loses lazy evaluation — all rows are read into memory
        return new MyDataView(input);
    }

    public void Save(ModelSaveContext ctx)
    {
        // Serialize model parameters
    }
}
```

**Advantages:** Maximum flexibility, no POCO binding constraints.

**Disadvantages:** Loses lazy row-by-row evaluation (materializes data), significantly more code, must handle schema propagation manually.

---

## Approach D — Source Contribution (Internal)

Subclass `RowToRowTransformerBase` or `OneToOneTransformerBase` directly. **Only works inside the ML.NET repository or a fork.**

```csharp
// Only compiles inside dotnet/machinelearning repo
internal sealed class MyInternalTransform : RowToRowTransformerBase
{
    public MyInternalTransform(IHostEnvironment env)
        : base(env, nameof(MyInternalTransform))
    {
    }

    // Full framework integration: ONNX export, zero-copy data access
    protected override IRowMapper MakeRowMapper(DataViewSchema schema)
    {
        return new Mapper(this, schema);
    }
}
```

**Advantages:** Full framework integration, ONNX export support, zero-copy data access via cursors.

**Use for:** Contributing transforms upstream to the `dotnet/machinelearning` repository.
