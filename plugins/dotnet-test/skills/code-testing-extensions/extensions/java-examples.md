# Java Pipeline Examples

Concrete input→output examples for the test generation pipeline targeting a Java codebase using JUnit 5 + Mockito. These show what each pipeline phase produces for a small project.

## Source Under Test

A simple `InvoiceService` in a Maven project using JUnit 5:

```text
pom.xml
src/main/java/com/contoso/billing/
  InvoiceService.java
  Invoice.java                       (mutable POJO with status, taxRate, lineItems and setStatus / setPaidDate mutators)
  InvoiceStatus.java                 (enum: PENDING, PAID)
  InvoiceRepository.java             (interface)
src/test/java/com/contoso/billing/   (exists, empty)
```

```java
// src/main/java/com/contoso/billing/InvoiceService.java
package com.contoso.billing;

import java.math.BigDecimal;
import java.math.RoundingMode;
import java.time.Clock;
import java.time.LocalDateTime;
import java.util.Optional;

public class InvoiceService {

    private final InvoiceRepository repository;
    private final Clock clock;

    public InvoiceService(InvoiceRepository repository) {
        this(repository, Clock.systemUTC());
    }

    public InvoiceService(InvoiceRepository repository, Clock clock) {
        this.repository = repository;
        this.clock = clock;
    }

    public BigDecimal calculateTotal(Invoice invoice) {
        if (invoice == null) {
            throw new IllegalArgumentException("invoice must not be null");
        }
        if (invoice.lineItems().isEmpty()) {
            throw new IllegalStateException("Invoice has no line items.");
        }
        BigDecimal subtotal = invoice.lineItems().stream()
            .map(li -> li.unitPrice().multiply(BigDecimal.valueOf(li.quantity())))
            .reduce(BigDecimal.ZERO, BigDecimal::add);
        BigDecimal tax = subtotal.multiply(invoice.taxRate());
        return subtotal.add(tax).setScale(2, RoundingMode.HALF_UP);
    }

    public Invoice getById(int id) {
        Optional<Invoice> invoice = repository.find(id);
        return invoice.orElseThrow(
            () -> new IllegalArgumentException("Invoice " + id + " not found."));
    }

    public void markAsPaid(int id) {
        Invoice invoice = repository.find(id)
            .orElseThrow(() -> new IllegalArgumentException("Invoice " + id + " not found."));
        if (invoice.status() == InvoiceStatus.PAID) {
            throw new IllegalStateException("Invoice is already paid.");
        }
        invoice.setStatus(InvoiceStatus.PAID);
        invoice.setPaidDate(LocalDateTime.now(clock));
        repository.update(invoice);
    }
}
```

## Sample Research Output

What `code-testing-researcher` produces in `.testagent/research.md`:

```markdown
# Test Generation Research

## Project Overview
- **Path**: /work/billing
- **Language**: Java 21 (`<maven.compiler.release>21</maven.compiler.release>`)
- **Build Tool**: Maven (wrapper `./mvnw` present)
- **Test Framework**: JUnit 5 (Jupiter 5.10) + Mockito 5.x (detected in pom.xml)
- **Assertion library**: built-in `Assertions` (no AssertJ/Hamcrest in deps)

## Coverage Baseline
- **Initial Line Coverage**: unknown
- **Strategy**: broad
- **Existing Test Count**: 0 tests across 0 files

## Build & Test Commands
- **Compile**: `./mvnw -q test-compile`
- **Test**: `./mvnw -q test`
- **Single class**: `./mvnw -q test -Dtest=InvoiceServiceTest`
- **Single method**: `./mvnw -q test -Dtest=InvoiceServiceTest#calculateTotal_validLineItems_returnsExpectedTotal`

## Project Structure
- Source: `src/main/java/com/contoso/billing/`
- Tests: `src/test/java/com/contoso/billing/` (exists, empty)

## Files to Test

### High Priority
| File | Classes/Methods | Testability | Notes |
|------|-----------------|-------------|-------|
| InvoiceService.java | calculateTotal, getById, markAsPaid | High | Repository dependency mockable via Mockito; clock injection available for time-dependent test |

