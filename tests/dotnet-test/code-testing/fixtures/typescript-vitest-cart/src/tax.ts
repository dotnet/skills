import type { CartLine } from "./product.ts";

/**
 * TaxCalculator is a seam consumers (and tests) substitute to compute tax.
 * Implementations must be pure and deterministic for a given input.
 */
export interface TaxCalculator {
  /**
   * @param taxableCents Subtotal after any discounts.
   * @param region  ISO-3166-1 alpha-2 country code (and optionally a sub-division), e.g. "US",
   *                "US-CA", "FR". Implementations decide how granular they go.
   * @returns Tax amount in cents as a non-negative integer. Rounded down to nearest cent.
   */
  computeTaxCents(taxableCents: number, region: string): number;
}

export class NoTaxCalculator implements TaxCalculator {
  computeTaxCents(_taxableCents: number, _region: string): number {
    return 0;
  }
}

/**
 * Looks up a flat tax rate per region. Unknown regions fall back to a default
 * rate (default 0). Rates are expressed as decimal fractions (e.g. 0.0875 for
 * 8.75%). The result is rounded down to the nearest cent.
 */
export class RegionalTaxCalculator implements TaxCalculator {
  constructor(
    private readonly rates: Readonly<Record<string, number>>,
    private readonly defaultRate: number = 0,
  ) {
    if (!Number.isFinite(defaultRate) || defaultRate < 0 || defaultRate > 1) {
      throw new RangeError(
        `defaultRate must be between 0 and 1 (got ${defaultRate})`,
      );
    }
    for (const [region, rate] of Object.entries(rates)) {
      if (!Number.isFinite(rate) || rate < 0 || rate > 1) {
        throw new RangeError(
          `rate for ${region} must be between 0 and 1 (got ${rate})`,
        );
      }
    }
  }

  computeTaxCents(taxableCents: number, region: string): number {
    if (taxableCents <= 0) return 0;
    const rate = this.rates[region] ?? this.defaultRate;
    return Math.floor(taxableCents * rate);
  }
}

/**
 * Async seam for callers that resolve rates from a remote service.
 *
 * Cart.checkout() uses this seam when configured; tests typically substitute
 * a mock returning a resolved promise (e.g. `vi.fn().mockResolvedValue(...)`).
 */
export interface AsyncTaxRateProvider {
  getRate(region: string): Promise<number>;
}

/** Convenience adapter: drives a synchronous TaxCalculator off a provider's rate. */
export class AsyncTaxCalculator {
  constructor(private readonly provider: AsyncTaxRateProvider) {}

  async computeTaxCents(taxableCents: number, region: string): Promise<number> {
    if (taxableCents <= 0) return 0;
    const rate = await this.provider.getRate(region);
    if (!Number.isFinite(rate) || rate < 0 || rate > 1) {
      throw new RangeError(`tax rate for ${region} must be between 0 and 1 (got ${rate})`);
    }
    return Math.floor(taxableCents * rate);
  }
}

/** Helper for tests / callers that need to compute taxable weight from lines. */
export function totalLineWeightGrams(lines: readonly CartLine[]): number {
  let total = 0;
  for (const line of lines) {
    total += (line.product.weightGrams ?? 0) * line.quantity;
  }
  return total;
}
