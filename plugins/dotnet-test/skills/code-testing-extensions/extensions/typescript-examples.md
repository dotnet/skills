# TypeScript Pipeline Examples

Concrete input→output examples for the test generation pipeline targeting a TypeScript codebase using Vitest. These show what each pipeline phase produces for a small project.

> Jest, Mocha, and node:test follow the same shape. Replace `vi.fn()` / `vi.mock()` with `jest.fn()` / `jest.mock()` (Jest) or hand-written stubs (node:test/Mocha) and adjust the runner command accordingly.

## Source Under Test

A simple `InvoiceService` in a TypeScript library using Vitest:

```text
src/
  invoiceService.ts
  invoice.ts
  invoiceRepository.ts
  index.ts                (re-exports public API)
package.json
tsconfig.json
vitest.config.ts
package-lock.json         (committed for reproducible installs)
```

```typescript
// src/invoiceService.ts
import { Invoice, InvoiceStatus } from "./invoice";
import { InvoiceRepository } from "./invoiceRepository";

export class InvoiceService {
  constructor(private readonly repository: InvoiceRepository) {}

  calculateTotal(invoice: Invoice): number {
    if (invoice == null) throw new TypeError("invoice must not be null");
    if (invoice.lineItems.length === 0) {
      throw new Error("Invoice has no line items.");
    }

    const subtotal = invoice.lineItems.reduce(
      (acc, li) => acc + li.quantity * li.unitPrice,
      0,
    );
    const tax = subtotal * invoice.taxRate;
    return roundTo2(subtotal + tax);
  }

  async getById(id: number): Promise<Invoice> {
    const invoice = await this.repository.find(id);
    if (invoice == null) {
      throw new Error(`Invoice ${id} not found.`);
    }
    return invoice;
  }

  async markAsPaid(id: number): Promise<void> {
    const invoice = await this.repository.find(id);
    if (invoice == null) {
      throw new Error(`Invoice ${id} not found.`);
    }
    if (invoice.status === InvoiceStatus.Paid) {
      throw new Error("Invoice is already paid.");
    }
    invoice.status = InvoiceStatus.Paid;
    invoice.paidDate = new Date();
    await this.repository.update(invoice);
  }
}

function roundTo2(n: number): number {
  return Math.round((n + Number.EPSILON) * 100) / 100;
}
```

## Sample Research Output

What `code-testing-researcher` produces in `.testagent/research.md`:

```markdown
# Test Generation Research

## Project Overview
- **Path**: /work/contoso-billing
- **Language**: TypeScript 5.4
- **Module system**: ESM (`"type": "module"` in package.json)
- **Test Framework**: Vitest 1.x (detected via `vitest.config.ts` and `devDependencies.vitest`)
- **Package Manager**: npm (lockfile = `package-lock.json`)

## Coverage Baseline
- **Initial Line Coverage**: unknown
- **Strategy**: broad
- **Existing Test Count**: 0 tests across 0 files

## Build & Test Commands
- **Install**: `npm ci`
- **Type-check**: `npx tsc --noEmit`
- **Test**: `npx vitest run` (NEVER bare `vitest` — that starts watch mode)
- **Lint**: none configured

## Project Structure
- Source: `src/`
- Tests: none (will colocate as `src/invoiceService.test.ts` to match Vitest defaults)

## Files to Test

### High Priority
| File | Classes/Functions | Testability | Notes |
|------|-------------------|-------------|-------|
| src/invoiceService.ts | InvoiceService: calculateTotal, getById, markAsPaid | High | Core business logic, repository dependency needs mocking |

### Low Priority / Skip
| File | Reason |
|------|--------|
| src/invoice.ts | Type definitions and enum |
| src/invoiceRepository.ts | Interface only |
| src/index.ts | Re-export barrel |

## Existing Tests
- No existing tests found

## Testing Patterns
- No existing patterns; recommend `describe`/`it` blocks, `vi.fn()` stubs for the repository interface, and `it.each` for table-driven cases.

## Recommendations
- Co-locate test next to source (`src/invoiceService.test.ts`) — matches Vitest defaults and avoids reaching into `../src/`
- Use a fake-timers helper (`vi.useFakeTimers()`) to control `new Date()` in `markAsPaid`
- Use a type-narrowed mock object (`{ find: vi.fn(), update: vi.fn() } satisfies InvoiceRepository`) rather than full module mocking
```

## Sample Plan Output

What `code-testing-planner` produces in `.testagent/plan.md`:

