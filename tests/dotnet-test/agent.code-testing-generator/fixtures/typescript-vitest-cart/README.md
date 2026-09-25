# Shopping cart (TypeScript + Vitest)

A small TypeScript shopping-cart library with pricing, tax, shipping, inventory,
and checkout components.

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
tests/                                  # project tests
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
