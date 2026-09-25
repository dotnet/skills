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

/** Flat amount off the subtotal. */
export class FixedAmountDiscountPolicy implements DiscountPolicy {
  constructor(private readonly amountCents: number) {
    if (!Number.isInteger(amountCents) || amountCents < 0) {
      throw new RangeError(
        `amountCents must be a non-negative integer (got ${amountCents})`,
      );
    }
  }

  computeDiscountCents(subtotalCents: number): number {
    if (subtotalCents <= 0) return 0;
    return Math.min(this.amountCents, subtotalCents);
  }
}

/**
 * Stacks multiple discount policies. Two stacking modes are supported:
 *
 * - `"sum"`: each child policy receives the original subtotal; results are summed
 *   then clamped to [0, subtotalCents] by the caller (Cart.totals).
 * - `"chain"`: each child policy is applied to the *remaining* subtotal after
 *   previous discounts (i.e. discounts compound). This is the typical "best
 *   coupon first" semantics. The discount value returned is the sum of the
 *   individual discount amounts, so the visible discountCents in CartTotals
 *   still represents the total reduction.
 */
export type StackingMode = "sum" | "chain";

export class CompositeDiscountPolicy implements DiscountPolicy {
  constructor(
    private readonly policies: readonly DiscountPolicy[],
    private readonly mode: StackingMode = "sum",
  ) {
    if (policies.length === 0) {
      throw new Error("CompositeDiscountPolicy requires at least one policy");
    }
  }

  computeDiscountCents(subtotalCents: number): number {
    if (subtotalCents <= 0) return 0;
    if (this.mode === "sum") {
      let total = 0;
      for (const p of this.policies) {
        total += Math.max(0, p.computeDiscountCents(subtotalCents));
      }
      return total;
    }
    let remaining = subtotalCents;
    let total = 0;
    for (const p of this.policies) {
      const step = Math.min(
        Math.max(0, p.computeDiscountCents(remaining)),
        remaining,
      );
      remaining -= step;
      total += step;
    }
    return total;
  }
}