```markdown
# Test Implementation Plan

## Overview
Generate Vitest tests for InvoiceService, covering all three public methods
across happy path, edge case, and error scenarios. Single phase since there is
only one source file.

## Commands
- **Install**: `npm ci`
- **Type-check**: `npx tsc --noEmit`
- **Test (file-scoped during dev)**: `npx vitest run src/invoiceService.test.ts`
- **Test (full)**: `npx vitest run`

## Phase Summary
| Phase | Focus | Files | Est. Tests |
|-------|-------|-------|------------|
| 1 | InvoiceService | 1 | 9-12 |

---

## Phase 1: InvoiceService

### Overview
Cover all public methods of InvoiceService. `calculateTotal` is pure logic tested
with `it.each`. Async methods require a fake repository.

### Files to Test

#### 1. invoiceService.ts
- **Source**: `src/invoiceService.ts`
- **Test File**: `src/invoiceService.test.ts`

**Methods to Test**:
1. `calculateTotal` — Pure calculation logic
   - Happy path: single line item returns quantity × price + tax
   - Happy path: multiple line items summed correctly
   - Edge case: zero tax rate returns subtotal only
   - Error case: null invoice throws TypeError
   - Error case: empty line items throws Error

2. `getById` — Repository lookup
   - Happy path: existing ID returns invoice
   - Error case: missing ID rejects with Error

3. `markAsPaid` — State transition
   - Happy path: pending invoice transitions to Paid with `paidDate` set
   - Error case: already-paid rejects with Error
   - Error case: missing ID rejects with Error

### Success Criteria
- [ ] Test file created at `src/invoiceService.test.ts`
- [ ] `npx tsc --noEmit` succeeds
- [ ] `npx vitest run` reports all tests passed
- [ ] No real network/timers — repository is a `vi.fn()` fake, `new Date()` is controlled via fake timers
```

## Sample Generated Test File

What `code-testing-implementer` produces:

```typescript
// src/invoiceService.test.ts
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { Invoice, InvoiceStatus } from "./invoice";
import type { InvoiceRepository } from "./invoiceRepository";
import { InvoiceService } from "./invoiceService";

function makeRepository(): InvoiceRepository & { find: ReturnType<typeof vi.fn>; update: ReturnType<typeof vi.fn> } {
  return {
    find: vi.fn(),
    update: vi.fn(),
  };
}

describe("InvoiceService", () => {
  let repository: ReturnType<typeof makeRepository>;
  let sut: InvoiceService;

  beforeEach(() => {
    repository = makeRepository();
    sut = new InvoiceService(repository);
  });

  // --- calculateTotal ---

  describe("calculateTotal", () => {
    it.each([
      { quantity: 1, unitPrice: 100, taxRate: 0.1, expected: 110 },
      { quantity: 3, unitPrice: 25, taxRate: 0, expected: 75 },
      { quantity: 2, unitPrice: 9.99, taxRate: 0.07, expected: 21.38 },
    ])(
      "returns $expected for $quantity × $unitPrice with tax $taxRate",
      ({ quantity, unitPrice, taxRate, expected }) => {
        const invoice: Invoice = {
          id: 1,
          status: InvoiceStatus.Pending,
          taxRate,
          lineItems: [{ quantity, unitPrice }],
        };

        expect(sut.calculateTotal(invoice)).toBe(expected);
      },
    );

    it("throws TypeError when invoice is null", () => {
      expect(() => sut.calculateTotal(null as unknown as Invoice)).toThrow(TypeError);
    });

    it("throws when line items are empty", () => {
      const invoice: Invoice = {
        id: 1,
        status: InvoiceStatus.Pending,
        taxRate: 0,
        lineItems: [],
      };

      expect(() => sut.calculateTotal(invoice)).toThrow("no line items");
    });
  });

  // --- getById ---

  describe("getById", () => {
    it("returns the invoice for an existing id", async () => {
      const expected: Invoice = { id: 42, status: InvoiceStatus.Pending, taxRate: 0, lineItems: [] };
      repository.find.mockResolvedValue(expected);

      await expect(sut.getById(42)).resolves.toBe(expected);
      expect(repository.find).toHaveBeenCalledWith(42);
    });

    it("rejects with Error when the id is missing", async () => {
      repository.find.mockResolvedValue(null);

      await expect(sut.getById(999)).rejects.toThrow(/999/);
    });
  });

  // --- markAsPaid ---

  describe("markAsPaid", () => {
    beforeEach(() => {
      vi.useFakeTimers();
      vi.setSystemTime(new Date("2025-01-01T12:00:00.000Z"));
    });
    afterEach(() => {
      vi.useRealTimers();
    });

    it("transitions a pending invoice to Paid with paidDate set", async () => {
      const invoice: Invoice = {
        id: 1,
        status: InvoiceStatus.Pending,
        taxRate: 0,
        lineItems: [],
      };
      repository.find.mockResolvedValue(invoice);
      repository.update.mockResolvedValue(undefined);

      await sut.markAsPaid(1);

      expect(invoice.status).toBe(InvoiceStatus.Paid);
      expect(invoice.paidDate).toEqual(new Date("2025-01-01T12:00:00.000Z"));
      expect(repository.update).toHaveBeenCalledWith(invoice);
    });

    it("rejects when the invoice is already paid", async () => {
      const invoice: Invoice = {
        id: 1,
        status: InvoiceStatus.Paid,
        taxRate: 0,
        lineItems: [],
      };
      repository.find.mockResolvedValue(invoice);

      await expect(sut.markAsPaid(1)).rejects.toThrow("already paid");
      expect(repository.update).not.toHaveBeenCalled();
    });

    it("rejects when the id is missing", async () => {
      repository.find.mockResolvedValue(null);

      await expect(sut.markAsPaid(999)).rejects.toThrow(/999/);
    });
  });
});
```

