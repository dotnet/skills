# Go Pipeline Examples

Concrete input→output examples for the test generation pipeline targeting a Go codebase. These show what each pipeline phase produces for a small package.

## Source Under Test

A simple `InvoiceService` in a Go module:

```text
go.mod                                 (module github.com/contoso/billing)
internal/billing/
  invoice.go
  invoice_repository.go               (defines the InvoiceRepository interface)
  invoice_service.go
```

```go
// internal/billing/invoice_service.go
package billing

import (
    "context"
    "errors"
    "fmt"
    "math"
    "time"
)

type InvoiceService struct {
    repository InvoiceRepository
    now        func() time.Time
}

func NewInvoiceService(repo InvoiceRepository) *InvoiceService {
    return &InvoiceService{repository: repo, now: time.Now}
}

func (s *InvoiceService) CalculateTotal(invoice *Invoice) (float64, error) {
    if invoice == nil {
        return 0, errors.New("invoice must not be nil")
    }
    if len(invoice.LineItems) == 0 {
        return 0, errors.New("invoice has no line items")
    }
    var subtotal float64
    for _, li := range invoice.LineItems {
        subtotal += float64(li.Quantity) * li.UnitPrice
    }
    tax := subtotal * invoice.TaxRate
    return math.Round((subtotal+tax)*100) / 100, nil
}

func (s *InvoiceService) GetByID(ctx context.Context, id int) (*Invoice, error) {
    invoice, err := s.repository.Find(ctx, id)
    if err != nil {
        return nil, err
    }
    if invoice == nil {
        return nil, fmt.Errorf("invoice %d not found", id)
    }
    return invoice, nil
}

func (s *InvoiceService) MarkAsPaid(ctx context.Context, id int) error {
    invoice, err := s.repository.Find(ctx, id)
    if err != nil {
        return err
    }
    if invoice == nil {
        return fmt.Errorf("invoice %d not found", id)
    }
    if invoice.Status == StatusPaid {
        return errors.New("invoice is already paid")
    }
    invoice.Status = StatusPaid
    invoice.PaidDate = s.now()
    return s.repository.Update(ctx, invoice)
}
```

## Sample Research Output

What `code-testing-researcher` produces in `.testagent/research.md`:

```markdown
# Test Generation Research

## Project Overview
- **Path**: /work/billing
- **Language**: Go 1.22 (from go.mod)
- **Module**: github.com/contoso/billing
- **Test Framework**: standard `testing` package (no testify/gomock detected in go.sum)

## Coverage Baseline
- **Initial Line Coverage**: unknown
- **Strategy**: broad
- **Existing Test Count**: 0 tests across 0 files

## Build & Test Commands
- **Vet**: `go vet ./...`
- **Build**: `go build ./...`
- **Compile tests**: `go test -count=1 -run=^$ ./internal/billing`
- **Test**: `go test -count=1 ./internal/billing`

## Project Structure
- Source: `internal/billing/`
- Tests: none

## Files to Test

### High Priority
| File | Functions | Testability | Notes |
|------|-----------|-------------|-------|
| internal/billing/invoice_service.go | InvoiceService.CalculateTotal, GetByID, MarkAsPaid | High | Uses InvoiceRepository interface — easy to fake with a hand-written struct |

## Existing Tests
- No existing tests found

## Testing Patterns
- No existing patterns; recommend white-box `package billing` tests with hand-written fake repository (no testify since the repo doesn't use it), table-driven `t.Run` subtests for CalculateTotal, and an injected `now func() time.Time` for MarkAsPaid.

## Recommendations
- Inject `now` instead of stubbing `time.Now` globally — the struct already supports it
- Place tests in `internal/billing/invoice_service_test.go` (same package, white-box)
```

## Sample Plan Output

```markdown
# Test Implementation Plan

## Overview
Generate standard-library Go tests for InvoiceService using table-driven subtests
and a hand-written fake repository. Single phase since there is only one source file.

## Commands
- **Compile tests**: `go test -count=1 -run=^$ ./internal/billing`
- **Test**: `go test -count=1 -v ./internal/billing`

## Phase 1: InvoiceService

### Files to Test

#### 1. invoice_service.go
- **Source**: `internal/billing/invoice_service.go`
- **Test File**: `internal/billing/invoice_service_test.go`

**Functions to Test**:
1. `CalculateTotal` — Table-driven
   - Happy paths: single item, multi-item, rounding
   - Error cases: nil invoice, empty line items
2. `GetByID` — happy + missing + repo error
3. `MarkAsPaid` — happy (verifies timestamp via injected clock) + already-paid + missing + repo error
```

