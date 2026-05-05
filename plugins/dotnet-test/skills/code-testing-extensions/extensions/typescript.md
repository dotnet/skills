# TypeScript Extension

Language-specific guidance for TypeScript (and JavaScript) test generation.

## Package Manager Detection

Before running any commands, detect the package manager from lockfiles:

| Indicator | Manager | Run command | Exec command |
|-----------|---------|-------------|-------------|
| `pnpm-lock.yaml` | pnpm | `pnpm test` | `pnpm exec vitest` |
| `yarn.lock` | Yarn | `yarn test` | `yarn vitest` |
| `bun.lockb` or `bun.lock` | Bun | `bun test` | `bunx vitest` |
| `package-lock.json` or none | npm | `npm test` | `npx vitest` |

- **Always prefer the project's existing scripts** (`npm test`, `pnpm test`, etc.) over raw tool CLIs
- **Do not add a new test framework if one already exists** — follow the repo's established choices
- For monorepos (workspaces), run commands from the package directory, not the root

## Build Commands

| Scope | Command |
|-------|---------|
| Type check (project-level) | `npx tsc --noEmit` or `npm run typecheck` |
| Build (if configured) | `npm run build` |

- Check `package.json` `scripts` for the project's preferred build/typecheck command
- Many projects don't need an explicit build step — the test runner handles transpilation
- If `tsconfig.json` exists, TypeScript is in use; check `strict` mode and `target` settings

## Test Commands

Detect the test runner from `package.json` `devDependencies` and `scripts.test`:

| Runner | All tests | Filtered | Watch mode |
|--------|-----------|----------|------------|
| **Jest** | `npx jest` | `npx jest --testPathPattern="module"` | `npx jest --watch` |
| **Vitest** | `npx vitest run` | `npx vitest run path/to/file` | `npx vitest` |
| **Mocha** | `npx mocha` | `npx mocha --grep "pattern"` | `npx mocha --watch` |

- Prefer `npm test` or `npm run test` (or equivalent for detected package manager) if it's configured in `package.json`
- Use `npx vitest run` (not `npx vitest`) to run once without watch mode
- For Jest: use `--verbose` for detailed output, `--bail` to stop on first failure
- For Jest: filter by test name with `npx jest path/to/file.test.ts -t "test name"`
- For Vitest: use `--reporter=verbose` for detailed output
- For Vitest: filter by test name with `npx vitest run path/to/file.test.ts -t "test name"`
- Mocha should almost always be invoked via the project's existing script/config — direct CLI only if existing tests already do that

## Lint Command

```bash
# ESLint (most common)
npx eslint path/to/test_file.ts --fix

# Prettier (formatting)
npx prettier --write path/to/test_file.ts

# Biome (all-in-one)
npx biome check --write path/to/test_file.ts
```

- Detect which tools the project uses from `package.json` `devDependencies` and config files (`.eslintrc.*`, `prettier.config.*`, `biome.json`)
- Run `npm run lint -- --fix` if the project has a lint script configured

## Dependency Validation

Before writing test code, verify test infrastructure is present:

1. **Test runner**: Check `package.json` `devDependencies` for `jest`, `vitest`, `mocha`, etc.
2. **Type definitions**: For Jest, ensure `@types/jest` is installed; Vitest includes its own types
3. **TypeScript support**: Jest needs `ts-jest` or `@swc/jest`; Vitest handles TS natively
4. **Assertion library**: Jest/Vitest have built-in `expect`; Mocha typically uses `chai`

If imports fail or tests won't run:

```bash
# Jest setup
npm install --save-dev jest ts-jest @types/jest

# Vitest setup
npm install --save-dev vitest

# Mocha + Chai setup
npm install --save-dev mocha chai @types/mocha @types/chai ts-node
```

## Common Errors

| Error | Meaning | Fix |
|-------|---------|-----|
| `Cannot find module 'X'` | Import path wrong or package not installed | Fix the import path or `npm install` the package |
| `TS2305: Module has no exported member` | Named export doesn't exist | Check the source file's actual exports |
| `TS2307: Cannot find module` | Missing module or type declarations | Install `@types/package` or check `tsconfig.json` paths |
| `TS2345: Argument type not assignable` | Type mismatch in function call | Match the expected type or use type assertion |
| `TS2339: Property does not exist on type` | Wrong property name or type | Verify property name against the source interface/class |
| `TS7006: Parameter implicitly has 'any' type` | Missing type annotation (strict mode) | Add explicit type annotations |
| `SyntaxError: Unexpected token` | Test runner can't parse TypeScript | Configure `ts-jest`, `@swc/jest`, or use Vitest which handles TS natively |
| `ReferenceError: describe is not defined` | Test globals not available | For Vitest: import from `vitest` or set `globals: true` in config; for Jest: ensure tests run under Jest (not `node`); for Mocha: check test bootstrap |
| `ERR_REQUIRE_ESM` / `Cannot use import statement outside a module` | ESM/CJS mismatch | Set `"type": "module"` in `package.json`, or configure the test runner's transform/loader — see ESM section below |
| `ReferenceError: document is not defined` | Code uses browser APIs | Configure test environment: `testEnvironment: 'jsdom'` (Jest) or `environment: 'jsdom'` (Vitest) |

## Project Layout Detection

| Layout | Test Location | Import Style |
|--------|--------------|-------------|
| Colocated | `src/module.test.ts` next to `src/module.ts` | `import { X } from './module'` |
| Separate `__tests__` | `src/__tests__/module.test.ts` | `import { X } from '../module'` |
| Top-level `tests/` | `tests/module.test.ts` | `import { X } from '../src/module'` |

