# Mutation Candidate Catalog

Read this reference only for an explicitly exhaustive audit or when a language's
mutation semantics are unfamiliar. For focused analysis, use the smaller
risk-ranked table in `SKILL.md`.

## Candidate categories

| Category | Typical changes | What a killing test must observe |
|---|---|---|
| Boundary | `<` ↔ `<=`, `>` ↔ `>=`, zero/one, first/last index | Exact value at and immediately around the boundary |
| Boolean/logic | `&&` ↔ &#124;&#124;, negate/remove one condition, `true` ↔ `false` | Each condition independently changes asserted behavior |
| Return value | value ↔ default/empty/null, true ↔ false, count ±1 | The returned value or downstream state |
| Error/guard | remove guard, change exception/error type, swallow propagation | Invalid input and exact observable error semantics |
| Arithmetic | `+` ↔ `-`, `*` ↔ `/`, sign flip, increment ↔ decrement | Exact calculated result, not only a broad range |
| Collection | empty/non-empty, omit first/last item, order reversal | Contents, count, and order where relevant |
| State transition | skip assignment, retain old state, alter an existing update | Both result and resulting state |

## Language-specific error candidates

| Language family | Meaningful candidates |
|---|---|
| C#/.NET | Remove null/range guards; change exception type; replace `??` fallback; change null-conditional access; return `default`; alter async cancellation/error propagation |
| Rust | Replace `?` with `unwrap()`/`expect()`; swap `Ok`/`Err` or `Some`/`None`; remove `if let`/`match` arm; change inclusive range; alter error mapping |
| Go | Remove or swallow a meaningful `err` branch; change wrapped error; alter `(value, err)` result. Do not flag a bare idiomatic passthrough unless behavior changes |
| Python | Remove `raise`; change exception type; replace `None` fallback; alter truthiness/boundary checks |
| TypeScript/JavaScript | Remove rejected-promise/error path; alter nullish coalescing; confuse truthiness with exact value; skip awaited behavior |
| Java/Kotlin | Remove validation/exception; change nullable/default handling; alter collection or stream predicate |

When framework-specific test discovery or assertion APIs are unclear, invoke
`test-analysis-extensions` and read only the matching language extension.

## Equivalence and noise filters

Exclude:

- generated/designer/migration output;
- auto-properties, records/data holders, and trivial forwarding methods;
- logging-only or formatting-only changes unless the user identifies them as
  contract behavior;
- impossible boundary values under the domain;
- redundant defensive checks whose removal cannot affect any public behavior;
- short-circuit or guard edits that fall through to the same return, exception,
  state, and side effects;
- private representation changes that no current public input sequence can
  distinguish, even when the existing suite stays green;
- multiple syntax variants that exercise the same missing behavior.

## Exhaustive audit procedure

1. Enumerate meaningful candidates by production behavior, not token/operator.
2. State the public input and different original/mutant observations.
3. Map each candidate to covering tests and relevant assertions.
4. Classify obvious killed/equivalent candidates statically.
5. Execute every candidate that might be reported as Survived.
6. After a green run, re-check that the mutation is publicly observable.
7. Revert after each run and confirm the clean baseline at the end.
8. Count only executed or definitively killed/equivalent candidates in the
   mutation totals; disclose any omitted scope.