## Sample Generated Test File

```go
// internal/billing/invoice_service_test.go
package billing

import (
    "context"
    "errors"
    "strings"
    "testing"
    "time"
)

type fakeRepository struct {
    findFunc   func(ctx context.Context, id int) (*Invoice, error)
    updateFunc func(ctx context.Context, invoice *Invoice) error
    updated    *Invoice
}

func (f *fakeRepository) Find(ctx context.Context, id int) (*Invoice, error) {
    if f.findFunc != nil {
        return f.findFunc(ctx, id)
    }
    return nil, nil
}

func (f *fakeRepository) Update(ctx context.Context, invoice *Invoice) error {
    f.updated = invoice
    if f.updateFunc != nil {
        return f.updateFunc(ctx, invoice)
    }
    return nil
}

func TestInvoiceService_CalculateTotal(t *testing.T) {
    tests := []struct {
        name    string
        invoice *Invoice
        want    float64
        wantErr string
    }{
        {
            name:    "single item with 10% tax",
            invoice: &Invoice{TaxRate: 0.10, LineItems: []LineItem{{Quantity: 1, UnitPrice: 100}}},
            want:    110,
        },
        {
            name:    "multi quantity zero tax",
            invoice: &Invoice{TaxRate: 0, LineItems: []LineItem{{Quantity: 3, UnitPrice: 25}}},
            want:    75,
        },
        {
            name:    "rounds half up",
            invoice: &Invoice{TaxRate: 0.07, LineItems: []LineItem{{Quantity: 2, UnitPrice: 9.99}}},
            want:    21.38,
        },
        {
            name:    "nil invoice errors",
            invoice: nil,
            wantErr: "invoice must not be nil",
        },
        {
            name:    "empty line items errors",
            invoice: &Invoice{TaxRate: 0, LineItems: []LineItem{}},
            wantErr: "no line items",
        },
    }
    sut := NewInvoiceService(&fakeRepository{})
    for _, tt := range tests {
        t.Run(tt.name, func(t *testing.T) {
            got, err := sut.CalculateTotal(tt.invoice)
            if tt.wantErr != "" {
                if err == nil || !strings.Contains(err.Error(), tt.wantErr) {
                    t.Fatalf("expected error containing %q, got %v", tt.wantErr, err)
                }
                return
            }
            if err != nil {
                t.Fatalf("unexpected error: %v", err)
            }
            if got != tt.want {
                t.Errorf("CalculateTotal = %v, want %v", got, tt.want)
            }
        })
    }
}

func TestInvoiceService_GetByID(t *testing.T) {
    ctx := context.Background()
    want := &Invoice{ID: 42}

    t.Run("returns invoice when found", func(t *testing.T) {
        repo := &fakeRepository{findFunc: func(_ context.Context, _ int) (*Invoice, error) { return want, nil }}
        sut := NewInvoiceService(repo)
        got, err := sut.GetByID(ctx, 42)
        if err != nil || got != want {
            t.Fatalf("got (%v, %v), want (%v, nil)", got, err, want)
        }
    })

    t.Run("returns not-found error when missing", func(t *testing.T) {
        repo := &fakeRepository{findFunc: func(_ context.Context, _ int) (*Invoice, error) { return nil, nil }}
        sut := NewInvoiceService(repo)
        _, err := sut.GetByID(ctx, 999)
        if err == nil || !strings.Contains(err.Error(), "999") {
            t.Fatalf("expected error mentioning 999, got %v", err)
        }
    })

    t.Run("propagates repository error", func(t *testing.T) {
        boom := errors.New("boom")
        repo := &fakeRepository{findFunc: func(_ context.Context, _ int) (*Invoice, error) { return nil, boom }}
        sut := NewInvoiceService(repo)
        _, err := sut.GetByID(ctx, 1)
        if !errors.Is(err, boom) {
            t.Fatalf("expected boom error, got %v", err)
        }
    })
}

func TestInvoiceService_MarkAsPaid(t *testing.T) {
    ctx := context.Background()
    fixedTime := time.Date(2025, 1, 1, 12, 0, 0, 0, time.UTC)

    t.Run("transitions pending invoice to paid", func(t *testing.T) {
        invoice := &Invoice{ID: 1, Status: StatusPending}
        repo := &fakeRepository{findFunc: func(_ context.Context, _ int) (*Invoice, error) { return invoice, nil }}
        sut := NewInvoiceService(repo)
        sut.now = func() time.Time { return fixedTime }

        if err := sut.MarkAsPaid(ctx, 1); err != nil {
            t.Fatalf("unexpected error: %v", err)
        }
        if invoice.Status != StatusPaid {
            t.Errorf("status = %v, want %v", invoice.Status, StatusPaid)
        }
        if !invoice.PaidDate.Equal(fixedTime) {
            t.Errorf("paid date = %v, want %v", invoice.PaidDate, fixedTime)
        }
        if repo.updated != invoice {
            t.Errorf("repository was not updated with the invoice")
        }
    })

    t.Run("rejects already-paid invoice", func(t *testing.T) {
        invoice := &Invoice{ID: 1, Status: StatusPaid}
        repo := &fakeRepository{findFunc: func(_ context.Context, _ int) (*Invoice, error) { return invoice, nil }}
        sut := NewInvoiceService(repo)
        if err := sut.MarkAsPaid(ctx, 1); err == nil || !strings.Contains(err.Error(), "already paid") {
            t.Fatalf("expected already-paid error, got %v", err)
        }
        if repo.updated != nil {
            t.Errorf("update should not be called for already-paid invoice")
        }
    })

    t.Run("returns not-found when missing", func(t *testing.T) {
        repo := &fakeRepository{findFunc: func(_ context.Context, _ int) (*Invoice, error) { return nil, nil }}
        sut := NewInvoiceService(repo)
        if err := sut.MarkAsPaid(ctx, 999); err == nil || !strings.Contains(err.Error(), "999") {
            t.Fatalf("expected not-found error, got %v", err)
        }
    })
}
```

