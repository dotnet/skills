import { calculateSubtotal } from "../../src/cart/pricing";

describe("calculateSubtotal", () => {
  it("adds prices", () => {
    expect(calculateSubtotal([200, 300])).toBe(500);
  });
});
