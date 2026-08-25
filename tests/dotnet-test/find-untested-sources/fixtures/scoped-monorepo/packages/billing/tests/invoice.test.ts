import { invoiceTotal } from "../src/invoice";

describe("invoiceTotal", () => {
  it("adds line values", () => {
    expect(invoiceTotal([200, 300])).toBe(500);
  });
});
