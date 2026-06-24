# Kotlin Pipeline Examples

Concrete input→output examples for the test generation pipeline targeting a Kotlin JVM codebase using Gradle + JUnit 5. These show what each pipeline phase produces for a small project.

> `kotlin.test` follows the same shape for multiplatform projects. Replace JUnit Jupiter parameterization with `@Test` methods or the repo's established KMP data pattern, and place tests under `src/commonTest/kotlin` or the matching target source set.

## Source Under Test

A simple `InvoiceService` in a Gradle Kotlin JVM project:

```text
settings.gradle.kts
build.gradle.kts
src/main/kotlin/com/contoso/billing/
  Invoice.kt
  InvoiceRepository.kt
  InvoiceService.kt
src/test/kotlin/com/contoso/billing/   (exists, empty)
```

```kotlin
// src/main/kotlin/com/contoso/billing/InvoiceService.kt
package com.contoso.billing

import java.math.BigDecimal
import java.math.RoundingMode
import java.time.Clock
import java.time.LocalDateTime

class InvoiceService(
    private val repository: InvoiceRepository,
    private val clock: Clock = Clock.systemUTC(),
) {
    fun calculateTotal(invoice: Invoice): BigDecimal {
        require(invoice.lineItems.isNotEmpty()) { "Invoice has no line items." }

        val subtotal = invoice.lineItems
            .map { it.unitPrice.multiply(BigDecimal.valueOf(it.quantity.toLong())) }
            .fold(BigDecimal.ZERO, BigDecimal::add)
        val tax = subtotal.multiply(invoice.taxRate)
        return subtotal.add(tax).setScale(2, RoundingMode.HALF_UP)
    }

    fun getById(id: Int): Invoice = repository.find(id)
        ?: throw NoSuchElementException("Invoice $id not found.")

    fun markAsPaid(id: Int) {
        val invoice = getById(id)
        check(invoice.status != InvoiceStatus.PAID) { "Invoice is already paid." }

        invoice.status = InvoiceStatus.PAID
        invoice.paidAt = LocalDateTime.now(clock)
        repository.update(invoice)
    }
}
```

## Sample Research Output

What `code-testing-researcher` produces in `.testagent/research.md`:

```markdown
# Test Generation Research

## Project Overview
- **Path**: /work/contoso-billing
- **Language**: Kotlin 2.0 JVM
- **Build Tool**: Gradle wrapper present (`./gradlew`)
- **Test Framework**: JUnit 5 + kotlin.test assertions (`useJUnitPlatform()` and `junit-jupiter-params` detected)
- **Mocking**: MockK is not present; repository is an interface and can be faked directly

## Coverage Baseline
- **Initial Line Coverage**: unknown
- **Strategy**: broad
- **Existing Test Count**: 0 tests across 0 files

## Build & Test Commands
- **Compile tests**: `./gradlew compileTestKotlin --console=plain`
- **Single class**: `./gradlew test --tests "com.contoso.billing.InvoiceServiceTest" --console=plain`
- **All tests**: `./gradlew test --console=plain`

## Files to Test

### High Priority
| File | Classes/Methods | Testability | Notes |
|------|-----------------|-------------|-------|
| src/main/kotlin/com/contoso/billing/InvoiceService.kt | calculateTotal, getById, markAsPaid | High | Repository interface is fakeable; Clock is injectable |

## Testing Patterns
- No existing patterns; recommend JUnit 5 `@Test`, `@ParameterizedTest` + `@CsvSource`, backticked test names, `kotlin.test` assertions, and a hand-written fake repository.
```

## Sample Plan Output

```markdown
# Test Implementation Plan

## Overview
Generate JUnit 5 tests for InvoiceService covering calculation, lookup, and
paid-state transition behavior.

## Commands
- **Compile tests**: `./gradlew compileTestKotlin --console=plain`
- **Test**: `./gradlew test --tests "com.contoso.billing.InvoiceServiceTest" --console=plain`

## Phase 1: InvoiceService

### Files to Test
- **Source**: `src/main/kotlin/com/contoso/billing/InvoiceService.kt`
- **Test File**: `src/test/kotlin/com/contoso/billing/InvoiceServiceTest.kt`

**Methods to Test**:
1. `calculateTotal` — parameterized tax, zero tax, rounding, empty-line-items error
2. `getById` — existing invoice and missing invoice
3. `markAsPaid` — success with fixed clock, already-paid, missing
```

## Sample Generated Test File

