# DataFrame for Data Preparation in ML.NET

Use `Microsoft.Data.Analysis.DataFrame` for loading and preparing tabular data before ML.NET training. Read this reference when the user has complex data preparation needs (filtering, grouping, column transforms, missing value handling).

## Package

```
dotnet add package Microsoft.Data.Analysis
```

## Loading Data

```csharp
// From CSV
DataFrame df = DataFrame.LoadCsv("data.csv");

// Programmatic construction
var nameColumn = new StringDataFrameColumn("Name", new[] { "Alice", "Bob" });
var ageColumn = new Int32DataFrameColumn("Age", new[] { 30, 25 });
var df = new DataFrame(nameColumn, ageColumn);
```

## Inspection

```csharp
df.Head(5);          // First 5 rows
df.Description();    // Summary statistics per column
df.Info();           // Column names, types, non-null counts
df.Rows.Count;       // Row count
df.Columns.Count;    // Column count
```

## Filtering

```csharp
DataFrame filtered = df.Filter(df["Age"].ElementwiseGreaterThan(18));
DataFrame subset = df.Filter(df["Category"].ElementwiseEquals("A"));
```

## Column Transforms

```csharp
// Arithmetic
df["Total"] = df["Price"].Multiply(df["Quantity"]);
df["Normalized"] = df["Value"].Subtract(min).Divide(max - min);

// Add / remove columns
df.Columns.Add(new Int32DataFrameColumn("NewCol", df.Rows.Count));
df.Columns.Remove("UnneededColumn");
```

## Missing Values

```csharp
// Fill with default
df["Age"].FillNulls(0, inPlace: true);

// Drop rows with any null
DataFrame clean = df.DropNulls();
```

## Grouping & Aggregation

```csharp
DataFrame grouped = df.GroupBy("Category").Sum("Amount");
DataFrame counts = df.GroupBy("Region").Count();
```

## ML.NET Integration

`DataFrame` implements `IDataView` directly — pass it straight to ML.NET pipelines without conversion:

```csharp
var mlContext = new MLContext(seed: 0);

// Use DataFrame as IDataView
var split = mlContext.Data.TrainTestSplit(df, testFraction: 0.2);
var model = pipeline.Fit(split.TrainSet);
```
