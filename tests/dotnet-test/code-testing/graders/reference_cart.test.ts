// Grader regression input only: never copy this reference suite into an eval.
import { expect, test } from "vitest";
import { Cart } from "../src/cart.ts";
import {
  CompositeDiscountPolicy, FixedAmountDiscountPolicy, NoDiscountPolicy,
  PercentageDiscountPolicy,
} from "../src/pricing.ts";
import { AsyncTaxCalculator, NoTaxCalculator, RegionalTaxCalculator, totalLineWeightGrams } from "../src/tax.ts";
import { FlatShippingCalculator, FreeShippingCalculator, WeightBasedShippingCalculator } from "../src/shipping.ts";
import { InventoryError, refreshPrices } from "../src/inventory.ts";
import type { Product } from "../src/product.ts";

const product = (price = 101, weight = 100): Product =>
  ({ id: "A", name: "Example", unitPriceCents: price, weightGrams: weight });
const lines = [{ product: product(), quantity: 2 }];
const failure = new Error("service unavailable");

test("discount arithmetic and ordering", () => {
  expect(new NoDiscountPolicy().computeDiscountCents(101)).toBe(0);
  const percent = new PercentageDiscountPolicy(10);
  const fixed = new FixedAmountDiscountPolicy(50);
  expect(percent.computeDiscountCents(101)).toBe(10);
  expect(percent.computeDiscountCents(0)).toBe(0);
  expect(fixed.computeDiscountCents(20)).toBe(20);
  expect(fixed.computeDiscountCents(0)).toBe(0);
  expect(new CompositeDiscountPolicy([fixed, percent]).computeDiscountCents(101)).toBe(60);
  expect(new CompositeDiscountPolicy([fixed, percent], "chain").computeDiscountCents(101)).toBe(55);
  expect(new CompositeDiscountPolicy([percent]).computeDiscountCents(0)).toBe(0);
  for (const value of [-1, 101, NaN, Infinity]) {
    expect(() => new PercentageDiscountPolicy(value)).toThrow(RangeError);
  }
  for (const value of [-1, 0.5, NaN]) {
    expect(() => new FixedAmountDiscountPolicy(value)).toThrow(RangeError);
  }
  expect(() => new CompositeDiscountPolicy([])).toThrow();
});

test("regional arithmetic and rate validation", async () => {
  const tax = new RegionalTaxCalculator({ US: 0.1 }, 0.2);
  expect(tax.computeTaxCents(101, "US")).toBe(10);
  expect(tax.computeTaxCents(101, "FR")).toBe(20);
  expect(tax.computeTaxCents(0, "US")).toBe(0);
  expect(new RegionalTaxCalculator({}).computeTaxCents(100, "US")).toBe(0);
  expect(new NoTaxCalculator().computeTaxCents(100, "US")).toBe(0);
  for (const rate of [-1, 2, NaN]) {
    expect(() => new RegionalTaxCalculator({ US: rate })).toThrow(RangeError);
    expect(() => new RegionalTaxCalculator({}, rate)).toThrow(RangeError);
  }
  const asyncTax = new AsyncTaxCalculator({ getRate: async () => 0.1 });
  expect(await asyncTax.computeTaxCents(101, "US")).toBe(10);
  expect(await asyncTax.computeTaxCents(0, "US")).toBe(0);
  await expect(new AsyncTaxCalculator({ getRate: async () => 2 })
    .computeTaxCents(101, "US")).rejects.toThrow(RangeError);
  expect(totalLineWeightGrams(lines)).toBe(200);
  expect(totalLineWeightGrams([{ product: { id: "B", name: "B", unitPriceCents: 1 }, quantity: 2 }])).toBe(0);
});

test("delivery thresholds and brackets", () => {
  expect(new FreeShippingCalculator().computeShippingCents(lines, 202)).toBe(0);
  const flat = new FlatShippingCalculator(20, 202);
  expect(flat.computeShippingCents([], 0)).toBe(0);
  expect(flat.computeShippingCents(lines, 201)).toBe(20);
  expect(flat.computeShippingCents(lines, 202)).toBe(0);
  expect(new FlatShippingCalculator(20).computeShippingCents(lines, 999)).toBe(20);
  const brackets = [{ upToGrams: 300, costCents: 50 }, { upToGrams: 200, costCents: 30 }];
  const weight = new WeightBasedShippingCalculator(brackets, 90);
  expect(weight.computeShippingCents(lines, 0)).toBe(30);
  expect(weight.computeShippingCents([{ product: product(101, 201), quantity: 1 }], 0)).toBe(50);
  expect(weight.computeShippingCents([{ product: product(), quantity: 4 }], 0)).toBe(90);
  expect(new WeightBasedShippingCalculator(brackets).computeShippingCents([{ product: product(), quantity: 4 }], 0)).toBe(50);
  expect(weight.computeShippingCents([], 0)).toBe(0);
  expect(weight.computeShippingCents([{ product: product(101, 0), quantity: 1 }], 0)).toBe(0);
  expect(() => new FlatShippingCalculator(-1)).toThrow(RangeError);
  expect(() => new FlatShippingCalculator(1, -1)).toThrow(RangeError);
  expect(() => new WeightBasedShippingCalculator([])).toThrow();
  expect(() => new WeightBasedShippingCalculator([{ upToGrams: 0, costCents: 1 }])).toThrow(RangeError);
  expect(() => new WeightBasedShippingCalculator([{ upToGrams: 1, costCents: -1 }])).toThrow(RangeError);
  expect(() => new WeightBasedShippingCalculator(brackets, -1)).toThrow(RangeError);
});

