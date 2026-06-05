# TypeScript / JavaScript Test Frameworks Reference (Jest, Vitest, Mocha, Jasmine, node:test)

Reference data for analyzing JS/TS test code. Used by the polyglot test analysis skills.

## Capability tags

| Capability | Support |
|------------|---------|
| Test discovery | Strong — `*.test.ts`, `*.spec.ts`, `__tests__/` |
| Assertion detection | Strong — `expect`, `assert`, `chai` |
| Sleep/delay detection | Strong — `setTimeout`, `sleep`, `wait` helpers |
| Skip/ignore detection | Strong — `.skip`, `xit`, `xdescribe` |
| Setup/teardown detection | Strong — `beforeEach`, `afterEach`, hooks |
| Tag support | **report-only** by default — no canonical attribute; some frameworks accept `tags` option (Vitest test.options.tag) or describe-based grouping |

## Test File Identification

| Framework | File convention | Test method markers |
|-----------|----------------|---------------------|
| Jest | `*.test.ts/js/tsx/jsx`, `*.spec.*`, files in `__tests__/` | `test()`, `it()`, `describe()` |
| Vitest | `*.test.ts/js`, `*.spec.*` | `test()`, `it()`, `describe()` (same shape as Jest) |
| Mocha | `test/**/*.js` (configurable) | `it()`, `describe()` |
| Jasmine | `*Spec.js`, `*.spec.js` | `it()`, `describe()` |
| node:test | `*.test.js`, `test/**/*.js` | `test()` from `node:test` |

## Assertion APIs

| Category | Jest / Vitest (`expect`) | Mocha + Chai (`expect`) | node:test (`assert`) |
|----------|--------------------------|-------------------------|---------------------|
| Equality | `expect(x).toBe(y)` / `.toEqual()` | `expect(x).to.equal(y)` / `.deep.equal()` | `assert.strictEqual(x, y)` / `assert.deepStrictEqual()` |
| Inequality | `expect(x).not.toBe(y)` | `expect(x).to.not.equal(y)` | `assert.notStrictEqual(x, y)` |
| Truthy/Falsy | `.toBeTruthy()` / `.toBeFalsy()` | `.to.be.true` / `.to.be.false` | `assert.ok(x)` |
| Null/Undefined | `.toBeNull()` / `.toBeUndefined()` / `.toBeDefined()` | `.to.be.null` / `.to.be.undefined` | `assert.equal(x, null)` |
| Exception | `expect(() => fn()).toThrow(Error)` / `await expect(promise).rejects.toThrow()` | `expect(fn).to.throw(Error)` | `assert.throws(fn, Error)` / `await assert.rejects(promise)` |
| Type | `.toBeInstanceOf(Cls)` | `.to.be.instanceOf(Cls)` | `assert.ok(x instanceof Cls)` |
| Membership | `.toContain(item)` | `.to.include(item)` | `assert.ok(arr.includes(item))` |
| String | `.toMatch(/regex/)` / `.toContain('sub')` | `.to.match(/regex/)` | `assert.match(s, /regex/)` |
| Object shape | `.toMatchObject({...})` | `.to.deep.include({...})` | (manual) |
| Snapshot | `.toMatchSnapshot()` / `.toMatchInlineSnapshot()` | *(via plugin)* | *(via plugin)* |
| Mock calls | `expect(mock).toHaveBeenCalledWith(...)` | `sinon.assert.calledWith(...)` | (manual) |

Third-party libraries: `chai`, `should`, `sinon-chai`, `@vitest/expect`.

## Sleep/Delay Patterns

| Pattern | Example |
|---------|---------|
| setTimeout sleep | `await new Promise(r => setTimeout(r, 1000))` |
| Hard sleep helpers | `sleep(1000)`, `await delay(500)` |
| Loop wait | `while (!condition) await sleep(100)` |
| Jest fake timers | `jest.advanceTimersByTime(...)` (acceptable, not a sleep) |

## Skip/Ignore Annotations

| Framework | Skip | Focused (only) |
|-----------|------|----------------|
| Jest | `test.skip`, `it.skip`, `describe.skip`, `xit`, `xdescribe`, `xtest` | `test.only`, `fit`, `fdescribe` |
| Vitest | `test.skip`, `it.skip`, `describe.skip`, `test.todo`, `test.skipIf(cond)` | `test.only` |
| Mocha | `it.skip`, `describe.skip`, `xit`, `xdescribe` | `it.only`, `describe.only` |
| Jasmine | `xit`, `xdescribe`, `pending()` | `fit`, `fdescribe` |
| node:test | `test(name, { skip: true }, fn)`, `test.skip(...)`, `test.todo(...)` | `test(name, { only: true }, fn)` |

