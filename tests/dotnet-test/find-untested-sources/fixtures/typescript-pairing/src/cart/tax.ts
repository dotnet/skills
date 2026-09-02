export function calculateTax(subtotal: number, rate: number): number {
  return Math.round(subtotal * rate);
}
