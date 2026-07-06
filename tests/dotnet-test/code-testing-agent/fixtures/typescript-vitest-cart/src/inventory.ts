import type { CartLine, Product } from "./product.ts";

/**
 * Async seam for refreshing prices from a backend before checkout. Tests
 * typically substitute a mock returning a resolved promise (e.g.
 * `vi.fn().mockResolvedValue({ ... })`).
 */
export interface PriceFetcher {
  fetchPriceCents(productId: string): Promise<number>;
}

/**
 * In-stock check seam used by Cart.checkout(). Implementations may resolve
 * with `available: true` and an optional `availableQuantity`, or reject /
 * resolve `available: false` to signal an inventory failure.
 */
export interface InventoryChecker {
  check(productId: string, quantity: number): Promise<InventoryDecision>;
}

export interface InventoryDecision {
  available: boolean;
  /** When provided and less than the requested quantity, callers should treat as a partial-stock denial. */
  availableQuantity?: number;
  /** Human-readable reason for an unavailable decision. */
  reason?: string;
}

export class InventoryError extends Error {
  constructor(
    message: string,
    readonly productId: string,
    readonly requested: number,
    readonly available: number | undefined,
  ) {
    super(message);
    this.name = "InventoryError";
  }
}

/**
 * Apply a fresh set of prices to a snapshot of cart lines. Returns a new
 * array with the same products but updated `unitPriceCents`. Throws if
 * any fetched price is not a non-negative integer.
 */
export async function refreshPrices(
  lines: readonly CartLine[],
  fetcher: PriceFetcher,
): Promise<CartLine[]> {
  const refreshed: CartLine[] = [];
  for (const line of lines) {
    const price = await fetcher.fetchPriceCents(line.product.id);
    if (!Number.isInteger(price) || price < 0) {
      throw new RangeError(
        `fetcher returned invalid price ${price} for product ${line.product.id}`,
      );
    }
    const product: Product = { ...line.product, unitPriceCents: price };
    refreshed.push({ product, quantity: line.quantity });
  }
  return refreshed;
}
