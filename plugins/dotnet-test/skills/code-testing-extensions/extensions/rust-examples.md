# Rust Pipeline Examples

Concrete input→output examples for the test generation pipeline targeting a Rust crate using the built-in test harness. These show what each pipeline phase produces for a small library crate.

## Source Under Test

A simple `InvoiceService` in a Rust crate:

```text
Cargo.toml
src/
  lib.rs
  invoice.rs
  invoice_repository.rs
  invoice_service.rs
```

```rust
// src/invoice_service.rs
use crate::invoice::{Invoice, InvoiceStatus};
use crate::invoice_repository::InvoiceRepository;
use std::time::SystemTime;

#[derive(Debug, PartialEq, Eq)]
pub enum InvoiceError {
    EmptyLineItems,
    NotFound(i32),
    AlreadyPaid,
    Repository(String),
}

pub struct InvoiceService<R, C>
where
    R: InvoiceRepository,
    C: Fn() -> SystemTime,
{
    repository: R,
    clock: C,
}

impl<R, C> InvoiceService<R, C>
where
    R: InvoiceRepository,
    C: Fn() -> SystemTime,
{
    pub fn new(repository: R, clock: C) -> Self {
        Self { repository, clock }
    }

    pub fn calculate_total(&self, invoice: &Invoice) -> Result<f64, InvoiceError> {
        if invoice.line_items.is_empty() {
            return Err(InvoiceError::EmptyLineItems);
        }

        let subtotal: f64 = invoice.line_items.iter().map(|item| item.quantity as f64 * item.unit_price).sum();
        Ok(((subtotal + subtotal * invoice.tax_rate) * 100.0).round() / 100.0)
    }

    pub fn get_by_id(&self, id: i32) -> Result<Invoice, InvoiceError> {
        self.repository.find(id).map_err(InvoiceError::Repository)?.ok_or(InvoiceError::NotFound(id))
    }

    pub fn mark_as_paid(&mut self, id: i32) -> Result<(), InvoiceError> {
        let mut invoice = self.get_by_id(id)?;
        if invoice.status == InvoiceStatus::Paid {
            return Err(InvoiceError::AlreadyPaid);
        }

        invoice.status = InvoiceStatus::Paid;
        invoice.paid_at = Some((self.clock)());
        self.repository.update(invoice).map_err(InvoiceError::Repository)
    }
}
```

## Sample Research Output

What `code-testing-researcher` produces in `.testagent/research.md`:

```markdown
# Test Generation Research

## Project Overview
- **Path**: /work/contoso-billing
- **Language**: Rust 1.78 (edition 2021, from Cargo.toml)
- **Crate Type**: library
- **Test Framework**: built-in Rust test harness (`#[test]`), no `mockall`/`rstest` dev-dependencies detected

## Coverage Baseline
- **Initial Line Coverage**: unknown
- **Strategy**: broad
- **Existing Test Count**: 0 tests across 0 files

## Build & Test Commands
- **Check**: `cargo check --all-targets`
- **Compile tests**: `cargo test --no-run`
- **Test**: `cargo test`
- **Single module**: `cargo test invoice_service::tests`

## Files to Test

### High Priority
| File | Types/Methods | Testability | Notes |
|------|---------------|-------------|-------|
| src/invoice_service.rs | InvoiceService: calculate_total, get_by_id, mark_as_paid | High | Generic repository trait is easy to fake; clock closure is injectable |

## Testing Patterns
- No existing patterns; recommend unit tests in `#[cfg(test)] mod tests` at the bottom of `invoice_service.rs` and a hand-written fake repository.
```

## Sample Plan Output

```markdown
# Test Implementation Plan

## Overview
Generate built-in Rust unit tests for InvoiceService covering calculation,
lookup, and paid-state transition behavior.

## Commands
- **Check**: `cargo check --all-targets`
- **Compile tests**: `cargo test --no-run`
- **Test**: `cargo test invoice_service::tests`

## Phase 1: InvoiceService

### Files to Test
- **Source**: `src/invoice_service.rs`
- **Test Location**: `#[cfg(test)] mod tests` appended to `src/invoice_service.rs`

**Methods to Test**:
1. `calculate_total` — tax, zero tax, rounding, empty-line-items error
2. `get_by_id` — existing invoice, missing invoice, repository error
3. `mark_as_paid` — success, already-paid, missing invoice
```

## Sample Generated Test File

```rust
// Appended to src/invoice_service.rs
#[cfg(test)]
mod tests {
    use super::*;
    use crate::invoice::{Invoice, InvoiceStatus, LineItem};
    use std::collections::HashMap;
    use std::time::{Duration, UNIX_EPOCH};

    #[derive(Default)]
    struct FakeRepository {
        invoices: HashMap<i32, Invoice>,
        updated: Option<Invoice>,
        find_error: Option<String>,
    }

    impl InvoiceRepository for FakeRepository {
        fn find(&self, id: i32) -> Result<Option<Invoice>, String> {
            if let Some(error) = &self.find_error {
                return Err(error.clone());
            }

            Ok(self.invoices.get(&id).cloned())
        }

        fn update(&mut self, invoice: Invoice) -> Result<(), String> {
            self.updated = Some(invoice.clone());
            self.invoices.insert(invoice.id, invoice);
            Ok(())
        }
    }

    fn make_invoice(id: i32) -> Invoice {
        Invoice {
            id,
            status: InvoiceStatus::Pending,
            tax_rate: 0.10,
            line_items: vec![LineItem { quantity: 1, unit_price: 100.0 }],
            paid_at: None,
        }
    }

