---
name: implementing-json-patch-aspnetcore
description: Implement HTTP PATCH with JSON Patch (RFC 6902) in ASP.NET Core. Use when adding partial update endpoints using JSON Patch operations.
---

# Implementing JSON Patch in ASP.NET Core

## When to Use
- Implementing HTTP PATCH endpoints with RFC 6902 JSON Patch operations
- Partial resource updates where clients send only changed fields
- APIs following REST conventions that need PATCH support

## When Not to Use
- Simple property-level updates → use a DTO with nullable fields
- Full resource replacement → use PUT
- If your API only uses System.Text.Json and cannot add Newtonsoft dependency

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Target model type | Yes | The entity/DTO type to patch |
| Patch operations | Yes | JSON Patch document from client |
| Validation rules | No | Model validation after applying patch |

## Workflow

### Step 1: CRITICAL — JSON Patch Requires Newtonsoft.Json, NOT System.Text.Json

```csharp
// CRITICAL: System.Text.Json does NOT support JsonPatchDocument
// You MUST use Microsoft.AspNetCore.Mvc.NewtonsoftJson

// COMMON MISTAKE: Trying to use System.Text.Json
// using System.Text.Json;
// JsonSerializer.Deserialize<JsonPatchDocument<Product>>(body); // FAILS - not supported

// CORRECT: Install the Newtonsoft.Json package
// dotnet add package Microsoft.AspNetCore.Mvc.NewtonsoftJson

// CRITICAL: Register Newtonsoft.Json in Program.cs
builder.Services.AddControllers()
    .AddNewtonsoftJson();  // REQUIRED for JsonPatchDocument deserialization

// COMMON MISTAKE: Forgetting AddNewtonsoftJson() — JsonPatchDocument will be null/empty
// builder.Services.AddControllers(); // Missing AddNewtonsoftJson!
```

### Step 2: CRITICAL — Content-Type Must Be application/json-patch+json

```csharp
// CRITICAL: JSON Patch requests use a DIFFERENT content type
// Content-Type: application/json-patch+json  (NOT application/json)

// The JSON Patch document format (RFC 6902):
// [
//   { "op": "replace", "path": "/name", "value": "New Name" },
//   { "op": "add", "path": "/tags/-", "value": "new-tag" },
//   { "op": "remove", "path": "/description" },
//   { "op": "copy", "from": "/name", "path": "/displayName" },
//   { "op": "move", "from": "/old", "path": "/new" },
//   { "op": "test", "path": "/version", "value": 2 }
// ]

// COMMON MISTAKE: Using application/json Content-Type
// The model binder won't deserialize properly with wrong content type
```

### Step 3: CRITICAL — Controller Implementation with ModelState Validation

```csharp
using Microsoft.AspNetCore.JsonPatch;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/[controller]")]
public class ProductsController : ControllerBase
{
    [HttpPatch("{id}")]
    [Consumes("application/json-patch+json")]  // CRITICAL: Specify correct content type
    public IActionResult Patch(int id, [FromBody] JsonPatchDocument<ProductDto> patchDoc)
    {
        if (patchDoc == null)
            return BadRequest("Patch document is null");

        var product = _repository.GetById(id);
        if (product == null)
            return NotFound();

        var productDto = MapToDto(product);

        // CRITICAL: Pass ModelState to ApplyTo for error tracking
        patchDoc.ApplyTo(productDto, ModelState);

        // CRITICAL: Check ModelState AFTER ApplyTo — invalid operations are recorded here
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        // CRITICAL: Also run data annotation validation AFTER patching
        if (!TryValidateModel(productDto))
            return BadRequest(ModelState);

        // Apply changes to entity
        MapToEntity(productDto, product);
        _repository.Save(product);

        return Ok(productDto);
    }
}

// COMMON MISTAKE: Not passing ModelState to ApplyTo
// patchDoc.ApplyTo(productDto);  // WRONG - errors silently ignored

// COMMON MISTAKE: Not validating model AFTER applying patch
// The patch might set invalid values (negative price, null required field)
```

### Step 4: CRITICAL — Minimal API Requires Manual Newtonsoft Deserialization

```csharp
// CRITICAL: Minimal APIs use System.Text.Json by default
// JsonPatchDocument CANNOT be deserialized by System.Text.Json
// You must manually read the body and deserialize with Newtonsoft

using Newtonsoft.Json;

app.MapPatch("/api/products/{id}", async (int id, HttpContext context) =>
{
    // CRITICAL: Read raw body and deserialize with Newtonsoft
    using var reader = new StreamReader(context.Request.Body);
    var body = await reader.ReadToEndAsync();

    var patchDoc = JsonConvert.DeserializeObject<JsonPatchDocument<ProductDto>>(body);
    if (patchDoc == null)
        return Results.BadRequest("Invalid patch document");

    var product = await db.Products.FindAsync(id);
    if (product == null)
        return Results.NotFound();

    var dto = MapToDto(product);
    patchDoc.ApplyTo(dto);

    // Validate after patching
    var validationResults = new List<ValidationResult>();
    if (!Validator.TryValidateObject(dto, new ValidationContext(dto), validationResults, true))
        return Results.ValidationProblem(
            validationResults.ToDictionary(
                r => r.MemberNames.First(),
                r => new[] { r.ErrorMessage! }));

    MapToEntity(dto, product);
    await db.SaveChangesAsync();
    return Results.Ok(dto);
});

// COMMON MISTAKE: Using [FromBody] JsonPatchDocument<T> in minimal APIs
// app.MapPatch("/products/{id}", (int id, [FromBody] JsonPatchDocument<ProductDto> patch) => ...);
// FAILS: System.Text.Json tries to deserialize and throws
```

### Step 5: Security — Restrict Patchable Properties

```csharp
// CRITICAL: Without restrictions, clients can patch ANY property
// including sensitive ones like Price, IsAdmin, etc.

// Option 1: Use a separate PatchDto with only allowed properties
public class ProductPatchDto
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    // Don't include Price, CreatedBy, etc.
}

// Option 2: Validate operations before applying
patchDoc.Operations.RemoveAll(op =>
{
    var path = op.path.ToLower().TrimStart('/');
    var allowed = new[] { "name", "description", "category" };
    return !allowed.Contains(path);
});
```

## Common Mistakes

1. **Using System.Text.Json**: JsonPatchDocument requires `Microsoft.AspNetCore.Mvc.NewtonsoftJson`. System.Text.Json does not support it.
2. **Forgetting AddNewtonsoftJson()**: Without it, JsonPatchDocument parameters will be null.
3. **Wrong Content-Type**: Must use `application/json-patch+json`, not `application/json`.
4. **Not passing ModelState to ApplyTo**: Invalid operations are silently ignored without ModelState.
5. **Not validating after patching**: The patch might produce invalid model state (null required fields, out-of-range values).
6. **Using [FromBody] in minimal APIs**: System.Text.Json can't deserialize JsonPatchDocument. Must manually deserialize with Newtonsoft.
7. **Not restricting patchable properties**: Clients can modify any property unless you use a restricted DTO.
