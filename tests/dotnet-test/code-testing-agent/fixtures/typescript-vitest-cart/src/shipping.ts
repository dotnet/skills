import type { CartLine } from "./product.ts";
import { totalLineWeightGrams } from "./tax.ts";

/**
 * ShippingCalculator is the seam consumers (and tests) substitute to compute shipping.
 * Implementations must be pure and deterministic for a given input.
 */
export interface ShippingCalculator {
  /**
   * @param lines           Cart lines being shipped.
   * @param subtotalCents   Subtotal AFTER discounts (used for free-shipping thresholds).
   * @returns Shipping cost in cents as a non-negative integer.
   */
  computeShippingCents(
    lines: readonly CartLine[],
    subtotalCents: number,
  ): number;
}

export class FreeShippingCalculator implements ShippingCalculator {
  computeShippingCents(
    _lines: readonly CartLine[],
    _subtotalCents: number,
  ): number {
    return 0;
  }
}

/**
 * Flat shipping fee with optional free-over-threshold logic.
 */
export class FlatShippingCalculator implements ShippingCalculator {
  constructor(
    private readonly flatCents: number,
    private readonly freeOverCents?: number,
  ) {
    if (!Number.isInteger(flatCents) || flatCents < 0) {
      throw new RangeError(
        `flatCents must be a non-negative integer (got ${flatCents})`,
      );
    }
    if (freeOverCents !== undefined) {
      if (!Number.isInteger(freeOverCents) || freeOverCents < 0) {
        throw new RangeError(
          `freeOverCents must be a non-negative integer (got ${freeOverCents})`,
        );
      }
    }
  }

  computeShippingCents(
    lines: readonly CartLine[],
    subtotalCents: number,
  ): number {
    if (lines.length === 0) return 0;
    if (this.freeOverCents !== undefined && subtotalCents >= this.freeOverCents) {
      return 0;
    }
    return this.flatCents;
  }
}

export interface WeightBracket {
  /** Inclusive upper bound for the bracket, in grams. */
  upToGrams: number;
  /** Cost in cents for items totalling weight <= upToGrams. */
  costCents: number;
}

/**
 * Weight-bucketed shipping. The bracket whose `upToGrams` first equals-or-exceeds
 * the total weight wins. Anything over the heaviest bracket uses an overflow rate
 * (defaults to the heaviest bracket cost). Empty carts ship free.
 */
export class WeightBasedShippingCalculator implements ShippingCalculator {
  private readonly sortedBrackets: readonly WeightBracket[];

  constructor(
    brackets: readonly WeightBracket[],
    private readonly overflowCostCents?: number,
  ) {
    if (brackets.length === 0) {
      throw new Error("at least one weight bracket is required");
    }
    for (const bracket of brackets) {
      if (!Number.isInteger(bracket.upToGrams) || bracket.upToGrams <= 0) {
        throw new RangeError("bracket.upToGrams must be a positive integer");
      }
      if (!Number.isInteger(bracket.costCents) || bracket.costCents < 0) {
        throw new RangeError("bracket.costCents must be a non-negative integer");
      }
    }
    if (
      overflowCostCents !== undefined &&
      (!Number.isInteger(overflowCostCents) || overflowCostCents < 0)
    ) {
      throw new RangeError("overflowCostCents must be a non-negative integer");
    }
    this.sortedBrackets = [...brackets].sort((a, b) => a.upToGrams - b.upToGrams);
  }

  computeShippingCents(
    lines: readonly CartLine[],
    _subtotalCents: number,
  ): number {
    if (lines.length === 0) return 0;
    const weight = totalLineWeightGrams(lines);
    if (weight === 0) return 0;
    for (const bracket of this.sortedBrackets) {
      if (weight <= bracket.upToGrams) return bracket.costCents;
    }
    return (
      this.overflowCostCents ??
      this.sortedBrackets[this.sortedBrackets.length - 1]!.costCents
    );
  }
}