    fn service_with(repository: FakeRepository) -> InvoiceService<FakeRepository, fn() -> std::time::SystemTime> {
        InvoiceService::new(repository, || UNIX_EPOCH + Duration::from_secs(123))
    }

    #[test]
    fn calculate_total_valid_line_items_returns_expected_total() {
        let cases = [
            ("single item with tax", vec![LineItem { quantity: 1, unit_price: 100.0 }], 0.10, 110.0),
            ("multi quantity zero tax", vec![LineItem { quantity: 3, unit_price: 25.0 }], 0.0, 75.0),
            ("rounds to two decimals", vec![LineItem { quantity: 2, unit_price: 9.99 }], 0.07, 21.38),
        ];
        let service = service_with(FakeRepository::default());

        for (name, line_items, tax_rate, expected) in cases {
            let mut invoice = make_invoice(1);
            invoice.line_items = line_items;
            invoice.tax_rate = tax_rate;

            let total = service.calculate_total(&invoice).unwrap_or_else(|err| panic!("{name}: unexpected error: {err:?}"));

            assert!((total - expected).abs() < 0.001, "{name}: got {total}, expected {expected}");
        }
    }

    #[test]
    fn calculate_total_empty_line_items_returns_error() {
        let service = service_with(FakeRepository::default());
        let mut invoice = make_invoice(1);
        invoice.line_items.clear();

        assert_eq!(Err(InvoiceError::EmptyLineItems), service.calculate_total(&invoice));
    }

    #[test]
    fn get_by_id_existing_invoice_returns_invoice() {
        let mut repository = FakeRepository::default();
        repository.invoices.insert(42, make_invoice(42));
        let service = service_with(repository);

        let invoice = service.get_by_id(42).expect("invoice should exist");

        assert_eq!(42, invoice.id);
    }

    #[test]
    fn get_by_id_missing_invoice_returns_not_found() {
        let service = service_with(FakeRepository::default());

        assert_eq!(Err(InvoiceError::NotFound(999)), service.get_by_id(999));
    }

    #[test]
    fn get_by_id_repository_error_is_preserved() {
        let repository = FakeRepository { find_error: Some("boom".to_owned()), ..FakeRepository::default() };
        let service = service_with(repository);

        assert_eq!(Err(InvoiceError::Repository("boom".to_owned())), service.get_by_id(1));
    }

    #[test]
    fn mark_as_paid_pending_invoice_updates_status_date_and_repository() {
        let mut repository = FakeRepository::default();
        repository.invoices.insert(1, make_invoice(1));
        let mut service = service_with(repository);

        service.mark_as_paid(1).expect("mark_as_paid should succeed");

        let updated = service.repository.updated.as_ref().expect("repository should be updated");
        assert_eq!(InvoiceStatus::Paid, updated.status);
        assert_eq!(Some(UNIX_EPOCH + Duration::from_secs(123)), updated.paid_at);
    }

    #[test]
    fn mark_as_paid_already_paid_returns_error_without_update() {
        let mut invoice = make_invoice(1);
        invoice.status = InvoiceStatus::Paid;
        let mut repository = FakeRepository::default();
        repository.invoices.insert(1, invoice);
        let mut service = service_with(repository);

        assert_eq!(Err(InvoiceError::AlreadyPaid), service.mark_as_paid(1));
        assert!(service.repository.updated.is_none());
    }
}
```

## Sample Fix Cycle

When the implementer hits a compiler or test-runner issue, the fixer agent diagnoses and resolves it.

**Build output:**

```text
error[E0596]: cannot borrow `self.repository` as mutable, as it is behind a `&` reference
```

**Fixer diagnosis:** `mark_as_paid` calls `repository.update(...)`, which requires mutable repository access. The production method must take `&mut self`, and tests must bind the service as `let mut service`.

**Fix applied:** Change the method receiver to `&mut self` and update tests to use mutable bindings for `mark_as_paid` cases.

**Rebuild + rerun:** `cargo test --no-run && cargo test invoice_service::tests` → SUCCESS

---

**Another common cycle — integration test imports:**

**Build output:**

```text
error[E0432]: unresolved import `crate::invoice_service`
```

**Fixer diagnosis:** The test was created under `tests/invoice_service.rs`, which is an integration test crate. Integration tests import the library by crate name, not `crate::`.

**Fix applied:** Move the tests into `#[cfg(test)] mod tests` in `src/invoice_service.rs` and use `use super::*;`.

**Rerun:** SUCCESS

## Sample Final Report

```markdown
## Test Generation Report

**Project**: contoso-billing (Rust)
**Strategy**: Direct (single module in scope)

### Results
| Metric         | Value |
|----------------|-------|
| Tests created  | 7     |
| Tests passing  | 7     |
| Tests failing  | 0     |
| Files created  | 0 (tests appended to source module) |

### Files Modified
- `src/invoice_service.rs` (7 unit tests in `#[cfg(test)] mod tests`)

### Coverage
- InvoiceService::calculate_total — 3 happy path, 1 error case
- InvoiceService::get_by_id — found, missing, repository error
- InvoiceService::mark_as_paid — success and already-paid branches

### Build / Test Validation
- Check: ✅ `cargo check --all-targets`
- Compile tests: ✅ `cargo test --no-run`
- Test run: ✅ `cargo test invoice_service::tests`
```
