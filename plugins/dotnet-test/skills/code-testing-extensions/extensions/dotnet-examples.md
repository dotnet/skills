# .NET Pipeline Examples

Concrete input→output examples for the test generation pipeline targeting a .NET/C# codebase. These show what each pipeline phase produces for a small project.

## Source Under Test

A simple `InvoiceService` in a .NET 9 project using MSTest:

```text
src/
  Contoso.Billing/
    Contoso.Billing.csproj
    InvoiceService.cs
    Invoice.cs
    IInvoiceRepository.cs
tests/
  Contoso.Billing.Tests/
    Contoso.Billing.Tests.csproj    (exists, references Contoso.Billing)
Contoso.Billing.sln
```

```csharp
// InvoiceService.cs
namespace Contoso.Billing;

public class InvoiceService(IInvoiceRepository repository)
{
    public decimal CalculateTotal(Invoice invoice)
    {
        if (invoice is null) throw new ArgumentNullException(nameof(invoice));
        if (invoice.LineItems.Count == 0) throw new InvalidOperationException("Invoice has no line items.");

        var subtotal = invoice.LineItems.Sum(li => li.Quantity * li.UnitPrice);
        var tax = subtotal * invoice.TaxRate;
        return Math.Round(subtotal + tax, 2);
    }

    public async Task<Invoice> GetByIdAsync(int id)
    {
        var invoice = await repository.FindAsync(id);
        return invoice ?? throw new KeyNotFoundException($"Invoice {id} not found.");
    }

    public async Task MarkAsPaidAsync(int id)
    {
        var invoice = await repository.FindAsync(id)
            ?? throw new KeyNotFoundException($"Invoice {id} not found.");
        if (invoice.Status == InvoiceStatus.Paid)
            throw new InvalidOperationException("Invoice is already paid.");
        invoice.Status = InvoiceStatus.Paid;
        invoice.PaidDate = DateTime.UtcNow;
        await repository.UpdateAsync(invoice);
    }
}
```

## Sample Research Output

What `code-testing-researcher` produces in `.testagent/research.md`:

```markdown
# Test Generation Research

## Project Overview
- **Path**: C:\src\Contoso.Billing
- **Language**: C# (.NET 9)
- **Framework**: .NET 9 (net9.0)
- **Test Framework**: MSTest 3.8

## Coverage Baseline
- **Initial Line Coverage**: unknown
- **Strategy**: broad
- **Existing Test Count**: 0 tests across 0 files

## Build & Test Commands
- **Build**: `dotnet build Contoso.Billing.sln`
- **Test**: `dotnet test Contoso.Billing.sln`
- **Lint**: `dotnet format Contoso.Billing.sln`

## Project Structure
- Source: `src/Contoso.Billing/`
- Tests: `tests/Contoso.Billing.Tests/` (exists, empty)

## Files to Test

### High Priority
| File | Classes/Functions | Testability | Notes |
|------|-------------------|-------------|-------|
| src/Contoso.Billing/InvoiceService.cs | InvoiceService: CalculateTotal, GetByIdAsync, MarkAsPaidAsync | High | Core business logic, repository dependency needs mocking |

### Low Priority / Skip
| File | Reason |
|------|--------|
| src/Contoso.Billing/Invoice.cs | Data model, no logic |
| src/Contoso.Billing/IInvoiceRepository.cs | Interface, no implementation |

## Existing Tests
- No existing tests found

## Existing Test Projects
- **Project file**: `tests/Contoso.Billing.Tests/Contoso.Billing.Tests.csproj`
- **Target source project**: `src/Contoso.Billing/Contoso.Billing.csproj`
- **Test files**: none

## Testing Patterns
- No existing patterns; recommend sealed test classes, AAA structure, `Moq` for mocking IInvoiceRepository

## Recommendations
- Start with InvoiceService.CalculateTotal (pure logic, easy to test)
- Then async methods (require mocking IInvoiceRepository)
```

## Sample Plan Output

What `code-testing-planner` produces in `.testagent/plan.md`:

