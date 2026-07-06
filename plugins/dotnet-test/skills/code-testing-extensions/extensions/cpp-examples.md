# C++ Pipeline Examples

Concrete input→output examples for the test generation pipeline targeting a C++ codebase using CMake + Catch2. These show what each pipeline phase produces for a small library project.

> GoogleTest follows the same shape. Replace `TEST_CASE` / `SECTION` / `REQUIRE` with `TEST` / `EXPECT_*` and link the test executable to `GTest::gtest_main` instead of `Catch2::Catch2WithMain`.

## Source Under Test

A simple `InvoiceService` in a CMake project:

```text
CMakeLists.txt
include/contoso/billing/
  invoice.hpp
  invoice_repository.hpp
  invoice_service.hpp
src/invoice_service.cpp
tests/CMakeLists.txt        (links Catch2::Catch2WithMain)
```

```cpp
// src/invoice_service.cpp
#include "contoso/billing/invoice_service.hpp"

#include <cmath>
#include <stdexcept>
#include <utility>

namespace contoso::billing {

InvoiceService::InvoiceService(InvoiceRepository& repository, Clock clock)
    : repository_(repository), clock_(std::move(clock)) {}

double InvoiceService::calculate_total(const Invoice& invoice) const {
    if (invoice.line_items.empty()) {
        throw std::invalid_argument("invoice has no line items");
    }

    double subtotal = 0.0;
    for (const LineItem& item : invoice.line_items) {
        subtotal += static_cast<double>(item.quantity) * item.unit_price;
    }

    return std::round((subtotal + subtotal * invoice.tax_rate) * 100.0) / 100.0;
}

Invoice InvoiceService::get_by_id(int id) const {
    auto invoice = repository_.find(id);
    if (!invoice.has_value()) {
        throw std::out_of_range("invoice not found");
    }

    return *invoice;
}

void InvoiceService::mark_as_paid(int id) {
    auto invoice = repository_.find(id);
    if (!invoice.has_value()) {
        throw std::out_of_range("invoice not found");
    }
    if (invoice->status == InvoiceStatus::paid) {
        throw std::logic_error("invoice is already paid");
    }

    invoice->status = InvoiceStatus::paid;
    invoice->paid_at = clock_();
    repository_.update(*invoice);
}

} // namespace contoso::billing
```

## Sample Research Output

What `code-testing-researcher` produces in `.testagent/research.md`:

```markdown
# Test Generation Research

## Project Overview
- **Path**: /work/contoso-billing
- **Language**: C++20
- **Build System**: CMake (preset `ninja-debug` present)
- **Test Framework**: Catch2 v3 (detected via `find_package(Catch2 3 REQUIRED)`)

## Coverage Baseline
- **Initial Line Coverage**: unknown
- **Strategy**: broad
- **Existing Test Count**: 0 tests across 0 files

## Build & Test Commands
- **Configure**: `cmake --preset ninja-debug`
- **Build tests**: `cmake --build --preset ninja-debug --target invoice_service_tests`
- **Test**: `ctest --preset ninja-debug --output-on-failure`
- **Coverage (if configured)**: rebuild with `--coverage`, then use `gcov` or `llvm-cov`

## Files to Test

### High Priority
| File | Classes/Functions | Testability | Notes |
|------|-------------------|-------------|-------|
| src/invoice_service.cpp | InvoiceService: calculate_total, get_by_id, mark_as_paid | High | Repository is an interface; clock dependency is injectable |

## Testing Patterns
- No existing patterns; recommend Catch2 `TEST_CASE` blocks, `SECTION` cases, `Approx` for floating-point assertions, and a hand-written fake repository.
```

## Sample Plan Output

```markdown
# Test Implementation Plan

## Overview
Generate Catch2 tests for InvoiceService covering pure calculation logic,
repository lookup behavior, and the paid-state transition.

## Commands
- **Build**: `cmake --build --preset ninja-debug --target invoice_service_tests`
- **Test**: `ctest --preset ninja-debug --output-on-failure`

## Phase 1: InvoiceService

### Files to Test
- **Source**: `src/invoice_service.cpp`
- **Test File**: `tests/invoice_service_tests.cpp`

**Methods to Test**:
1. `calculate_total` — tax, zero tax, rounding, empty line items
2. `get_by_id` — existing invoice and missing invoice
3. `mark_as_paid` — success with fixed clock, already-paid, missing
```

## Sample Generated Test File

