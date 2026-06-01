import type { CartLine, Product } from "./product.ts";
import { NoDiscountPolicy, type DiscountPolicy } from "./pricing.ts";

export interface CartTotals {
  subtotalCents: number;
  discountCents: number;
  totalCents: number;
}

/**
 * Cart aggregates lines of products and computes totals through an injectable
 * DiscountPolicy. The cart never reaches the network or filesystem.
 */
export class Cart {
  private readonly lines = new Map<string, CartLine>();

  constructor(private readonly discountPolicy: DiscountPolicy = new NoDiscountPolicy()) {}

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

  get itemCount(): number {
    let total = 0;
    for (const line of this.lines.values()) total += line.quantity;
    return total;
  }

  totals(): CartTotals {
    let subtotalCents = 0;
    for (const line of this.lines.values()) {
      subtotalCents += line.product.unitPriceCents * line.quantity;
    }
    const discountCents = Math.min(
      Math.max(0, this.discountPolicy.computeDiscountCents(subtotalCents)),
      subtotalCents,
    );
    return {
      subtotalCents,
      discountCents,
      totalCents: subtotalCents - discountCents,
    };
  }

  snapshot(): readonly CartLine[] {
    return Array.from(this.lines.values()).map((line) => ({ ...line }));
  }
}
