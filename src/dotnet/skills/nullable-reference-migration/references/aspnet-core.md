# ASP.NET Core Considerations

ASP.NET Core reads nullable annotations at runtime to drive model validation and serialization behavior. Enabling NRTs in an ASP.NET Core project can change request validation outcomes, not just compiler warnings:

- **MVC model validation treats non-nullable properties as `[Required]`**: When NRTs are enabled, ASP.NET Core MVC and Web API implicitly add `[Required(AllowEmptyStrings = true)]` to every non-nullable reference type property in DTOs and view models. A `string Name` property that previously accepted null from JSON or form posts will now return a 400 Bad Request. Review all model classes when enabling NRTs. To disable this behavior during gradual migration, set `SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true` in `AddControllers` options.
- **`System.Text.Json` can enforce NRT annotations (.NET 9+)**: Setting `JsonSerializerOptions.RespectNullableAnnotations = true` (opt-in) makes the serializer enforce nullable annotations during both serialization and deserialization — throwing `JsonException` when a non-nullable property receives an explicit `null` during deserialization, or emits `null` for a non-nullable property during serialization. However, this enforcement has hard limitations rooted in how NRTs are represented in IL. It does **not** cover:
  - Collection element types (`List<string>` and `List<string?>` are indistinguishable via reflection)
  - Dictionary value types (`Dictionary<string, string>` vs `Dictionary<string, string?>`)
  - Top-level types passed directly to `Deserialize<T>`
  - Generic type parameter nullability

  For these gaps, use manual validation or custom converters. Do not assume `RespectNullableAnnotations` provides complete null safety for your JSON layer.
- **Use `#nullable disable`, not `#nullable disable warnings` on model files**: Just as with EF Core, `#nullable disable warnings` only suppresses compiler diagnostics — the annotations remain active and MVC still reads them via reflection to infer `[Required]`. Use `#nullable disable` to fully opt out for files not yet migrated.
