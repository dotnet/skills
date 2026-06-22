import type { CartLine, Product } from "./product.ts";
import { NoDiscountPolicy, type DiscountPolicy } from "./pricing.ts";
import {
  type AsyncTaxRateProvider,
  AsyncTaxCalculator,
  NoTaxCalculator,
  type TaxCalculator,
} from "./tax.ts";
import { FreeShippingCalculator, type ShippingCalculator } from "./shipping.ts";
import {
  InventoryError,
  refreshPrices,
  type InventoryChecker,
  type PriceFetcher,
} from "./inventory.ts";

export interface CartTotals {
  subtotalCents: number;
  discountCents: number;
  taxCents: number;
  shippingCents: number;
  totalCents: number;
}

export interface CartConfig {
  discountPolicy?: DiscountPolicy;
  taxCalculator?: TaxCalculator;
  shippingCalculator?: ShippingCalculator;
  /** ISO region used for tax look-ups. Defaults to "US". */
  region?: string;
}

export interface CheckoutCollaborators {
  priceFetcher?: PriceFetcher;
  inventoryChecker?: InventoryChecker;
  asyncTaxProvider?: AsyncTaxRateProvider;
}

export interface CheckoutResult {
  totals: CartTotals;
  lines: readonly CartLine[];
}

/**
 * Cart aggregates lines of products and computes totals. The pricing pipeline
 * is fixed at: **subtotal → discount (clamped to [0, subtotal]) → tax on the
 * discounted subtotal → shipping (computed against discounted subtotal)**.
 *
 * The cart never reaches the network or filesystem itself; all I/O is delegated
 * to injected collaborators (DiscountPolicy / TaxCalculator / ShippingCalculator
 * for sync totals, and PriceFetcher / InventoryChecker / AsyncTaxRateProvider
 * for `checkout()`).
 */
export class Cart {
  private readonly lines = new Map<string, CartLine>();
  private readonly discountPolicy: DiscountPolicy;
  private readonly taxCalculator: TaxCalculator;
  private readonly shippingCalculator: ShippingCalculator;
  private readonly region: string;

  constructor(config: CartConfig = {}) {
    this.discountPolicy = config.discountPolicy ?? new NoDiscountPolicy();
    this.taxCalculator = config.taxCalculator ?? new NoTaxCalculator();
    this.shippingCalculator =
      config.shippingCalculator ?? new FreeShippingCalculator();
    this.region = config.region ?? "US";
  }

  add(product: Product, quantity: number): void {
    if (!Number.isInteger(quantity) || quantity <= 0) {
      throw new RangeError(`quantity must be a positive integer (got ${quantity})`);
    }
    if (product.unitPriceCents < 0) {
      throw new RangeError(`unitPriceCents must be non-negative (got ${product.unitPriceCents})`);
    }

    const existing = this.lines.get(product.id);
    if (existing) {
      existing.quantity += quantity;
    } else {
      this.lines.set(product.id, { product, quantity });
    }
  }

  remove(productId: string): boolean {
    return this.lines.delete(productId);
  }

  updateQuantity(productId: string, quantity: number): void {
    if (!Number.isInteger(quantity) || quantity < 0) {
      throw new RangeError(`quantity must be a non-negative integer (got ${quantity})`);
    }
    const line = this.lines.get(productId);
    if (!line) {
      throw new Error(`Product ${productId} is not in the cart`);
    }
    if (quantity === 0) {
      this.lines.delete(productId);
      return;
    }
    line.quantity = quantity;
  }

  clear(): void {
    this.lines.clear();
  }

  get itemCount(): number {
    let total = 0;
    for (const line of this.lines.values()) total += line.quantity;
    return total;
  }

  totals(): CartTotals {
    return this.computeTotals(this.snapshot());
  }

  snapshot(): readonly CartLine[] {
    // Deep-copy both the CartLine and its Product so callers mutating the
    // returned snapshot cannot reach back into the cart's internal state.
    return Array.from(this.lines.values()).map((line) => ({
      product: { ...line.product },
      quantity: line.quantity,
    }));
  }

  /**
   * Async checkout pipeline:
   *  1. Refresh prices via the (optional) `priceFetcher`.
   *  2. Validate inventory via the (optional) `inventoryChecker`; first denial
   *     wins and throws an :class:`InventoryError` describing the failing line.
   *  3. Compute totals; if an `asyncTaxProvider` is supplied, the tax calculator
   *     is replaced for this call by an :class:`AsyncTaxCalculator`.
   */
  async checkout(collaborators: CheckoutCollaborators = {}): Promise<CheckoutResult> {
    let lines = this.snapshot();
    if (collaborators.priceFetcher) {
      lines = await refreshPrices(lines, collaborators.priceFetcher);
    }
    if (collaborators.inventoryChecker) {
      for (const line of lines) {
        const decision = await collaborators.inventoryChecker.check(
          line.product.id,
          line.quantity,
        );
        const available = decision.availableQuantity;
        if (!decision.available || (available !== undefined && available < line.quantity)) {
          throw new InventoryError(
            decision.reason ??
              `insufficient stock for ${line.product.id}: requested ${line.quantity}, available ${available ?? 0}`,
            line.product.id,
            line.quantity,
            available,
          );
        }
      }
    }
    const totals = collaborators.asyncTaxProvider
      ? await this.computeTotalsAsync(lines, collaborators.asyncTaxProvider)
      : this.computeTotals(lines);
    return { totals, lines };
  }

  private computeSubtotal(lines: readonly CartLine[]): number {
    let total = 0;
    for (const line of lines) {
      total += line.product.unitPriceCents * line.quantity;
    }
    return total;
  }

  private clampDiscount(subtotalCents: number): number {
    return Math.min(
      Math.max(0, this.discountPolicy.computeDiscountCents(subtotalCents)),
      subtotalCents,
    );
  }

  private computeTotals(lines: readonly CartLine[]): CartTotals {
    const subtotalCents = this.computeSubtotal(lines);
    const discountCents = this.clampDiscount(subtotalCents);
    const discountedSubtotal = subtotalCents - discountCents;
    const taxCents = Math.max(
      0,
      this.taxCalculator.computeTaxCents(discountedSubtotal, this.region),
    );
    const shippingCents = Math.max(
      0,
      this.shippingCalculator.computeShippingCents(lines, discountedSubtotal),
    );
    return {
      subtotalCents,
      discountCents,
      taxCents,
      shippingCents,
      totalCents: discountedSubtotal + taxCents + shippingCents,
    };
  }

  private async computeTotalsAsync(
    lines: readonly CartLine[],
    provider: AsyncTaxRateProvider,
  ): Promise<CartTotals> {
    const subtotalCents = this.computeSubtotal(lines);
    const discountCents = this.clampDiscount(subtotalCents);
    const discountedSubtotal = subtotalCents - discountCents;
    const asyncTax = new AsyncTaxCalculator(provider);
    const taxCents = Math.max(
      0,
      await asyncTax.computeTaxCents(discountedSubtotal, this.region),
    );
    const shippingCents = Math.max(
      0,
      this.shippingCalculator.computeShippingCents(lines, discountedSubtotal),
    );
    return {
      subtotalCents,
      discountCents,
      taxCents,
      shippingCents,
      totalCents: discountedSubtotal + taxCents + shippingCents,
    };
  }
}
