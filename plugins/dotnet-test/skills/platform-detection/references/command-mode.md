# `dotnet test` Command Mode

Read this reference only when the user explicitly asks for command mode or
supplies a `dotnet test mode:` output label.

Command mode and executed test platform are separate axes. Command mode controls
CLI syntax; VSTest mode can still bridge to an MTP test application.

## Selection

- SDK 10+ with `global.json` `"test": { "runner":
  "Microsoft.Testing.Platform" }` selects native MTP mode.
- SDK 10+ with runner `VSTest` or no `test` section selects VSTest mode.
- SDK 8/9 supports only VSTest mode.

Report the requested axes separately:

```text
dotnet test mode: VSTest
Platform: MTP
Framework: MSTest
```

A complete VSTest-to-MTP bridge still reports `dotnet test mode: VSTest` and
`Platform: MTP`. Native MTP reports `dotnet test mode: native MTP`. If native
MTP selection conflicts with a VSTest opt-out or incompatible project shape,
report `Platform: unavailable` rather than inventing successful execution.
