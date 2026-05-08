# ASP.NET Core Breaking Changes (.NET 11)

These breaking changes affect ASP.NET Core projects. Source: https://learn.microsoft.com/en-us/dotnet/core/compatibility/11

> **Note:** .NET 11 is in preview. Additional ASP.NET Core breaking changes are expected in later previews.

## Source-Incompatible Changes

### Microsoft.OpenApi updated to v3 with OpenAPI 3.2.0 support (Preview 2)

**Impact: Medium.** `Microsoft.AspNetCore.OpenApi` updated its dependency from `Microsoft.OpenApi` 2.x to 3.x, adding OpenAPI 3.2.0 document generation. The underlying `Microsoft.OpenApi` library has breaking API changes in the v2→v3 transition.

Code that directly uses `Microsoft.OpenApi` types (`OpenApiDocument`, `OpenApiSchema`, `OpenApiOperation`, etc.) will have compile errors.

**Fix:** Follow the [Microsoft.OpenApi v3 upgrade guide](https://github.com/microsoft/OpenAPI.NET/blob/main/docs/upgrade-guide-3.md). If you only use the ASP.NET Core OpenAPI integration (`.WithOpenApi()`, `MapOpenApi()`) without touching the object model directly, no changes are needed.

Source: https://github.com/dotnet/aspnetcore/pull/65415

## Behavioral Changes

### Blazor Virtualize&lt;T&gt; default OverscanCount changed from 3 to 15 (Preview 3)

**Impact: Low.** The default `OverscanCount` on the `Virtualize<TItem>` component changed from `3` to `15` to support variable-height item measurement. `QuickGrid` retains its own default of `3`.

**Fix:** If performance-sensitive, set `OverscanCount` explicitly: `<Virtualize OverscanCount="3" />`.

Source: https://github.com/dotnet/aspnetcore/pull/64964
