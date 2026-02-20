import type { ConfidenceInterval } from "./types.js";

/**
 * Compute a bootstrap percentile confidence interval.
 * Resamples `data` with replacement `iterations` times,
 * computes the mean of each resample, and returns the
 * percentile-based CI at the given confidence level.
 */
export function bootstrapConfidenceInterval(
  data: number[],
  confidenceLevel: number = 0.95,
  iterations: number = 10000
): ConfidenceInterval {
  if (data.length === 0) {
    return { low: 0, high: 0, level: confidenceLevel };
  }
  if (data.length === 1) {
    return { low: data[0], high: data[0], level: confidenceLevel };
  }

  const means: number[] = [];
  for (let i = 0; i < iterations; i++) {
    let sum = 0;
    for (let j = 0; j < data.length; j++) {
      sum += data[Math.floor(seededRandom(i * data.length + j) * data.length)];
    }
    means.push(sum / data.length);
  }

  means.sort((a, b) => a - b);

  const alpha = 1 - confidenceLevel;
  const lowIdx = Math.floor((alpha / 2) * means.length);
  const highIdx = Math.floor((1 - alpha / 2) * means.length) - 1;

  return {
    low: means[Math.max(0, lowIdx)],
    high: means[Math.min(means.length - 1, highIdx)],
    level: confidenceLevel,
  };
}

/**
 * Deterministic pseudo-random for reproducible bootstrap.
 * Uses a simple splitmix-style hash.
 */
function seededRandom(seed: number): number {
  let s = seed | 0;
  s = (s + 0x9e3779b9) | 0;
  s = Math.imul(s ^ (s >>> 16), 0x85ebca6b);
  s = Math.imul(s ^ (s >>> 13), 0xc2b2ae35);
  s = s ^ (s >>> 16);
  return (s >>> 0) / 0x100000000;
}

/**
 * Check if the CI excludes zero, indicating statistical significance.
 */
export function isStatisticallySignificant(ci: ConfidenceInterval): boolean {
  return ci.low > 0 || ci.high < 0;
}

/**
 * Wilson score interval for a binomial proportion.
 * Useful for pass/fail rates with small samples.
 */
export function wilsonScoreInterval(
  successes: number,
  total: number,
  confidenceLevel: number = 0.95
): ConfidenceInterval {
  if (total === 0) {
    return { low: 0, high: 0, level: confidenceLevel };
  }

  // z-score approximation for common confidence levels
  const z = zScore(confidenceLevel);
  const p = successes / total;
  const n = total;

  const denominator = 1 + z * z / n;
  const center = (p + z * z / (2 * n)) / denominator;
  const margin =
    (z / denominator) * Math.sqrt(p * (1 - p) / n + z * z / (4 * n * n));

  return {
    low: Math.max(0, center - margin),
    high: Math.min(1, center + margin),
    level: confidenceLevel,
  };
}

function zScore(confidenceLevel: number): number {
  // Common z-scores; fall back to 1.96 for 0.95
  const table: Record<string, number> = {
    "0.9": 1.645,
    "0.95": 1.96,
    "0.99": 2.576,
  };
  return table[confidenceLevel.toString()] ?? 1.96;
}
