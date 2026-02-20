import { describe, it, expect } from "vitest";
import {
  bootstrapConfidenceInterval,
  isStatisticallySignificant,
  wilsonScoreInterval,
} from "../src/statistics.js";

describe("bootstrapConfidenceInterval", () => {
  it("returns zero interval for empty data", () => {
    const ci = bootstrapConfidenceInterval([], 0.95);
    expect(ci.low).toBe(0);
    expect(ci.high).toBe(0);
    expect(ci.level).toBe(0.95);
  });

  it("returns point interval for single data point", () => {
    const ci = bootstrapConfidenceInterval([0.5], 0.95);
    expect(ci.low).toBe(0.5);
    expect(ci.high).toBe(0.5);
  });

  it("produces a reasonable interval for positive data", () => {
    const data = [0.1, 0.15, 0.2, 0.12, 0.18];
    const ci = bootstrapConfidenceInterval(data, 0.95);
    expect(ci.low).toBeGreaterThan(0);
    expect(ci.high).toBeGreaterThan(ci.low);
    expect(ci.high).toBeLessThanOrEqual(0.2);
    expect(ci.level).toBe(0.95);
  });

  it("produces interval spanning zero for mixed data", () => {
    const data = [-0.1, 0.2, -0.05, 0.15, -0.08, 0.1];
    const ci = bootstrapConfidenceInterval(data, 0.95);
    // With mixed positive/negative, CI should be wide
    expect(ci.low).toBeLessThan(ci.high);
  });

  it("narrower CI with more data points", () => {
    const small = [0.1, 0.2, 0.15];
    const large = [0.1, 0.2, 0.15, 0.12, 0.18, 0.14, 0.16, 0.13, 0.17, 0.11];
    const ciSmall = bootstrapConfidenceInterval(small, 0.95);
    const ciLarge = bootstrapConfidenceInterval(large, 0.95);
    const widthSmall = ciSmall.high - ciSmall.low;
    const widthLarge = ciLarge.high - ciLarge.low;
    expect(widthLarge).toBeLessThan(widthSmall);
  });

  it("is deterministic (reproducible)", () => {
    const data = [0.1, 0.2, 0.3, 0.15, 0.25];
    const ci1 = bootstrapConfidenceInterval(data, 0.95);
    const ci2 = bootstrapConfidenceInterval(data, 0.95);
    expect(ci1.low).toBe(ci2.low);
    expect(ci1.high).toBe(ci2.high);
  });
});

describe("isStatisticallySignificant", () => {
  it("returns true when CI is entirely positive", () => {
    expect(isStatisticallySignificant({ low: 0.05, high: 0.3, level: 0.95 })).toBe(true);
  });

  it("returns true when CI is entirely negative", () => {
    expect(isStatisticallySignificant({ low: -0.4, high: -0.1, level: 0.95 })).toBe(true);
  });

  it("returns false when CI spans zero", () => {
    expect(isStatisticallySignificant({ low: -0.1, high: 0.2, level: 0.95 })).toBe(false);
  });

  it("returns false when CI is exactly at zero", () => {
    expect(isStatisticallySignificant({ low: 0, high: 0.1, level: 0.95 })).toBe(false);
  });
});

describe("wilsonScoreInterval", () => {
  it("returns zero interval for zero total", () => {
    const ci = wilsonScoreInterval(0, 0);
    expect(ci.low).toBe(0);
    expect(ci.high).toBe(0);
  });

  it("produces reasonable interval for perfect success", () => {
    const ci = wilsonScoreInterval(10, 10);
    expect(ci.low).toBeGreaterThan(0.5);
    expect(ci.high).toBe(1);
  });

  it("produces reasonable interval for no successes", () => {
    const ci = wilsonScoreInterval(0, 10);
    expect(ci.low).toBe(0);
    expect(ci.high).toBeLessThan(0.5);
  });

  it("produces interval centered around proportion", () => {
    const ci = wilsonScoreInterval(5, 10);
    expect(ci.low).toBeLessThan(0.5);
    expect(ci.high).toBeGreaterThan(0.5);
  });

  it("narrows with more samples", () => {
    const ci10 = wilsonScoreInterval(5, 10);
    const ci100 = wilsonScoreInterval(50, 100);
    const width10 = ci10.high - ci10.low;
    const width100 = ci100.high - ci100.low;
    expect(width100).toBeLessThan(width10);
  });
});
