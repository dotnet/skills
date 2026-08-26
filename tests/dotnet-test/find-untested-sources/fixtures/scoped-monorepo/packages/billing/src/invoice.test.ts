import { invoiceTotal } from "./invoice";

describe("invoiceTotal", () => {
  it("adds line values", () => {
    expect(invoiceTotal([200, 300])).toBe(500);
  });
});
