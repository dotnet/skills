export function invoiceTotal(lines: number[]): number {
  return lines.reduce((sum, value) => sum + value, 0);
}