- Check existing test files to match the project's convention
- If `tsconfig.json` has `paths` aliases (e.g., `@/`), use them in test imports
- For monorepos, import from the package name, not relative paths across packages

## Test File Naming

- Jest default: `*.test.ts`, `*.test.tsx`, `*.spec.ts`, `*.spec.tsx`, or files inside `__tests__/`
- Vitest default: same as Jest
- Match the existing project convention — check for `.test.` vs `.spec.` usage
- Place test files to mirror source structure

## Jest Template

```typescript
import { ClassName } from '../module';

describe('ClassName', () => {
  let sut: ClassName;

  beforeEach(() => {
    sut = new ClassName();
  });

  describe('methodName', () => {
    it('returns expected result for valid input', () => {
      // Arrange
      const input = 'test';

      // Act
      const result = sut.methodName(input);

      // Assert
      expect(result).toBe(expected);
    });

    it.each([
      { a: 2, b: 3, expected: 5 },
      { a: -1, b: 1, expected: 0 },
      { a: 0, b: 0, expected: 0 },
    ])('add($a, $b) returns $expected', ({ a, b, expected }) => {
      expect(sut.add(a, b)).toBe(expected);
    });

    it('throws on invalid input', () => {
      expect(() => sut.methodName(null!)).toThrow('must not be null');
    });
  });
});
```

## Vitest Template

```typescript
import { describe, it, expect, beforeEach } from 'vitest';
import { ClassName } from '../module';

describe('ClassName', () => {
  let sut: ClassName;

  beforeEach(() => {
    sut = new ClassName();
  });

  describe('methodName', () => {
    it('returns expected result for valid input', () => {
      const result = sut.methodName('test');
      expect(result).toBe(expected);
    });

    it.each([
      { a: 2, b: 3, expected: 5 },
      { a: -1, b: 1, expected: 0 },
    ])('add($a, $b) returns $expected', ({ a, b, expected }) => {
      expect(sut.add(a, b)).toBe(expected);
    });
  });
});
```

## Mocking Guidelines

### Jest

```typescript
// Manual mock
const mockRepo = {
  find: jest.fn().mockResolvedValue({ id: 1, name: 'test' }),
  save: jest.fn(),
};
const sut = new Service(mockRepo as unknown as Repository);

// Module mock
jest.mock('../repository', () => ({
  Repository: jest.fn().mockImplementation(() => ({
    find: jest.fn().mockResolvedValue({ id: 1 }),
  })),
}));

// Spy on existing method
jest.spyOn(sut, 'methodName').mockReturnValue('mocked');
```

### Vitest

```typescript
import { vi } from 'vitest';

const mockRepo = {
  find: vi.fn().mockResolvedValue({ id: 1, name: 'test' }),
  save: vi.fn(),
};
const sut = new Service(mockRepo as unknown as Repository);

// Module mock
vi.mock('../repository', () => ({
  Repository: vi.fn().mockImplementation(() => ({
    find: vi.fn().mockResolvedValue({ id: 1 }),
  })),
}));
```

- Prefer dependency injection over module mocking — cleaner and less brittle
- Prefer typed mock helpers (`jest.Mocked<T>`, `vi.mocked`) or `Pick<T, 'method1' | 'method2'>` over `as unknown as Type`
- Use `as unknown as Type` only as a last resort for partial mocks
- For complex interfaces, consider a factory helper to reduce mock boilerplate
- If a test needs more than 3–4 mocks, flag it as a design smell
- Mock reset: if config enables `clearMocks`/`mockReset`, rely on it; otherwise reset explicitly in `beforeEach`

## Async Tests

```typescript
// Jest / Vitest — both support async/await natively
it('fetches data successfully', async () => {
  const result = await sut.fetchData(42);
  expect(result).toBeDefined();
});

// Testing rejected promises
it('throws on not found', async () => {
  await expect(sut.fetchData(-1)).rejects.toThrow('not found');
});
```

## TypeScript-Specific Considerations

- **Access modifiers**: TypeScript `private` and `protected` are compile-time only — they don't exist at runtime. Tests can technically access them but **should not** — test through the public API
- **Interfaces**: When the source defines interfaces, mock against the interface type, not the concrete class
- **Enums**: Import and use enum values directly in test assertions — don't hardcode the underlying numbers
- **Generics**: Provide explicit type arguments when instantiating generic classes in tests for clarity
- **Type assertions in tests**: Use `as Type` sparingly and only for test setup (mock objects), never to silence legitimate type errors

## ESM vs CommonJS

Many TypeScript projects are transitioning to ESM. Watch for these signals:

- `"type": "module"` in `package.json` → ESM project
- `"module": "ESNext"` or `"NodeNext"` in `tsconfig.json` → ESM output
- `.mjs`/`.mts` file extensions → ESM files

If the test runner fails with ESM errors:

- **Jest**: May need `--experimental-vm-modules` flag and ESM-compatible transform (`ts-jest` with `useESM: true`, or `@swc/jest`)
- **Vitest**: Handles ESM natively — prefer Vitest for ESM projects if no runner is established
- **Mocha**: Needs `--loader ts-node/esm` or similar loader configuration

Check the project's existing test configuration before changing module settings.

## Framework Detection Priority

When the project has multiple test runners configured, prefer in this order:

1. Whatever `npm test` / `scripts.test` runs
2. Vitest (faster, better TS support)
3. Jest (most widely used)
4. Mocha + Chai (older projects)

## Skip Coverage Tools

Do not configure or run code coverage measurement tools (istanbul, c8, vitest --coverage). Coverage is measured separately by the evaluation harness.
