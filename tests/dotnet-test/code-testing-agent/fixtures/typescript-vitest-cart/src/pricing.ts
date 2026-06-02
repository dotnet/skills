/**
 * DiscountPolicy is the seam consumers (and tests) substitute to control pricing.
 * Implementations must be deterministic and pure.
 */
export interface DiscountPolicy {
  /**
   * @param subtotalCents Sum of (unitPriceCents * quantity) across all lines, before any discount.
   * @returns Discount amount in cents as an integer. Cart.totals() clamps the returned value
   *          into [0, subtotalCents], so policies should aim for that range but are not
   *          required to — negative values are clamped to 0 and over-subtotal values are
   *          clamped down to subtotalCents.
   */
  computeDiscountCents(subtotalCents: number): number;
}

/** Default policy: no discount. */
export class NoDiscountPolicy implements DiscountPolicy {
  computeDiscountCents(_subtotalCents: number): number {
    return 0;
  }
}

/** Flat percentage off the subtotal, rounded down to the nearest cent. */
export class PercentageDiscountPolicy implements DiscountPolicy {
  constructor(private readonly percent: number) {
    if (!Number.isFinite(percent) || percent < 0 || percent > 100) {
      throw new RangeError(`percent must be between 0 and 100 (got ${percent})`);
    }
  }

  computeDiscountCents(subtotalCents: number): number {
    if (subtotalCents <= 0) return 0;
    return Math.floor((subtotalCents * this.percent) / 100);
  }
}