## Testing Patterns
- No existing patterns; recommend JUnit 5 + Mockito with `@ExtendWith(MockitoExtension.class)`, `@Mock` / `@InjectMocks` fields, `@ParameterizedTest` + `@CsvSource` for table-driven `calculateTotal`, and `Clock.fixed(...)` for `markAsPaid` timestamp.

## Recommendations
- Test class lives in the same package (`com.contoso.billing`) for package-private access if needed
- Inject `Clock.fixed(...)` rather than mocking `LocalDateTime.now(...)` — the service already accepts a Clock
```

## Sample Plan Output

```markdown
# Test Implementation Plan

## Overview
Generate JUnit 5 + Mockito tests for InvoiceService, covering all three public
methods across happy path, edge case, and error scenarios. Single phase since
there is only one source file.

## Commands
- **Compile**: `./mvnw -q test-compile`
- **Test**: `./mvnw -q test -Dtest=InvoiceServiceTest`

## Phase 1: InvoiceService

### Files to Test

#### 1. InvoiceService.java
- **Source**: `src/main/java/com/contoso/billing/InvoiceService.java`
- **Test File**: `src/test/java/com/contoso/billing/InvoiceServiceTest.java`

**Methods to Test**:
1. `calculateTotal` — pure logic (parameterized)
   - Happy paths: single item w/ tax, multi-quantity zero tax, rounding-half-up
   - Error cases: null invoice → IllegalArgumentException; empty line items → IllegalStateException
2. `getById` — happy + missing
3. `markAsPaid` — happy (verify status + paid date via fixed clock + verify update) + already-paid + missing
```

## Sample Generated Test File

```java
// src/test/java/com/contoso/billing/InvoiceServiceTest.java
package com.contoso.billing;

import java.math.BigDecimal;
import java.time.Clock;
import java.time.Instant;
import java.time.LocalDateTime;
import java.time.ZoneOffset;
import java.util.List;
import java.util.Optional;

import org.junit.jupiter.api.DisplayName;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.extension.ExtendWith;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.CsvSource;

import org.mockito.InjectMocks;
import org.mockito.Mock;
import org.mockito.junit.jupiter.MockitoExtension;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertSame;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.mockito.ArgumentMatchers.any;
import static org.mockito.Mockito.never;
import static org.mockito.Mockito.verify;
import static org.mockito.Mockito.when;

@ExtendWith(MockitoExtension.class)
class InvoiceServiceTest {

    @Mock
    InvoiceRepository repository;

    @InjectMocks
    InvoiceService sut;

    // --- calculateTotal ---

    @ParameterizedTest(name = "qty={0} unitPrice={1} taxRate={2} -> {3}")
    @CsvSource({
        "1, 100.00, 0.10, 110.00",
        "3, 25.00,  0.00,  75.00",
        "2,  9.99,  0.07,  21.38"
    })
    void calculateTotal_validLineItems_returnsExpectedTotal(
        int quantity, BigDecimal unitPrice, BigDecimal taxRate, BigDecimal expected
    ) {
        Invoice invoice = new Invoice(1, InvoiceStatus.PENDING, taxRate,
            List.of(new LineItem(quantity, unitPrice)));

        BigDecimal total = sut.calculateTotal(invoice);

        assertEquals(0, total.compareTo(expected),
            () -> "expected " + expected + " but got " + total);
    }

    @Test
    @DisplayName("null invoice throws IllegalArgumentException")
    void calculateTotal_nullInvoice_throws() {
        assertThrows(IllegalArgumentException.class, () -> sut.calculateTotal(null));
    }

    @Test
    void calculateTotal_emptyLineItems_throws() {
        Invoice invoice = new Invoice(1, InvoiceStatus.PENDING, BigDecimal.ZERO, List.of());

        IllegalStateException ex = assertThrows(IllegalStateException.class,
            () -> sut.calculateTotal(invoice));
        assertEquals("Invoice has no line items.", ex.getMessage());
    }

    // --- getById ---

    @Test
    void getById_existingId_returnsInvoice() {
        Invoice expected = new Invoice(42, InvoiceStatus.PENDING, BigDecimal.ZERO, List.of());
        when(repository.find(42)).thenReturn(Optional.of(expected));

        assertSame(expected, sut.getById(42));
    }

