export function discountedTotal(total: number, percent: number): number {
  return total - (total * percent) / 100;
}
