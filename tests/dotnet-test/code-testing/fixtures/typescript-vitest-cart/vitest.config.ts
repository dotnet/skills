import { defineConfig } from "vitest/config";

export default defineConfig({
  test: {
    include: ["tests/**/*.test.ts"],
    environment: "node",
    globals: false,
    reporters: ["default"],
    coverage: {
      // Coverage is enforced as a hard floor when --coverage is passed: the run
      // fails when line / statement / function coverage is below 80% (branch
      // coverage 70%). The agent must plan its tests to cover every module
      // (cart / pricing / tax / shipping / inventory / product) — incidental
      // coverage from a few happy-path tests will not clear the bar.
      provider: "v8",
      reporter: ["lcov", "text"],
      include: ["src/**/*.ts"],
      exclude: ["src/index.ts"],
      thresholds: {
        lines: 80,
        statements: 80,
        functions: 80,
        branches: 70,
      },
    },
  },
});