test("refreshed lines retain identity without modifying inputs", async () => {
  const refreshed = await refreshPrices(lines, { fetchPriceCents: async (id) => {
    expect(id).toBe("A");
    return 250;
  } });
  expect(refreshed).toEqual([{ product: product(250), quantity: 2 }]);
  expect(lines[0].product.unitPriceCents).toBe(101);
  expect(refreshed[0].product).not.toBe(lines[0].product);
  for (const invalid of [-1, 0.5, NaN]) {
    await expect(refreshPrices(lines, { fetchPriceCents: async () => invalid })).rejects.toThrow(RangeError);
  }
  expect(await refreshPrices([], { fetchPriceCents: async () => 1 })).toEqual([]);
});

test("line lifecycle and defensive snapshots", () => {
  const cart = new Cart();
  cart.add(product(), 2);
  cart.add(product(), 3);
  expect(cart.itemCount).toBe(5);
  const snapshot = cart.snapshot();
  snapshot[0].quantity = 90;
  snapshot[0].product.unitPriceCents = 999;
  expect(cart.itemCount).toBe(5);
  expect(cart.totals().subtotalCents).toBe(505);
  cart.updateQuantity("A", 2);
  expect(cart.itemCount).toBe(2);
  cart.updateQuantity("A", 0);
  expect(cart.snapshot()).toEqual([]);
  expect(cart.remove("missing")).toBe(false);
  cart.add(product(), 1);
  expect(cart.remove("A")).toBe(true);
  cart.add(product(), 1);
  cart.clear();
  expect(cart.itemCount).toBe(0);
  expect(() => cart.add(product(), 0)).toThrow(RangeError);
  expect(() => cart.add(product(-1), 1)).toThrow(RangeError);
  expect(() => cart.updateQuantity("missing", 1)).toThrow();
  expect(() => cart.updateQuantity("missing", -1)).toThrow(RangeError);
});

test("discounted synchronous composition", () => {
  const cart = new Cart({
    discountPolicy: new FixedAmountDiscountPolicy(50),
    taxCalculator: new RegionalTaxCalculator({ US: 0.1 }),
    shippingCalculator: new FlatShippingCalculator(25, 200),
  });
  cart.add(product(), 2);
  expect(cart.totals()).toEqual({
    subtotalCents: 202, discountCents: 50, taxCents: 15, shippingCents: 25, totalCents: 192,
  });
  for (const [discount, expected] of [[-10, 202], [999, 0]]) {
    const clamped = new Cart({ discountPolicy: { computeDiscountCents: () => discount } });
    clamped.add(product(), 2);
    expect(clamped.totals().totalCents).toBe(expected);
  }
});

test("checkout refresh and asynchronous composition", async () => {
  const cart = new Cart({
    region: "FR", discountPolicy: new FixedAmountDiscountPolicy(50),
    shippingCalculator: new FlatShippingCalculator(20),
  });
  cart.add(product(), 2);
  const result = await cart.checkout({
    priceFetcher: { fetchPriceCents: async () => 250 },
    inventoryChecker: { check: async (id, quantity) => {
      expect([id, quantity]).toEqual(["A", 2]);
      return { available: true, availableQuantity: 2 };
    } },
    asyncTaxProvider: { getRate: async (region) => {
      expect(region).toBe("FR");
      return 0.1;
    } },
  });
  expect(result.totals).toEqual({
    subtotalCents: 500, discountCents: 50, taxCents: 45, shippingCents: 20, totalCents: 515,
  });
  expect(result.lines).toEqual([{ product: product(250), quantity: 2 }]);
  expect(cart.snapshot()).toEqual([{ product: product(), quantity: 2 }]);
  expect((await cart.checkout()).totals.totalCents).toBe(172);
});

test("unavailable inventory reports the denied product", async () => {
  const cart = new Cart();
  cart.add(product(), 2);
  await expect(cart.checkout({ inventoryChecker: { check: async () => ({
    available: false, reason: "sold out",
  }) } })).rejects.toMatchObject({
    name: "InventoryError", message: "sold out", productId: "A", requested: 2, available: undefined,
  });
});

test("partial stock cannot satisfy an order", async () => {
  const cart = new Cart();
  cart.add(product(), 2);
  const checkout = () => cart.checkout({ inventoryChecker: { check: async () => ({
    available: true, availableQuantity: 1,
  }) } });
  await expect(checkout()).rejects.toBeInstanceOf(InventoryError);
  await expect(checkout()).rejects.toMatchObject({ productId: "A", requested: 2, available: 1 });
});

test("checkout propagates each service rejection", async () => {
  const cart = new Cart();
  cart.add(product(), 2);
  await expect(cart.checkout({ priceFetcher: {
    fetchPriceCents: async () => { throw failure; },
  } })).rejects.toBe(failure);
  await expect(cart.checkout({ inventoryChecker: {
    check: async () => { throw failure; },
  } })).rejects.toBe(failure);
  await expect(cart.checkout({ asyncTaxProvider: {
    getRate: async () => { throw failure; },
  } })).rejects.toBe(failure);
  expect(cart.itemCount).toBe(2);
});
