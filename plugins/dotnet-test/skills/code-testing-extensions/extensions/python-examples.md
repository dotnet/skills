# Python Pipeline Examples

Concrete input→output examples for the test generation pipeline targeting a Python codebase using pytest. These show what each pipeline phase produces for a small project.

## Source Under Test

A simple `InvoiceService` in a Python package using pytest:

```text
src/
  contoso_billing/
    __init__.py
    invoice_service.py
    invoice.py
    invoice_repository.py
tests/
  __init__.py
  conftest.py             (empty, just marks tests/ as a package root)
pyproject.toml
```

```python
# src/contoso_billing/invoice_service.py
from decimal import Decimal, ROUND_HALF_UP
from .invoice import Invoice, InvoiceStatus
from .invoice_repository import InvoiceRepository


class InvoiceService:
    def __init__(self, repository: InvoiceRepository) -> None:
        self._repository = repository

    def calculate_total(self, invoice: Invoice) -> Decimal:
        if invoice is None:
            raise ValueError("invoice must not be None")
        if not invoice.line_items:
            raise ValueError("Invoice has no line items.")

        subtotal = sum(
            (li.quantity * li.unit_price for li in invoice.line_items),
            start=Decimal("0"),
        )
        tax = subtotal * invoice.tax_rate
        return (subtotal + tax).quantize(Decimal("0.01"), rounding=ROUND_HALF_UP)

    def get_by_id(self, invoice_id: int) -> Invoice:
        invoice = self._repository.find(invoice_id)
        if invoice is None:
            raise KeyError(f"Invoice {invoice_id} not found.")
        return invoice

    def mark_as_paid(self, invoice_id: int) -> None:
        invoice = self._repository.find(invoice_id)
        if invoice is None:
            raise KeyError(f"Invoice {invoice_id} not found.")
        if invoice.status == InvoiceStatus.PAID:
            raise ValueError("Invoice is already paid.")
        invoice.status = InvoiceStatus.PAID
        invoice.paid_date = _utcnow()
        self._repository.update(invoice)


def _utcnow():
    from datetime import datetime, timezone
    return datetime.now(timezone.utc)
```

## Sample Research Output

What `code-testing-researcher` produces in `.testagent/research.md`:

```markdown
# Test Generation Research

## Project Overview
- **Path**: /work/contoso-billing
- **Language**: Python 3.11
- **Framework**: pure library (no Flask/Django)
- **Test Framework**: pytest 8.x (declared in pyproject.toml [project.optional-dependencies].test)
- **Package Layout**: `src/` layout — production package imports as `contoso_billing`

## Coverage Baseline
- **Initial Line Coverage**: unknown
- **Strategy**: broad
- **Existing Test Count**: 0 tests across 0 files

## Build & Test Commands
- **Install (editable)**: `python -m pip install -e ".[test]"`
- **Build/Type-check**: none configured
- **Test**: `python -m pytest`
- **Lint**: none configured

## Project Structure
- Source: `src/contoso_billing/`
- Tests: `tests/` (exists, empty besides `conftest.py`)

## Files to Test

### High Priority
| File | Classes/Functions | Testability | Notes |
|------|-------------------|-------------|-------|
| src/contoso_billing/invoice_service.py | InvoiceService: calculate_total, get_by_id, mark_as_paid | High | Core business logic, repository dependency needs mocking |

### Low Priority / Skip
| File | Reason |
|------|--------|
| src/contoso_billing/invoice.py | Dataclass, no logic |
| src/contoso_billing/invoice_repository.py | Interface/protocol, no implementation |

## Existing Tests
- No existing tests found

## Testing Patterns
- No existing patterns; recommend pytest function-style tests in `tests/test_invoice_service.py`, `unittest.mock.Mock(spec=InvoiceRepository)` for repository fakes, and `@pytest.mark.parametrize` for table-driven cases.

## Recommendations
- Start with `calculate_total` (pure logic, easy to parametrize)
- Then `get_by_id` and `mark_as_paid` (require mocking the repository)
- Use `unittest.mock.patch("contoso_billing.invoice_service._utcnow")` to control the timestamp in `mark_as_paid`
```

## Sample Plan Output

What `code-testing-planner` produces in `.testagent/plan.md`:

