export type CurrencyCode = "USD" | "EUR" | "GBP" | "JPY";

export interface Product {
  id: string;
  name: string;
  unitPriceCents: number;
  /** Weight in grams. Used by ShippingCalculator implementations. */
  weightGrams?: number;
  /** Currency this price is quoted in. Defaults to USD when omitted. */
  currency?: CurrencyCode;
}

export interface CartLine {
  product: Product;
  quantity: number;
}