```kotlin
// src/test/kotlin/com/contoso/billing/InvoiceServiceTest.kt
package com.contoso.billing

import org.junit.jupiter.api.Test
import org.junit.jupiter.api.assertThrows
import org.junit.jupiter.params.ParameterizedTest
import org.junit.jupiter.params.provider.CsvSource
import java.math.BigDecimal
import java.time.Clock
import java.time.Instant
import java.time.LocalDateTime
import java.time.ZoneOffset
import kotlin.test.assertEquals
import kotlin.test.assertSame
import kotlin.test.assertTrue

class InvoiceServiceTest {

    private class FakeRepository : InvoiceRepository {
        val invoices = mutableMapOf<Int, Invoice>()
        var updated: Invoice? = null

        override fun find(id: Int): Invoice? = invoices[id]

        override fun update(invoice: Invoice) {
            updated = invoice
            invoices[invoice.id] = invoice
        }
    }

    @ParameterizedTest(name = "qty={0}, unitPrice={1}, taxRate={2} -> {3}")
    @CsvSource(
        "1, 100.00, 0.10, 110.00",
        "3, 25.00, 0.00, 75.00",
        "2, 9.99, 0.07, 21.38",
    )
    fun `calculateTotal returns expected total for valid line items`(
        quantity: Int,
        unitPrice: BigDecimal,
        taxRate: BigDecimal,
        expected: BigDecimal,
    ) {
        val service = InvoiceService(FakeRepository())
        val invoice = invoice(
            taxRate = taxRate,
            lineItems = mutableListOf(LineItem(quantity = quantity, unitPrice = unitPrice)),
        )

        val total = service.calculateTotal(invoice)

        assertEquals(0, total.compareTo(expected), "expected $expected but got $total")
    }

    @Test
    fun `calculateTotal throws for empty line items`() {
        val service = InvoiceService(FakeRepository())
        val invoice = invoice(lineItems = mutableListOf())

        val exception = assertThrows<IllegalArgumentException> { service.calculateTotal(invoice) }

        assertTrue(exception.message!!.contains("no line items", ignoreCase = true))
    }

    @Test
    fun `getById returns existing invoice`() {
        val repository = FakeRepository()
        val expected = invoice(id = 42)
        repository.invoices[42] = expected
        val service = InvoiceService(repository)

        val result = service.getById(42)

        assertSame(expected, result)
    }

    @Test
    fun `getById throws for missing invoice`() {
        val service = InvoiceService(FakeRepository())

        val exception = assertThrows<NoSuchElementException> { service.getById(999) }

        assertTrue(exception.message!!.contains("999"))
    }

    @Test
    fun `markAsPaid updates status date and repository`() {
        val repository = FakeRepository()
        val invoice = invoice(id = 1)
        repository.invoices[1] = invoice
        val fixedClock = Clock.fixed(Instant.parse("2025-01-01T12:00:00Z"), ZoneOffset.UTC)
        val service = InvoiceService(repository, fixedClock)

        service.markAsPaid(1)

        assertEquals(InvoiceStatus.PAID, invoice.status)
        assertEquals(LocalDateTime.ofInstant(fixedClock.instant(), ZoneOffset.UTC), invoice.paidAt)
        assertSame(invoice, repository.updated)
    }

    @Test
    fun `markAsPaid throws and does not update already paid invoice`() {
        val repository = FakeRepository()
        repository.invoices[1] = invoice(id = 1, status = InvoiceStatus.PAID)
        val service = InvoiceService(repository)

        val exception = assertThrows<IllegalStateException> { service.markAsPaid(1) }

        assertTrue(exception.message!!.contains("already paid", ignoreCase = true))
        assertEquals(null, repository.updated)
    }

    private fun invoice(
        id: Int = 1,
        status: InvoiceStatus = InvoiceStatus.PENDING,
        taxRate: BigDecimal = BigDecimal("0.10"),
        lineItems: MutableList<LineItem> = mutableListOf(LineItem(quantity = 1, unitPrice = BigDecimal("100.00"))),
    ): Invoice = Invoice(id = id, status = status, taxRate = taxRate, lineItems = lineItems, paidAt = null)
}
```

## Sample Fix Cycle

When the implementer hits a Gradle or Kotlin compile issue, the fixer agent diagnoses and resolves it.

**Build output:**

```text
No tests found for given includes: [com.contoso.billing.InvoiceServiceTest]
```

**Fixer diagnosis:** The test file was created under `src/test/java` with Kotlin source, so the Kotlin JVM source set did not compile it into the expected package.

**Fix applied:** Move the file to `src/test/kotlin/com/contoso/billing/InvoiceServiceTest.kt` and keep `package com.contoso.billing` at the top.

**Rebuild + rerun:** `./gradlew test --tests "com.contoso.billing.InvoiceServiceTest" --console=plain` → SUCCESS

---

**Another common cycle — JUnit Platform not enabled:**

**Test output:**

```text
0 tests completed
```

**Fixer diagnosis:** The project has JUnit Jupiter dependencies but the Gradle `test` task is not configured with `useJUnitPlatform()`.

**Fix applied:** Match the repo's build convention and add `tasks.test { useJUnitPlatform() }` if it is missing.

**Rerun:** SUCCESS

## Sample Final Report

```markdown
## Test Generation Report

**Project**: contoso-billing (Kotlin / Gradle)
**Strategy**: Direct (single source file in scope)

### Results
| Metric         | Value |
|----------------|-------|
| Tests created  | 8     |
| Tests passing  | 8     |
| Tests failing  | 0     |
| Files created  | 1     |

### Files Created
- `src/test/kotlin/com/contoso/billing/InvoiceServiceTest.kt` (8 JUnit 5 tests, 3 parameterized cases)

### Coverage
- InvoiceService.calculateTotal — 3 happy path, 1 error case
- InvoiceService.getById — found and missing branches
- InvoiceService.markAsPaid — success and already-paid branches

### Build / Test Validation
- Compile tests: ✅ `./gradlew compileTestKotlin --console=plain`
- Test run: ✅ `./gradlew test --tests "com.contoso.billing.InvoiceServiceTest" --console=plain`
```