## Sample Fix Cycle

When the implementer encounters a runner or type error, the fixer agent diagnoses and resolves it.

**Test output:**

```text
Error: Vitest failed to access its internal state.
One of the following is possible:
- "vitest" is imported directly without running "vitest" command
```

**Fixer diagnosis:** The agent ran `node src/invoiceService.test.ts` (or bare `vitest`, which is watch-mode). The runner must be invoked via `npx vitest run`.

**Fix applied:**

```bash
# Wrong — bare vitest starts an interactive watcher in CI
npx vitest

# Right — `run` is the one-shot command
npx vitest run
```

**Rerun:** SUCCESS

---

**Another common cycle — ESM/CJS mismatch:**

**Test output:**

```text
SyntaxError: Cannot use import statement outside a module
```

**Fixer diagnosis:** The project's `tsconfig.json` emits ESM (`"module": "NodeNext"`) but `package.json` has no `"type": "module"`. Vitest happens to handle this natively; switching to Jest would require additional configuration. The fix here is to ensure Vitest is the runner being used (as already configured in `vitest.config.ts`) and avoid recompiling test files through a separate non-ESM-aware tool.

**Fix applied:** Use `npx vitest run` (which uses esbuild internally and handles both ESM and CJS) instead of compiling with `tsc` and running the emitted `.js` directly.

**Rerun:** SUCCESS

---

**Another common cycle — wrong mock typing:**

**Build output:**

```text
src/invoiceService.test.ts:14:5 - error TS2322: Type '{ find: Mock<any, any>; }' is not assignable to type 'InvoiceRepository'.
  Property 'update' is missing in type '{ find: Mock<any, any>; }' but required in type 'InvoiceRepository'.
```

**Fixer diagnosis:** The fake repository only stubbed `find`, not `update`. The `InvoiceRepository` interface requires both. TypeScript caught this at compile time.

**Fix applied:**

```typescript
// Before
const repository = { find: vi.fn() } as InvoiceRepository;

// After — provide both methods, narrow the return type so the test code keeps autocomplete
function makeRepository(): InvoiceRepository & { find: ReturnType<typeof vi.fn>; update: ReturnType<typeof vi.fn> } {
  return { find: vi.fn(), update: vi.fn() };
}
```

**Rebuild + rerun:** SUCCESS

## Sample Final Report

What `code-testing-generator` produces at Step 9:

```markdown
## Test Generation Report

**Project**: contoso-billing (TypeScript)
**Strategy**: Direct (single source file in scope)

### Results
| Metric         | Value |
|----------------|-------|
| Tests created  | 9     |
| Tests passing  | 9     |
| Tests failing  | 0     |
| Files created  | 1     |

### Files Created
- `src/invoiceService.test.ts` (9 tests, 3 parameterized via `it.each`)

### Coverage
- InvoiceService.calculateTotal — 3 happy path, 2 error cases
- InvoiceService.getById — 1 happy path, 1 error case
- InvoiceService.markAsPaid — 1 happy path, 2 error cases

### Build / Test Validation
- Install: ✅ `npm ci`
- Type-check: ✅ `npx tsc --noEmit`
- Test run: ✅ `npx vitest run`

### Next Steps
- Add tests for any HTTP/Express adapters once they exist
- Consider property-based testing (`fast-check`) for `calculateTotal` rounding
```