```markdown
# Test Implementation Plan

## Overview
Generate MSTest tests for the Contoso.Billing InvoiceService, covering all three
public methods across happy path, edge case, and error scenarios. Single phase
since there is only one source file.

## Commands
- **Build**: `dotnet build tests/Contoso.Billing.Tests/Contoso.Billing.Tests.csproj`
- **Test**: `dotnet test tests/Contoso.Billing.Tests/Contoso.Billing.Tests.csproj`
- **Lint**: `dotnet format --include tests/Contoso.Billing.Tests/`

## Phase Summary
| Phase | Focus | Files | Est. Tests |
|-------|-------|-------|------------|
| 1 | InvoiceService | 1 | 9-12 |

---

## Phase 1: InvoiceService

### Overview
Cover all public methods of InvoiceService. CalculateTotal is pure logic tested
with DataRow. Async methods require a mocked IInvoiceRepository.

### Files to Test

#### 1. InvoiceService.cs
- **Source**: `src/Contoso.Billing/InvoiceService.cs`
- **Test File**: `tests/Contoso.Billing.Tests/InvoiceServiceTests.cs`
- **Test Class**: `InvoiceServiceTests`

**Methods to Test**:
1. `CalculateTotal` — Pure calculation logic
   - Happy path: single line item returns quantity × price + tax
   - Happy path: multiple line items summed correctly
   - Edge case: zero tax rate returns subtotal only
   - Error case: null invoice throws ArgumentNullException
   - Error case: empty line items throws InvalidOperationException

2. `GetByIdAsync` — Repository lookup
   - Happy path: existing ID returns invoice
   - Error case: non-existent ID throws KeyNotFoundException

3. `MarkAsPaidAsync` — State transition
   - Happy path: unpaid invoice transitions to Paid with PaidDate set
   - Error case: already paid throws InvalidOperationException
   - Error case: non-existent ID throws KeyNotFoundException

### Success Criteria
- [ ] All test files created
- [ ] Tests compile with `dotnet build`
- [ ] All tests pass with `dotnet test`
```

## Sample Generated Test File

What `code-testing-implementer` produces:

```csharp
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Contoso.Billing;

namespace Contoso.Billing.Tests;

[TestClass]
public sealed class InvoiceServiceTests
{
    private readonly Mock<IInvoiceRepository> _repositoryMock = new();
    private readonly InvoiceService _sut;

    public InvoiceServiceTests()
    {
        _sut = new InvoiceService(_repositoryMock.Object);
    }

    // --- CalculateTotal ---

    [TestMethod]
    [DataRow(1, 100.00, 0.10, 110.00, DisplayName = "Single item with 10% tax")]
    [DataRow(3, 25.00, 0.0, 75.00, DisplayName = "Multiple quantity, zero tax")]
    public void CalculateTotal_ValidLineItems_ReturnsExpectedTotal(
        int quantity, double unitPrice, double taxRate, double expected)
    {
        // Arrange
        var invoice = new Invoice
        {
            TaxRate = (decimal)taxRate,
            LineItems = [new LineItem { Quantity = quantity, UnitPrice = (decimal)unitPrice }]
        };

        // Act
        var total = _sut.CalculateTotal(invoice);

        // Assert
        Assert.AreEqual((decimal)expected, total);
    }

    [TestMethod]
    public void CalculateTotal_NullInvoice_ThrowsArgumentNullException()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => _sut.CalculateTotal(null!));
    }

    [TestMethod]
    public void CalculateTotal_EmptyLineItems_ThrowsInvalidOperationException()
    {
        // Arrange
        var invoice = new Invoice { LineItems = [] };

        // Act & Assert
        Assert.ThrowsExactly<InvalidOperationException>(() => _sut.CalculateTotal(invoice));
    }

    // --- GetByIdAsync ---

    [TestMethod]
    public async Task GetByIdAsync_ExistingId_ReturnsInvoice()
    {
        // Arrange
        var expected = new Invoice { Id = 42 };
        _repositoryMock.Setup(r => r.FindAsync(42)).ReturnsAsync(expected);

        // Act
        var result = await _sut.GetByIdAsync(42);

        // Assert
        Assert.AreSame(expected, result);
    }

    [TestMethod]
    public async Task GetByIdAsync_NonExistentId_ThrowsKeyNotFoundException()
    {
        // Arrange
        _repositoryMock.Setup(r => r.FindAsync(999)).ReturnsAsync((Invoice?)null);

        // Act & Assert
        await Assert.ThrowsExactlyAsync<KeyNotFoundException>(
            () => _sut.GetByIdAsync(999));
    }

    // --- MarkAsPaidAsync ---

    [TestMethod]
    public async Task MarkAsPaidAsync_UnpaidInvoice_SetsStatusAndDate()
    {
        // Arrange
        var invoice = new Invoice { Id = 1, Status = InvoiceStatus.Pending };
        _repositoryMock.Setup(r => r.FindAsync(1)).ReturnsAsync(invoice);

        // Act
        await _sut.MarkAsPaidAsync(1);

        // Assert
        Assert.AreEqual(InvoiceStatus.Paid, invoice.Status);
        Assert.IsNotNull(invoice.PaidDate);
        _repositoryMock.Verify(r => r.UpdateAsync(invoice), Times.Once);
    }

    [TestMethod]
    public async Task MarkAsPaidAsync_AlreadyPaid_ThrowsInvalidOperationException()
    {
        // Arrange
        var invoice = new Invoice { Id = 1, Status = InvoiceStatus.Paid };
        _repositoryMock.Setup(r => r.FindAsync(1)).ReturnsAsync(invoice);

        // Act & Assert
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => _sut.MarkAsPaidAsync(1));
    }
}
```