```markdown
# Test Implementation Plan

## Overview
Generate pytest tests for the Contoso Billing InvoiceService, covering all three
public methods across happy path, edge case, and error scenarios. Single phase
since there is only one source file.

## Commands
- **Install**: `python -m pip install -e ".[test]"`
- **Test**: `python -m pytest tests/test_invoice_service.py -q`
- **Test (file-scoped during dev)**: `python -m pytest tests/test_invoice_service.py::test_calculate_total_valid_line_items_returns_expected_total -q`

## Phase Summary
| Phase | Focus | Files | Est. Tests |
|-------|-------|-------|------------|
| 1 | InvoiceService | 1 | 9-12 |

---

## Phase 1: InvoiceService

### Overview
Cover all public methods of InvoiceService. `calculate_total` is pure logic tested
with `@pytest.mark.parametrize`. The async-looking methods are synchronous but
require a mocked InvoiceRepository.

### Files to Test

#### 1. invoice_service.py
- **Source**: `src/contoso_billing/invoice_service.py`
- **Test File**: `tests/test_invoice_service.py`

**Methods to Test**:
1. `calculate_total` — Pure calculation logic
   - Happy path: single line item returns quantity × price + tax
   - Happy path: multiple line items summed correctly
   - Edge case: zero tax rate returns subtotal only
   - Error case: None invoice raises ValueError
   - Error case: empty line items raises ValueError

2. `get_by_id` — Repository lookup
   - Happy path: existing ID returns invoice
   - Error case: missing ID raises KeyError

3. `mark_as_paid` — State transition
   - Happy path: pending invoice transitions to PAID with `paid_date` set
   - Error case: already-paid raises ValueError
   - Error case: missing ID raises KeyError

### Success Criteria
- [ ] Test file created at `tests/test_invoice_service.py`
- [ ] `python -m pytest` reports all tests passed
- [ ] No real network/IO; repository is mocked with `Mock(spec=InvoiceRepository)`
```

## Sample Generated Test File

What `code-testing-implementer` produces:

```python
# tests/test_invoice_service.py
from datetime import datetime, timezone
from decimal import Decimal
from unittest.mock import Mock, patch

import pytest

from contoso_billing.invoice import Invoice, InvoiceStatus, LineItem
from contoso_billing.invoice_repository import InvoiceRepository
from contoso_billing.invoice_service import InvoiceService


@pytest.fixture
def repository() -> Mock:
    return Mock(spec=InvoiceRepository)


@pytest.fixture
def sut(repository: Mock) -> InvoiceService:
    return InvoiceService(repository)


# --- calculate_total ---

@pytest.mark.parametrize(
    "quantity, unit_price, tax_rate, expected",
    [
        (1, "100.00", "0.10", "110.00"),
        (3, "25.00", "0.00", "75.00"),
        (2, "9.99", "0.07", "21.38"),
    ],
    ids=["single-item-10pct-tax", "multi-quantity-zero-tax", "rounds-half-up"],
)
def test_calculate_total_valid_line_items_returns_expected_total(
    sut: InvoiceService, quantity: int, unit_price: str, tax_rate: str, expected: str
) -> None:
    invoice = Invoice(
        tax_rate=Decimal(tax_rate),
        line_items=[LineItem(quantity=quantity, unit_price=Decimal(unit_price))],
    )

    total = sut.calculate_total(invoice)

    assert total == Decimal(expected)


def test_calculate_total_none_invoice_raises_value_error(sut: InvoiceService) -> None:
    with pytest.raises(ValueError, match="invoice must not be None"):
        sut.calculate_total(None)


def test_calculate_total_empty_line_items_raises_value_error(sut: InvoiceService) -> None:
    invoice = Invoice(tax_rate=Decimal("0"), line_items=[])

    with pytest.raises(ValueError, match="no line items"):
        sut.calculate_total(invoice)


# --- get_by_id ---

def test_get_by_id_existing_id_returns_invoice(
    sut: InvoiceService, repository: Mock
) -> None:
    expected = Invoice(id=42, tax_rate=Decimal("0"), line_items=[])
    repository.find.return_value = expected

    result = sut.get_by_id(42)

    assert result is expected
    repository.find.assert_called_once_with(42)


def test_get_by_id_missing_id_raises_key_error(
    sut: InvoiceService, repository: Mock
) -> None:
    repository.find.return_value = None

    with pytest.raises(KeyError, match="999"):
        sut.get_by_id(999)


# --- mark_as_paid ---

def test_mark_as_paid_pending_invoice_sets_status_and_date(
    sut: InvoiceService, repository: Mock
) -> None:
    invoice = Invoice(id=1, status=InvoiceStatus.PENDING, tax_rate=Decimal("0"), line_items=[])
    repository.find.return_value = invoice
    fixed_now = datetime(2025, 1, 1, 12, 0, tzinfo=timezone.utc)

    with patch("contoso_billing.invoice_service._utcnow", return_value=fixed_now):
        sut.mark_as_paid(1)

    assert invoice.status == InvoiceStatus.PAID
    assert invoice.paid_date == fixed_now
    repository.update.assert_called_once_with(invoice)


def test_mark_as_paid_already_paid_raises_value_error(
    sut: InvoiceService, repository: Mock
) -> None:
    invoice = Invoice(id=1, status=InvoiceStatus.PAID, tax_rate=Decimal("0"), line_items=[])
    repository.find.return_value = invoice

    with pytest.raises(ValueError, match="already paid"):
        sut.mark_as_paid(1)

    repository.update.assert_not_called()


def test_mark_as_paid_missing_id_raises_key_error(
    sut: InvoiceService, repository: Mock
) -> None:
    repository.find.return_value = None

    with pytest.raises(KeyError, match="999"):
        sut.mark_as_paid(999)
```

