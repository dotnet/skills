# Partial-suite gap decisions

Use this when tests already exist and the user asks for missing cases rather than
tests for a named untested symbol.

## Do not enumerate plausible edges blindly

An apparent edge is not a gap when an existing test already kills the behavior
through a helper, parameter row, or public call chain. Build an evidence map first:

1. Run the narrow existing suite and establish a green baseline.
2. Map each existing assertion to the production branch or outcome it pins down.
3. Propose mutations only at meaningful decisions: boundary flips, removed guards,
   boolean changes, default returns, exception removal, and order-of-operations.
4. For every candidate you intend to report or test, apply it temporarily and run
   the narrow covering tests.
   - Existing tests fail: the mutation is killed; do **not** add a duplicate test.
   - Tests stay green: the mutation survived; add the smallest test that kills it.
5. Revert each mutation immediately and confirm production source is restored.
6. Put additions in a new file when the user requires existing tests unchanged.
7. Re-run the suite and the same mutation against the final tests.

One test may kill several related mutations. Prefer that over one test per syntax
variation. Report only empirically survived/no-coverage behavior; label static-only
reasoning when execution is genuinely unavailable.
