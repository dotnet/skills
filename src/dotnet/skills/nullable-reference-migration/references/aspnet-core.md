# ASP.NET Core Considerations

ASP.NET Core reads nullable annotations at runtime to drive model validation and serialization behavior. Enabling NRTs in an ASP.NET Core project can change request validation outcomes, not just compiler warnings:

- **MVC model validation treats non-nullable properties as `[Required]`**: When NRTs are enabled, ASP.NET Core MVC and Web API implicitly add `[Required(AllowEmptyStrings = true)]` to every non-nullable reference type property in DTOs and view models. A `string Name` property that previously accepted null from JSON or form posts will now return a 400 Bad Request. Review all model classes when enabling NRTs. To disable this behavior during gradual migration, set `SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true` in `AddControllers` options.
- **Enable `JsonSerializerOptions.RespectNullableAnnotations = true` (.NET 9+)**: For .NET 9+ projects, always enable `RespectNullableAnnotations` (along with `RespectRequiredConstructorParameters`) to align runtime serialization behavior with your NRT annotations. Without this, `System.Text.Json` silently assigns `null` to non-nullable properties, undermining compile-time null safety. When enabled, the serializer throws `JsonException` when a non-nullable property receives an explicit `null` during deserialization, or emits `null` for a non-nullable property during serialization. Be aware this enforcement has hard limitations rooted in how NRTs are represented in IL. It does **not** cover:
  - Collection element types (`List<string>` and `List<string?>` are indistinguishable via reflection)
  - Dictionary value types (`Dictionary<string, string>` vs `Dictionary<string, string?>`)
  - Top-level types passed directly to `Deserialize<T>`
  - Generic type parameter nullability

  For these gaps, use manual validation or custom converters. Do not rely on `RespectNullableAnnotations` alone for complete null safety in your JSON layer.
- **Use `#nullable disable`, not `#nullable disable warnings` on model files**: Just as with EF Core, `#nullable disable warnings` only suppresses compiler diagnostics — the annotations remain active and MVC still reads them via reflection to infer `[Required]`. Use `#nullable disable` to fully opt out for files not yet migrated.