## Sample Fix Cycle

When the implementer encounters an import or attribute error, the fixer agent diagnoses and resolves it.

**Test output:**

```text
ModuleNotFoundError: No module named 'contoso_billing'
```

**Fixer diagnosis:** The package is not installed in editable mode, so the `src/` layout's package is not on `sys.path`.

**Fix applied:**

```bash
python -m pip install -e ".[test]"
```

**Rerun:** `python -m pytest tests/test_invoice_service.py -q` → SUCCESS

---

**Another common cycle — patch target wrong:**

**Test output:**

```text
AttributeError: <module 'datetime'> does not have the attribute '_utcnow'
```

**Fixer diagnosis:** The test patched `datetime._utcnow` but the production code defines its own `_utcnow` helper inside `contoso_billing.invoice_service`. Patches must target the lookup site, not the definition site.

**Fix applied:**

```python
# Before (wrong)
with patch("datetime._utcnow", return_value=fixed_now):

# After (fixed) — patch where the name is looked up
with patch("contoso_billing.invoice_service._utcnow", return_value=fixed_now):
```

**Rerun:** SUCCESS

---

**Another common cycle — Mock without spec:**

**Test output:**

```text
AttributeError: Mock object has no attribute 'find_by_id'
```

(but the actual repository method is `find`, not `find_by_id`)

**Fixer diagnosis:** `Mock()` happily creates any attribute on access, so a typo in the test went undetected until the production code called `repository.find(...)`. Using `Mock(spec=InvoiceRepository)` would have failed at setup time.

**Fix applied:**

```python
# Before
repository = Mock()
repository.find_by_id.return_value = expected   # typo, silently accepted

# After
repository = Mock(spec=InvoiceRepository)
repository.find.return_value = expected         # typos now raise AttributeError
```

**Rerun:** SUCCESS

## Sample Final Report

What `code-testing-generator` produces at Step 9:

```markdown
## Test Generation Report

**Project**: contoso-billing
**Strategy**: Direct (single source file in scope)

### Results
| Metric         | Value |
|----------------|-------|
| Tests created  | 9     |
| Tests passing  | 9     |
| Tests failing  | 0     |
| Files created  | 1     |

### Files Created
- `tests/test_invoice_service.py` (9 tests, 3 parametrized)

### Coverage
- InvoiceService.calculate_total — 3 happy path, 2 error cases
- InvoiceService.get_by_id — 1 happy path, 1 error case
- InvoiceService.mark_as_paid — 1 happy path, 2 error cases

### Build / Install Validation
- Editable install: ✅ `python -m pip install -e ".[test]"`
- Test run: ✅ `python -m pytest` — 9 passed in 0.12s

### Next Steps
- Add tests for repository implementations if any exist
- Consider snapshot/property-based testing (`hypothesis`) for `calculate_total` rounding behaviour
```