## Sample Fix Cycle

When the implementer hits a compile or test-runner issue, the fixer agent diagnoses and resolves it.

**Test output:**

```text
internal/billing/invoice_service_test.go:14:6: cannot use &fakeRepository{} (value of type *fakeRepository) as type InvoiceRepository in argument to NewInvoiceService:
        *fakeRepository does not implement InvoiceRepository (missing method Update)
```

**Fixer diagnosis:** The fake repository only implemented `Find`. Go enforces full interface implementation at compile time. Add the missing method.

**Fix applied:** Add the `Update` method to `fakeRepository` (shown in the test file above).

**Rebuild + rerun:** `go test -count=1 ./internal/billing` → SUCCESS

---

**Another common cycle — wrong test selection regex:**

**Test output:**

```text
testing: warning: no tests to run
```

**Fixer diagnosis:** The agent used `go test -run TestInvoiceService_CalculateTotal/single_item` without `^...$` anchors. The Go test runner treats `-run` as a regex; the underscore makes the match too narrow.

**Fix applied:**

```bash
# Before — bare name without anchors, and an unquoted space would be parsed
# by the shell as two separate arguments
go test -run 'TestInvoiceService_CalculateTotal/single_item'

# After — anchor the subtest name, replace spaces with underscores
go test -run '^TestInvoiceService_CalculateTotal$/^single_item_with_10%_tax$' ./internal/billing
```

**Rerun:** SUCCESS

## Sample Final Report

```markdown
## Test Generation Report

**Project**: billing (Go)
**Strategy**: Direct (single source file in scope)

### Results
| Metric         | Value |
|----------------|-------|
| Tests created  | 11    |
| Tests passing  | 11    |
| Tests failing  | 0     |
| Files created  | 1     |

### Files Created
- `internal/billing/invoice_service_test.go` (3 top-level tests, 11 subtests including 5 table cases)

### Coverage
- InvoiceService.CalculateTotal — 3 happy + 2 error cases (table-driven)
- InvoiceService.GetByID — happy + missing + repo-error
- InvoiceService.MarkAsPaid — happy (with fixed clock) + already-paid + missing

### Build / Test Validation
- `go vet ./...`: ✅
- `go test -count=1 ./internal/billing`: ✅ PASS

### Next Steps
- Add fuzz test (`FuzzCalculateTotal`) if rounding correctness is critical
- Consider extracting a `Clock` interface if more time-dependent logic appears
```
