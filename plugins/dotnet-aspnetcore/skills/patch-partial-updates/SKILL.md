---
name: patch-partial-updates
description: >
  Implement HTTP PATCH partial updates in ASP.NET Core that change only the fields the client sends and
  distinguish an explicitly-cleared field (null) from an omitted one.
  USE FOR: adding a PATCH endpoint that updates a subset of a resource's fields; distinguishing "set this
  field to null / clear it" from "leave this field unchanged"; applying JSON Merge Patch semantics over
  JsonElement/JsonNode, a JsonPatchDocument, or a tri-state Optional wrapper; validating only the provided
  fields.
  DO NOT USE FOR: full replacement (PUT) or creation (POST); optimistic concurrency on the update (use the
  concurrency skills); DTO shape design in general (use the model-payloads skills).
license: MIT
---

# PATCH: Partial Updates

A PATCH changes only the fields the client actually sends. The trap is that a plain DTO makes an **omitted field** and an **explicit null** look identical - both deserialize to `null` - so you cannot tell "leave it alone" from "clear it." Pick a mechanism that preserves that distinction, and apply only what was provided.

## Why a plain DTO fails

```csharp
public record UpdateContact(string? Name, string? Email); // Name == null: omitted, or cleared?
```

Copying `contact.Name = dto.Name` writes `null` over `Name` even when the client only meant to change `Email`. That is a full replace (PUT), not a PATCH.

## Preferred: JSON Merge Patch over the raw JSON

Bind the body as `JsonElement` (or `JsonNode`) and apply only the keys that are **present**; a key present with `null` clears the field, an absent key is left untouched. This is JSON Merge Patch (RFC 7386) semantics.

```csharp
[HttpPatch("{id:int}")]
public async Task<ActionResult<ContactDto>> Patch(int id, [FromBody] JsonElement patch, CancellationToken ct)
{
    var contact = await db.Contacts.FindAsync([id], ct);
    if (contact is null)
    {
        return NotFound();
    }

    if (patch.TryGetProperty("name", out var name))
    {
        contact.Name = name.ValueKind == JsonValueKind.Null ? null : name.GetString();
    }

    if (patch.TryGetProperty("email", out var email))
    {
        contact.Email = email.ValueKind == JsonValueKind.Null ? null : email.GetString();
    }

    // Keys the client did not send are left exactly as they were.
    await db.SaveChangesAsync(ct);
    return Ok(contact.ToDto());
}
```

`TryGetProperty` true means "apply"; false means "leave unchanged"; present-and-null means "clear." That is the whole distinction a plain DTO loses.

## Alternatives

- **`JsonPatchDocument<T>` (RFC 6902):** explicit operations (`replace`, `remove`, `add`) with paths, applied via `patch.ApplyTo(...)`. Requires `Microsoft.AspNetCore.JsonPatch` (with the Newtonsoft input formatter). Use when clients want explicit, scriptable edits or array operations. `remove` is the explicit clear.
- **Tri-state `Optional<T>` DTO:** a wrapper whose value carries whether it was set, so `absent` / `null` / `value` stay distinct in a typed model. Use when you want a strongly-typed request instead of raw JSON.

## Validate only what was provided

Run validation against the fields the request actually included (a missing field is not "invalid," it is unchanged). Return the updated resource (`200`) or `204`.

## Verify

- Only the fields the client supplies are changed; omitted fields are left as they were, not overwritten.
- An explicit `null` (clear) produces a different result from an omitted field (leave unchanged).
- The mechanism preserves that distinction - merge patch over `JsonElement`/`JsonNode`, a `JsonPatchDocument`, or a tri-state `Optional` wrapper - not a plain DTO where absent and null collapse to the same value.
- The endpoint uses `PATCH`, and validation applies to the provided fields only.

❌ Bind a plain `UpdateContact` DTO and assign every property - the client clearing nothing still nulls the fields it omitted.
✅ Apply only the keys present in the JSON (present-and-null clears; absent leaves unchanged).