    @Test
    void getById_missingId_throws() {
        when(repository.find(999)).thenReturn(Optional.empty());

        IllegalArgumentException ex = assertThrows(IllegalArgumentException.class,
            () -> sut.getById(999));
        assertEquals("Invoice 999 not found.", ex.getMessage());
    }

    // --- markAsPaid (uses an injected fixed Clock instead of @InjectMocks) ---

    @Test
    void markAsPaid_pendingInvoice_transitionsToPaidAndPersists() {
        Clock fixed = Clock.fixed(Instant.parse("2025-01-01T12:00:00Z"), ZoneOffset.UTC);
        InvoiceService service = new InvoiceService(repository, fixed);
        Invoice invoice = new Invoice(1, InvoiceStatus.PENDING, BigDecimal.ZERO, List.of());
        when(repository.find(1)).thenReturn(Optional.of(invoice));

        service.markAsPaid(1);

        assertEquals(InvoiceStatus.PAID, invoice.status());
        assertEquals(LocalDateTime.ofInstant(fixed.instant(), ZoneOffset.UTC), invoice.paidDate());
        verify(repository).update(invoice);
    }

    @Test
    void markAsPaid_alreadyPaid_throwsAndDoesNotUpdate() {
        Invoice invoice = new Invoice(1, InvoiceStatus.PAID, BigDecimal.ZERO, List.of());
        when(repository.find(1)).thenReturn(Optional.of(invoice));

        assertThrows(IllegalStateException.class, () -> sut.markAsPaid(1));
        verify(repository, never()).update(any());
    }

    @Test
    void markAsPaid_missingId_throws() {
        when(repository.find(999)).thenReturn(Optional.empty());

        assertThrows(IllegalArgumentException.class, () -> sut.markAsPaid(999));
    }
}
```

## Sample Fix Cycle

When the implementer hits a compile or runtime error, the fixer agent diagnoses and resolves it.

**Test output:**

```text
[ERROR] No tests found for given includes: [com.contoso.billing.InvoiceServiceTest]
```

**Fixer diagnosis:** Surefire only includes `**/*Test.class` (default). The class is `InvoiceServiceTest` (correct) but it was created under `src/test/java/com/contoso/billing/` with **no** package declaration. Maven compiles it into the default package, so `-Dtest=com.contoso.billing.InvoiceServiceTest` doesn't match.

**Fix applied:** Add `package com.contoso.billing;` at the top of the test file so it lands in the expected package.

**Rebuild + rerun:** `./mvnw -q test -Dtest=InvoiceServiceTest` → SUCCESS

---

**Another common cycle — wrong Mockito setup:**

**Test output:**

```text
org.mockito.exceptions.misusing.UnnecessaryStubbingException:
Unnecessary stubbings detected.
  1. -> at InvoiceServiceTest.calculateTotal_nullInvoice_throws(InvoiceServiceTest.java:55)
```

**Fixer diagnosis:** `@MockitoExtension` runs in strict mode by default — stubbed calls (`when(repository.find(...)).thenReturn(...)`) must be used. The test stubbed `repository` in a `@BeforeEach` for every test, but `calculateTotal_nullInvoice_throws` never touches the repository.

**Fix applied:** Move stubs into the tests that actually need them (as shown in the generated file above), rather than a single shared `@BeforeEach`.

**Rebuild + rerun:** SUCCESS

## Sample Final Report

```markdown
## Test Generation Report

**Project**: billing (Java / Maven)
**Strategy**: Direct (single source file in scope)

### Results
| Metric         | Value |
|----------------|-------|
| Tests created  | 8     |
| Tests passing  | 8     |
| Tests failing  | 0     |
| Files created  | 1     |

### Files Created
- `src/test/java/com/contoso/billing/InvoiceServiceTest.java` (8 tests, 3 parameterized cases via @CsvSource)

### Coverage
- InvoiceService.calculateTotal — 3 happy path, 2 error cases
- InvoiceService.getById — happy + missing
- InvoiceService.markAsPaid — happy (fixed Clock) + already-paid + missing

### Build / Test Validation
- `./mvnw -q test-compile`: ✅
- `./mvnw -q test`: ✅ Tests run: 8, Failures: 0, Errors: 0

### Next Steps
- Add AssertJ if the team standardises on it (more expressive assertions)
- Consider Testcontainers for true repository integration tests
```