## Sample Fix Cycle

When the implementer encounters a build error, the fixer agent diagnoses and resolves it:

**Build output:**

```text
error CS0246: The type or namespace name 'Moq' could not be found (are you missing a using directive or an assembly reference?)
```

**Fixer diagnosis:** The test project is missing the Moq NuGet package.

**Fix applied:**

```bash
dotnet add tests/Contoso.Billing.Tests/Contoso.Billing.Tests.csproj package Moq
```

**Rebuild:** `dotnet build tests/Contoso.Billing.Tests/Contoso.Billing.Tests.csproj` → SUCCESS

---

**Another common cycle:**

**Build output:**

```text
error CS7036: There is no argument given that corresponds to the required parameter 'repository' of 'InvoiceService.InvoiceService(IInvoiceRepository)'
```

**Fixer diagnosis:** Test code instantiated `new InvoiceService()` without passing the required constructor parameter. The source uses a primary constructor with an `IInvoiceRepository` dependency.

**Fix applied:**

```csharp
// Before (wrong)
var sut = new InvoiceService();

// After (fixed)
var repositoryMock = new Mock<IInvoiceRepository>();
var sut = new InvoiceService(repositoryMock.Object);
```

**Rebuild:** SUCCESS

## Sample Final Report

What `code-testing-generator` produces at Step 9:

```markdown
## Test Generation Report

**Project**: Contoso.Billing
**Strategy**: Single pass

### Results
| Metric         | Value |
|----------------|-------|
| Tests created  | 9     |
| Tests passing  | 9     |
| Tests failing  | 0     |
| Files created  | 1     |

### Files Created
- `tests/Contoso.Billing.Tests/InvoiceServiceTests.cs` (9 tests)

### Coverage
- InvoiceService.CalculateTotal — 3 happy path, 2 error cases
- InvoiceService.GetByIdAsync — 1 happy path, 1 error case
- InvoiceService.MarkAsPaidAsync — 1 happy path, 1 error case

### Build Validation
- Scoped build: ✅ passed
- Full solution build (`dotnet build --no-incremental`): ✅ passed

### Next Steps
- Add integration tests for repository layer if needed
- Consider testing with multiple line items for CalculateTotal
```
