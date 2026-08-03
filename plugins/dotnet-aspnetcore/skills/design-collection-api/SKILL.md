---
name: design-collection-api
description: >
  Build a complete, production-ready collection (list) API for a resource and reconcile it with the EF
  Core data model, instead of a naive "return everything" list.
  USE FOR: implementing, adding, building, or scaffolding the list + item (read) endpoints for a resource;
  a "build the API for <resource>" or "list <resource>" task; deciding which cross-cutting capabilities a
  collection needs (stable ordering, pagination, filtering, field selection, incremental change tracking,
  optimistic concurrency, status codes) and what columns/indexes the data model must add (order key,
  rowversion/timestamp watermark, soft-delete, concurrency token), evolving the entity or scoping a
  capability out on purpose.
  DO NOT USE FOR: the mechanics of a single endpoint (use author-controller-endpoints /
  author-minimal-api-endpoints); nesting child resources or lifecycle transitions; the detailed
  implementation of one capability - route to that capability's own skill.
license: MIT
---

# Design a Collection API

A collection endpoint is rarely just `return db.Orders.ToList()`. Before writing the list + item endpoints for a resource, decide which cross-cutting capabilities it needs, because **each capability imposes a requirement on the data model** - an indexed order key, a change watermark, a soft-delete flag, a concurrency token. Deciding late means a schema migration and a breaking API change. So the design move is: pick the capabilities, **reconcile them with the entity** (add the columns/indexes, or consciously scope the capability out), then implement each via its dedicated skill.

Always offer **filtering** and **incremental change tracking**, and reconcile the persistence columns they need even when the entity already has them - these are the axes most often dropped from a collection API. Run the checklist so nothing is omitted silently.

## The checklist: decide each axis, then reconcile with the model

For the resource's collection, decide each row - "yes and implement", or "no, out of scope for now" (a deliberate, recorded choice, not an oversight). The right column is the data-model obligation the "yes" answer creates.

| Capability | Data-model requirement it imposes | Implement via skill |
|---|---|---|
| **Stable ordering** (deterministic list order) | An indexed sort column, plus a unique final tiebreaker (the id) | controller-data-access / minimal-api-data-access |
| **Pagination** (bounded page, forward nav) | Keyset over that same indexed order key (not OFFSET) | controller-data-access / minimal-api-data-access |
| **Filtering** (narrow the list) | Each filterable field indexed; opt-in allow-list | filter-and-select |
| **Field selection** (smaller responses) | none (projection only) - but keep the order key internally | filter-and-select |
| **Incremental change tracking** (delta sync) | A monotonic per-row watermark (**rowversion**, else an app-set timestamp) **+ soft-delete** (`IsDeleted`/`DeletedAt`) with tombstone retention | change-tracking-delta |
| **Optimistic concurrency** (safe updates) | A concurrency token (the same rowversion) surfaced as `ETag` | controller-concurrency / minimal-api-concurrency |
| **Correct status codes / result types** | none | author-controller-endpoints / author-minimal-api-endpoints |
| **Long-running writes** (202) | Persisted operation-status record | long-running-operations |
| **Rate limiting** | A partition key (tenant/subject) | rate-limiting |
| **Partial update** (PATCH) | Nullable-vs-required field distinction | patch-partial-updates |

## Reconcile with the data model - the step that gets skipped

Open the entity and, for every "yes" row, confirm the column exists or add it; do not assume the capability works against the entity as-is.

- **Ordering / pagination** need an **indexed** column with a natural order (a `CreatedAt` or a sequence) and a unique tiebreaker. If the only candidate is a mutable `Name`, ordering by it alone is unstable - add the id tiebreaker and index `(sortKey, id)`.
- **Change tracking** needs a **rowversion** (`[Timestamp]` / `IsRowVersion()`), **soft-delete** columns, and a retention policy for tombstones. A hard-delete model *cannot* report deletions to a syncing client - decide this before clients depend on delta.
- **Concurrency** reuses that rowversion as the `ETag` validator - one column serves both delta and concurrency.
- If a needed column is missing and you will not add it now, **scope the capability out explicitly** and say why, rather than shipping a half-working version.

A well-designed entity for a syncable, pageable, concurrency-checked collection therefore carries: the id, the business fields, `CreatedAt`/`LastModifiedAt`, `IsDeleted`/`DeletedAt`, and a `RowVersion` - and indexes the ordering/filtering columns.

```csharp
public class Shipment                    // a resource designed for a full collection API
{
    public Guid Id { get; set; }
    public required string TrackingCode { get; set; }
    public ShipmentStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastModifiedAt { get; set; }
    public bool IsDeleted { get; set; }          // soft-delete: lets delta report removals
    public DateTimeOffset? DeletedAt { get; set; }
    [Timestamp] public byte[] RowVersion { get; set; } = []; // ETag validator AND delta watermark
}
// index (CreatedAt, Id) for stable keyset order; index Status if it is filterable.
```

## Combine the capabilities in the right order

These features compose in one query pipeline; the order matters:

**filter (allow-listed `Where`) → stable order (`OrderBy(key).ThenBy(id)`) → keyset page (`Where` against the cursor) → project (`Select`, keeping the order key) → shape the response.**

Delta is the same pipeline with the ordering key set to the change marker (see change-tracking-delta). Field selection must never drop the order key from the query even if the client did not ask for it, or paging breaks.

Always end the sort on a unique column so ties cannot skip or repeat rows across pages:

```csharp
query.OrderBy(s => s.CreatedAt).ThenBy(s => s.Id)   // total order: unique final key
```

## Start simple, then watch for amplification

Ship the smallest surface that meets the need, but design the entity so the next capability is an additive migration, not a rewrite. Adding `RowVersion` + soft-delete columns *now* (even before delta ships) keeps change tracking and concurrency a pure addition later. Adding them *after* clients depend on hard deletes is a breaking change.

## Verify

- Every checklist axis is a conscious decision - implemented, or scoped out on purpose with a reason - not silently omitted. In particular, filtering and change tracking were each considered, not skipped by default.
- For each implemented axis, the entity actually carries the column/index it needs (order key + id tiebreaker; rowversion; soft-delete; concurrency token), or a migration adds it.
- The list returns a deterministic total order ending in a unique key, and is paged with a bounded size - never an unbounded `ToList()`.
- One `RowVersion` column serves both the `ETag` concurrency validator and the delta watermark; soft-delete is present if delta is (or in) scope.
- Each capability is implemented through its named skill rather than re-invented here; the response projects to a DTO instead of leaking the entity graph.

❌ A list endpoint that returns the whole table in database order, with no filtering, no change feed, and an entity that has no rowversion or soft-delete - so adding those later breaks clients.
✅ A capability decision per axis, an entity reconciled to support the "yes" ones (indexed order key, rowversion, soft-delete), and each capability delegated to its skill.
