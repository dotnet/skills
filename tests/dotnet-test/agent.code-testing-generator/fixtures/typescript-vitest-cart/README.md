# Shopping cart (TypeScript + Vitest) — code-testing-agent polyglot eval fixture

A small TypeScript shopping-cart library used as a polyglot eval fixture for the `code-testing-agent` skill. The agent is asked to write a comprehensive Vitest suite; the eval verifies that `vitest run` passes against the suite the agent produced.

## Layout

```
package.json                            # pinned vitest + typescript + @vitest/coverage-v8 (devDependencies)
package-lock.json                       # generated, committed for npm ci reproducibility
tsconfig.json                           # bundler resolution, strict mode, allowImportingTsExtensions
vitest.config.ts                        # tests/**/*.test.ts, node env, non-global API
src/
  product.ts                            # Product (+ optional weight / currency) + CartLine value types
  pricing.ts                            # DiscountPolicy + No / Percentage / FixedAmount / CompositeDiscountPolicy (sum | chain)
  tax.ts                                # TaxCalculator + No/RegionalTaxCalculator; AsyncTaxRateProvider + AsyncTaxCalculator
  shipping.ts                           # ShippingCalculator + Free/Flat/WeightBasedShippingCalculator + WeightBracket
  inventory.ts                          # PriceFetcher + InventoryChecker async seams + InventoryError + refreshPrices()
  cart.ts                               # Cart: pricing pipeline (subtotal → discount → tax → shipping) + async checkout()
  index.ts                              # barrel export
tests/                                  # no test files yet (only a .gitkeep marker) — the agent must create the suite here
```

## Running tests locally

```bash
npm ci
npx vitest run
npx vitest run --coverage
```

Coverage (`@vitest/coverage-v8`) is pre-configured in `vitest.config.ts`
and is enforced as a **hard floor** when `--coverage` is passed: lines /
statements / functions ≥ 80%, branches ≥ 70%. The coverage run exits
non-zero if any threshold is not met.

## What the agent should produce

A planned, layered Vitest suite covering this fixture's multiple seams:

- Unit tests for each discount policy (`No`, `Percentage`, `FixedAmount`, `Composite`) covering
  rounding, boundary inputs (negative subtotal, zero subtotal, very large subtotal), and constructor
  validation (`PercentageDiscountPolicy` rejects out-of-range percent, `FixedAmount` rejects negative,
  `Composite` rejects empty list). For `CompositeDiscountPolicy`, cover both stacking modes
  (`"sum"` vs `"chain"`) including ordering effects.
- Unit tests for each tax calculator (`No`, `Regional`, `AsyncTaxCalculator`) covering region
  fallback to the default rate, rejection of out-of-range rates, async-rate failure propagation,
  and rounding behavior on the discounted subtotal.
- Unit tests for each shipping calculator (`Free`, `Flat`, `WeightBased`) covering empty-cart
  short-circuits, free-over-threshold, weight bracket selection (boundaries between brackets), and
  the overflow cost path.
- `Cart` tests covering: merge semantics on `add`, `updateQuantity` 0-removes, `remove` returns
  false for unknown ids, `clear`, and the pricing pipeline composition (the order
  subtotal → discount → tax → shipping must hold; tax is on the *discounted* subtotal).
- Async `Cart.checkout()` tests using `vi.fn().mockResolvedValue(...)` (and `.mockRejectedValue(...)`)
  for `PriceFetcher`, `InventoryChecker`, and `AsyncTaxRateProvider`. Cover: price refresh, inventory
  denial (and partial-stock denial via `availableQuantity`), async tax with discount + shipping
  composition, and the `InventoryError` shape (productId, requested, available).
- No real network, no real filesystem.
