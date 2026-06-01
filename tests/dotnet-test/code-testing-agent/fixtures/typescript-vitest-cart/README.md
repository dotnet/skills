# Shopping cart (TypeScript + Vitest) — code-testing-agent polyglot eval fixture

A small TypeScript shopping-cart library used as a polyglot eval fixture for the `code-testing-agent` skill. The agent is asked to write a comprehensive Vitest suite; the eval verifies that `vitest run` passes against the suite the agent produced.

## Layout

```
package.json                            # pinned vitest + typescript + @vitest/coverage-v8 (devDependencies)
package-lock.json                       # generated, committed for npm ci reproducibility
tsconfig.json                           # bundler resolution, strict mode, allowImportingTsExtensions
vitest.config.ts                        # tests/**/*.test.ts, node env, non-global API
src/
  product.ts                            # Product + CartLine value types
  pricing.ts                            # DiscountPolicy interface + No/Percentage policies
  cart.ts                               # Cart class with injected DiscountPolicy seam
  index.ts                              # barrel export
tests/                                  # intentionally empty — the agent must create this
```

## Running tests locally

```bash
npm ci
npx vitest run
```

## What the agent should produce

- Unit tests for `Cart` that mock `DiscountPolicy` (e.g. via `vi.fn()` returning a fixed discount) to exercise the seam.
- Boundary tests for `PercentageDiscountPolicy` constructor (rejects out-of-range percent).
- Coverage of `Cart.add` merge semantics, `updateQuantity` 0-removes, `remove` returns false for unknown id, and `totals()` clamping when policy returns a discount greater than subtotal.
- No real network, no real filesystem.
