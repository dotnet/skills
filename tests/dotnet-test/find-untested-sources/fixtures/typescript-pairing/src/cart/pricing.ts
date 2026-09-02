export function calculateSubtotal(prices: number[]): number {
  return prices.reduce((sum, price) => sum + price, 0);
}
