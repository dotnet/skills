# schemagen

Generates C# classes from JSON schemas. Every process start loads the type catalog, which takes about 1.4 s
no matter how many schemas are generated.

```
schemagen --input <schema.json> --output <file.g.cs>
schemagen --input-list <list.txt> --output-dir <dir>
```

- `--input` converts one schema into one file.
- `--input-list` reads one schema path per line and writes `<SchemaName>.g.cs` for each into `--output-dir`.
  The output is identical to running `--input` once per schema.
