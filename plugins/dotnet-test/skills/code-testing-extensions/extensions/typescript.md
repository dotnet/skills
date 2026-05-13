# TypeScript Extension

Language-specific guidance for TypeScript (and JavaScript) test generation.

## Rule #1: Investigate the Repo First

Before writing any test or running any command, read:

1. **Existing tests** — find `*.test.ts` / `*.spec.ts` files and copy their style (imports, describe/it vs test, assertion patterns, mock approach)
2. **`package.json`** — `scripts.test`, `devDependencies`, `type` field
3. **Config files** — `tsconfig.json`, `jest.config.*`, `vitest.config.*`, `eslint.config.*`

Use the repo's existing test runner and conventions — do not switch frameworks. If multiple runners are configured, follow whichever `scripts.test` invokes. Only introduce a framework if the repo has no tests at all.

## Package Manager Detection

Detect the package manager from lockfiles and use it consistently for **all** commands:

| Indicator | Manager | Run script | Execute binary |
|-----------|---------|------------|----------------|
| `pnpm-lock.yaml` | pnpm | `pnpm test` | `pnpm exec <tool>` |
| `yarn.lock` | Yarn | `yarn test` | `yarn <tool>` |
| `bun.lockb` / `bun.lock` | Bun | `bun test` | `bunx <tool>` |
| `package-lock.json` or none | npm | `npm test` | `npx <tool>` |

Use `<exec>` below as shorthand for the detected exec command.

## Build Commands

| Scope | Command |
|-------|---------|
| Type check | `<exec> tsc --noEmit` or the repo's `typecheck` script |
| Build (if configured) | The repo's `build` script |

Many projects don't need an explicit build step — the test runner handles transpilation.

## Test Commands

Detect the runner from `devDependencies` and `scripts.test`. Always prefer the repo's test script first.

| Runner | Run once | Filter by file | Filter by name |
|--------|----------|----------------|----------------|
| **Jest** | `<exec> jest` | `<exec> jest path/to/file` | `<exec> jest -t "name"` |
| **Vitest** | `<exec> vitest run` | `<exec> vitest run path/to/file` | `<exec> vitest run -t "name"` |
| **Mocha** | `<exec> mocha` | (use config or positional args) | `<exec> mocha --grep "name"` |

- **Always use `vitest run`** (not bare `vitest`) — bare `vitest` starts watch mode
- **Never use `--watch`** — the agent must not start interactive/watch mode
- For Jest: `--bail` to stop on first failure, `--verbose` for detail
- Mocha `--grep` filters by **test name**, not file path

## Lint Command

Use the repo's lint script first. Otherwise detect from `devDependencies` and config:

- `eslint.config.*` or `.eslintrc.*` → `<exec> eslint --fix path/to/file.ts`
- `prettier` → `<exec> prettier --write path/to/file.ts`
- `biome.json` → `<exec> biome check --write path/to/file.ts`

## Project Layout and Imports

| Layout | Import Style |
|--------|-------------|
| Colocated (`src/module.test.ts`) | `import { X } from './module'` |
| `__tests__/` dir | `import { X } from '../module'` |
| Top-level `tests/` | `import { X } from '../src/module'` |

- **Match existing test imports** — copy path style from neighboring tests
- If `tsconfig.json` has `paths` aliases (e.g., `@/`), use them in tests too
- For monorepos: import from the package name, not relative cross-package paths
- For monorepo workspaces (Nx, Turborepo, Lerna): run tests via the workspace tool (`nx test <project>`, `turbo test`), not from a random package directory

## Test File Naming

- Match existing convention — check for `.test.ts` vs `.spec.ts`
- Jest/Vitest default: `*.test.ts`, `*.spec.ts`, or files inside `__tests__/`
- Place test files to mirror the existing project pattern

## Common Errors

| Error | Fix |
|-------|-----|
| `Cannot find module 'X'` | Check existing imports for correct paths; verify `tsconfig.json` `paths`; check `moduleNameMapper` (Jest) or `resolve.alias` (Vitest) |
| `TS2305: has no exported member` | Verify the exact export name from the source file |
| `TS2345: type not assignable` | Match the expected type; use type assertion only for mock objects |
| `SyntaxError: Unexpected token` / `Jest encountered an unexpected token` | Verify TS transform config (`ts-jest`, `@swc/jest`, or Vitest handles natively) |
| `ReferenceError: describe is not defined` | Vitest: import from `vitest` or set `globals: true` in config; Jest: ensure tests run under Jest not bare `node` |
| `Cannot use import statement outside a module` / `ERR_REQUIRE_ESM` | ESM/CJS mismatch — align runner config with the project's module system (see ESM section); do **not** blindly set `"type": "module"` |
| `ReferenceError: document is not defined` | Set test environment: `testEnvironment: 'jsdom'` (Jest) or `environment: 'jsdom'` (Vitest) |
| `jest.mock() ... out-of-scope variables` | Keep `jest.mock()` at top level; don't reference variables declared after the mock call (Jest hoists mocks) |
| `Cannot find module '@/...'` | Mirror the project's alias config in the test runner's module resolution |
| `Warning: not wrapped in act(...)` | Await async UI updates using the repo's existing pattern (`waitFor`, `act`) |

## ESM vs CommonJS

Check these signals to determine the project's module system:

- `"type": "module"` in `package.json` → ESM
- `"module": "ESNext"` or `"NodeNext"` in `tsconfig.json` → ESM output (but not sufficient alone)
- `.mjs`/`.mts` extensions → ESM files

If the test runner fails with ESM errors, align the runner's config with the project's module system. **Do not change `package.json` `type` field** — align the test runner to match whatever the project uses:

- **Jest**: `--experimental-vm-modules` + `ts-jest` with `useESM: true`, or `@swc/jest`
- **Vitest**: handles ESM natively
- **Mocha**: `--loader ts-node/esm`

## Mocking Rules

- Prefer dependency injection over module mocking
- Use typed mocks: `jest.Mocked<T>`, `vi.mocked(obj)`, or `Partial<T>` with `as T`
- Jest: `jest.mock()` is hoisted — keep at top level, don't close over local variables
- Vitest: `vi.mock()` follows the same hoisting rules
- If a test needs more than 3–4 mocks, flag it as a design smell
- Mock reset: rely on `clearMocks`/`restoreMocks` config if present; otherwise reset in `beforeEach`

## Framework-Specific Notes

- **React/Preact**: use `@testing-library/react`, wrap with necessary providers (router, query client, theme) matching existing test setup
- **Express/Koa**: use `supertest` for HTTP testing if the repo already uses it
- **NestJS**: build testing module with `Test.createTestingModule` — don't instantiate controllers directly

## Dependency Installation (Last Resort)

Only install packages after investigation confirms they are missing. Use the detected package manager:

```
<manager> add --save-dev jest ts-jest @types/jest
<manager> add --save-dev vitest
```

Never install test infrastructure that conflicts with what the repo already uses.

## Skip Coverage Tools

Do not configure or run coverage tools (istanbul, c8, `vitest --coverage`). Coverage is measured separately by the evaluation harness.