`.only` patterns are an anti-pattern when committed — they silently disable the rest of the suite.

## Exception Handling — Idiomatic Alternatives

```ts
// Jest / Vitest (sync):
expect(() => parseAmount(-5)).toThrow(RangeError);

// Jest / Vitest (async):
await expect(parseAmountAsync(-5)).rejects.toThrow(RangeError);

// Chai:
expect(() => parseAmount(-5)).to.throw(RangeError, /must be positive/);

// node:test:
assert.throws(() => parseAmount(-5), RangeError);
await assert.rejects(parseAmountAsync(-5), RangeError);
```

Flag `try { ... } catch (e) { /* nothing */ }` and `try { ... } catch { expect(...) }` patterns as Exception Handling smells unless the catch performs a specific assertion.

## Mystery Guest — Common JS/TS Patterns

| Indicator | What to look for |
|-----------|------------------|
| File system | `fs.readFileSync`, `fs.promises.readFile`, hard-coded absolute paths |
| Database | direct `pg.Client`, `mongodb.MongoClient` against a real DB |
| Network | `fetch`, `axios.get/post`, `http.request` without a mock adapter |
| Environment | `process.env.X` (especially without default) |
| Acceptable | `memfs`, `mock-fs`, `nock`, `msw`, `axios-mock-adapter`, `vi.mock` / `jest.mock` |

## Integration Test Markers

- Folder names: `__tests__/integration/`, `tests/e2e/`, `cypress/`, `playwright/`
- File suffix: `*.integration.test.ts`, `*.e2e.test.ts`
- `describe('Integration: …', …)` wrappers
- Playwright/Cypress/WebdriverIO usage almost always implies E2E

## Setup/Teardown

| Framework | Per-test | Per-suite |
|-----------|----------|-----------|
| Jest / Vitest / Mocha / Jasmine | `beforeEach()` / `afterEach()` | `beforeAll()` / `afterAll()` (Mocha: `before` / `after`) |
| node:test | `beforeEach(fn)` / `afterEach(fn)` from `node:test` | `before(fn)` / `after(fn)` |

## Tag/Trait Attributes (for `test-tagging`)

**Default mode: report-only.** JS/TS test frameworks generally have no canonical tag attribute. Strategies:

- **describe-based grouping** — wrap tests in `describe('@positive | OrderService', ...)` and grep the prefix.
- **Test name prefixes** — `it('[boundary] handles zero quantity', ...)`.
- **Vitest options object** — Vitest accepts arbitrary metadata on tests but no first-class tag filter.
- **Custom reporters** — projects can read JSDoc-style `@tags` and surface them.

Only switch to `auto-edit` mode when the project already follows one of these conventions (detect by sampling existing tests).

## Language-specific calibration notes

- **Async tests missing `await`** are a critical smell. `expect(promise).resolves.toBe(...)` without `await` resolves nothing and the test passes silently. Flag any unawaited promise inside a test body (linters: `@typescript-eslint/no-floating-promises`, `vitest/no-disabled-tests`).
- **Snapshot tests** count as assertions — but flag stale or always-passing snapshots (no `expect.assertions(n)` and only `toMatchSnapshot`).
- **`expect.assertions(n)`** is a useful guardrail; tests using it lock in assertion count.
- **Implicit assertion via mock matchers**: `expect(mock).toHaveBeenCalled()` is a valid assertion — do not treat as assertion-free.
- **Done callbacks** in Mocha-style tests (`it('x', (done) => { ... done(); })`) are legacy; absence of `done()` call in a callback test is a silent pass.
- **`xit`/`xdescribe`** are commits of disabled tests — flag like `[Ignore]`.
- **`.only`** committed to source is a critical smell — silently disables the rest of the file/suite.
- **describe.each / test.each** are parametrized; not duplicate tests.
- **`fail()` is removed in Jest 27+** — flag `if (cond) fail('msg')` patterns and recommend `throw new Error('msg')` or an explicit failing assertion such as `expect(value).toBe(...)` instead.
