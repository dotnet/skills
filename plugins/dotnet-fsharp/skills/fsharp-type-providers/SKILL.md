---
name: fsharp-type-providers
description: "Use F# type providers to get strongly-typed access to external data (JSON, CSV, XML, HTML, SQL) without hand-writing types. Use when reading structured data from a sample or live source and you want compile-time-checked properties instead of stringly-typed parsing. Covers FSharp.Data providers (JsonProvider, CsvProvider, HtmlProvider, XmlProvider), referencing them from scripts and projects, sample vs live schemas, and runtime caveats. Do not use for trivial one-field parsing, or when AOT/trimming forbids the provider's generated code."
license: MIT
---

# F# Type Providers

## Purpose

A type provider generates types at compile time from an external schema or sample, so you can
read JSON/CSV/XML/SQL with dotted, IntelliSense-backed property access instead of manual,
error-prone parsing.

## When to Use

- Reading structured external data (JSON, CSV, XML, HTML, a database)
- You have a representative sample or a live endpoint to infer the shape from
- You want compile-time-checked access to fields rather than string keys

## When Not to Use

- Trivial parsing of one or two fields (plain parsing is simpler)
- Native AOT / aggressive trimming contexts where provider-generated code is unsupported
- A fixed, well-known contract you would rather model explicitly (see `fsharp-domain-modeling`)

## FSharp.Data providers

Add the `FSharp.Data` package (`#r "nuget: FSharp.Data"` in a script - see `fsharp-scripts`).

### JsonProvider

```fsharp
#r "nuget: FSharp.Data"
open FSharp.Data

type Weather = JsonProvider<""" { "city": "Oslo", "tempC": 12.5 } """>

let sample = Weather.Parse(""" { "city": "Bergen", "tempC": 9.0 } """)
printfn "%s is %f C" sample.City sample.TempC     // strongly typed
```

The string literal is a **sample** used only to infer the type; real data is parsed at runtime.
You can also point at a file or URL: `JsonProvider<"sample.json">` or
`JsonProvider<"https://...">`.

### CsvProvider

```fsharp
type Stocks = CsvProvider<"Date,Open,Close\n2020-01-01,100.0,101.5">

let data = Stocks.Load("stocks.csv")
for row in data.Rows do
    printfn "%A closed at %f" row.Date row.Close
```

### XmlProvider / HtmlProvider

`XmlProvider<...>` and `HtmlProvider<...>` work the same way: give a sample (literal, file, or
URL); access elements/tables as typed members.

## Workflow

1. Reference `FSharp.Data` (`#r "nuget:"` in a script, or a package reference in a project).
2. Pick the provider for the format (Json/Csv/Xml/Html).
3. Provide a representative sample (literal, file path, or URL) to the provider type.
4. Parse/Load real data and access fields through the generated typed members.
5. Verify with `dotnet fsi` against actual data.

## Validation

- [ ] The right provider is used for the data format
- [ ] A representative sample defines the schema
- [ ] Fields are accessed via generated typed members, not string keys
- [ ] Real data parses and reads correctly (`dotnet fsi`)
- [ ] AOT/trimming constraints checked if the project targets them

## Common Pitfalls

| Pitfall | Correction |
|---------|------------|
| Sample not representative of real data | Use a sample covering all fields and nullability |
| Expecting providers under native AOT | Provider-generated types are generally unsupported there |
| Treating the sample as the data | The sample only infers types; parse/load the real source |
| Huge live sample fetched at build time | Prefer a small local sample file for reproducible builds |

## More info

- Type providers: https://learn.microsoft.com/en-us/dotnet/fsharp/tutorials/type-providers/
- FSharp.Data: https://fsprojects.github.io/FSharp.Data/