```cpp
// tests/invoice_service_tests.cpp
#include "contoso/billing/invoice_service.hpp"

#include <catch2/catch_test_macros.hpp>
#include <catch2/matchers/catch_matchers_string.hpp>

#include <chrono>
#include <optional>
#include <stdexcept>
#include <unordered_map>

using Catch::Matchers::ContainsSubstring;

namespace contoso::billing {
namespace {

class FakeRepository final : public InvoiceRepository {
public:
    std::optional<Invoice> find(int id) override {
        auto it = invoices.find(id);
        return it == invoices.end() ? std::nullopt : std::optional<Invoice>{it->second};
    }

    void update(const Invoice& invoice) override {
        updated = invoice;
        invoices[invoice.id] = invoice;
    }

    std::unordered_map<int, Invoice> invoices;
    std::optional<Invoice> updated;
};

Invoice make_invoice(int id = 1) {
    return Invoice{
        .id = id,
        .status = InvoiceStatus::pending,
        .tax_rate = 0.10,
        .line_items = {LineItem{.quantity = 1, .unit_price = 100.0}},
    };
}

} // namespace

TEST_CASE("InvoiceService calculates totals", "[invoice-service]") {
    FakeRepository repository;
    InvoiceService sut(repository, [] { return std::chrono::system_clock::time_point{}; });

    SECTION("single item with tax") {
        REQUIRE(sut.calculate_total(make_invoice()) == Catch::Approx(110.0));
    }

    SECTION("multi quantity with zero tax") {
        Invoice invoice = make_invoice();
        invoice.tax_rate = 0.0;
        invoice.line_items = {LineItem{.quantity = 3, .unit_price = 25.0}};

        REQUIRE(sut.calculate_total(invoice) == Catch::Approx(75.0));
    }

    SECTION("rounds to two decimals") {
        Invoice invoice = make_invoice();
        invoice.tax_rate = 0.07;
        invoice.line_items = {LineItem{.quantity = 2, .unit_price = 9.99}};

        REQUIRE(sut.calculate_total(invoice) == Catch::Approx(21.38).epsilon(0.001));
    }

    SECTION("empty line items throw") {
        Invoice invoice = make_invoice();
        invoice.line_items.clear();

        REQUIRE_THROWS_WITH(sut.calculate_total(invoice), ContainsSubstring("no line items"));
    }
}

TEST_CASE("InvoiceService uses the repository", "[invoice-service]") {
    const auto fixed_time = std::chrono::system_clock::time_point{std::chrono::seconds{123}};
    FakeRepository repository;
    repository.invoices.emplace(42, make_invoice(42));
    InvoiceService sut(repository, [fixed_time] { return fixed_time; });

    SECTION("get_by_id returns an existing invoice") {
        REQUIRE(sut.get_by_id(42).id == 42);
    }

    SECTION("get_by_id throws when missing") {
        REQUIRE_THROWS_WITH(sut.get_by_id(999), ContainsSubstring("not found"));
    }

    SECTION("mark_as_paid updates status, date, and repository") {
        repository.invoices.emplace(1, make_invoice(1));

        sut.mark_as_paid(1);

        REQUIRE(repository.updated.has_value());
        REQUIRE(repository.updated->status == InvoiceStatus::paid);
        REQUIRE(repository.updated->paid_at == fixed_time);
    }

    SECTION("mark_as_paid rejects an already-paid invoice") {
        Invoice invoice = make_invoice(2);
        invoice.status = InvoiceStatus::paid;
        repository.invoices.emplace(2, invoice);

        REQUIRE_THROWS_WITH(sut.mark_as_paid(2), ContainsSubstring("already paid"));
    }
}

} // namespace contoso::billing
```

## Sample Fix Cycle

When the implementer hits a compile or runner issue, the fixer agent diagnoses and resolves it.

**Build output:**

```text
error: cannot declare variable 'repository' to be of abstract type 'FakeRepository'
note: missing pure virtual method 'InvoiceRepository::update'
```

**Fixer diagnosis:** The fake repository implemented `find` but not the full `InvoiceRepository` interface.

**Fix applied:** Add `void update(const Invoice& invoice) override` to `FakeRepository` and record the updated invoice for assertions.

**Rebuild + rerun:** `cmake --build --preset ninja-debug --target invoice_service_tests && ctest --preset ninja-debug --output-on-failure` → SUCCESS

## Sample Final Report

```markdown
## Test Generation Report

**Project**: contoso-billing (C++ / CMake)
**Strategy**: Direct (single source file in scope)

### Results
| Metric         | Value |
|----------------|-------|
| Tests created  | 9     |
| Tests passing  | 9     |
| Tests failing  | 0     |
| Files created  | 1     |

### Files Created
- `tests/invoice_service_tests.cpp` (2 Catch2 test cases, 9 sections)

### Coverage
- InvoiceService.calculate_total — tax, zero tax, rounding, empty input
- InvoiceService.get_by_id — found and missing branches
- InvoiceService.mark_as_paid — success and already-paid branches

### Build / Test Validation
- Configure: ✅ `cmake --preset ninja-debug`
- Build: ✅ `cmake --build --preset ninja-debug --target invoice_service_tests`
- Test: ✅ `ctest --preset ninja-debug --output-on-failure`
```
