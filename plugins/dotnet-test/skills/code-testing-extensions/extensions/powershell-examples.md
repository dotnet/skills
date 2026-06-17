# PowerShell Pipeline Examples

Concrete input→output examples for the test generation pipeline targeting a PowerShell module using Pester v5. These show what each pipeline phase produces for a small module.

## Source Under Test

A simple `InvoiceService` module:

```text
src/
  Contoso.Billing.psd1
  Contoso.Billing.psm1
Tests/                      (empty)
```

```powershell
# src/Contoso.Billing.psm1
enum InvoiceStatus {
    Pending
    Paid
}

function Get-InvoiceTotal {
    [CmdletBinding()]
    param([Parameter(Mandatory)][pscustomobject]$Invoice)

    if (-not $Invoice.LineItems -or $Invoice.LineItems.Count -eq 0) {
        throw 'Invoice has no line items.'
    }

    $subtotal = 0
    foreach ($lineItem in $Invoice.LineItems) {
        $subtotal += $lineItem.Quantity * $lineItem.UnitPrice
    }

    [math]::Round($subtotal + ($subtotal * $Invoice.TaxRate), 2)
}

function Get-InvoiceById {
    [CmdletBinding()]
    param([Parameter(Mandatory)][int]$Id, [Parameter(Mandatory)][scriptblock]$FindInvoice)

    $invoice = & $FindInvoice $Id
    if ($null -eq $invoice) {
        throw "Invoice $Id not found."
    }

    $invoice
}

function Set-InvoicePaid {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][int]$Id,
        [Parameter(Mandatory)][scriptblock]$FindInvoice,
        [Parameter(Mandatory)][scriptblock]$UpdateInvoice,
        [scriptblock]$GetNow = { Get-Date }
    )

    $invoice = Get-InvoiceById -Id $Id -FindInvoice $FindInvoice
    if ($invoice.Status -eq [InvoiceStatus]::Paid) {
        throw 'Invoice is already paid.'
    }

    $invoice.Status = [InvoiceStatus]::Paid
    $invoice.PaidDate = & $GetNow
    & $UpdateInvoice $invoice
}

Export-ModuleMember -Function Get-InvoiceTotal, Get-InvoiceById, Set-InvoicePaid
```

## Sample Research Output

What `code-testing-researcher` produces in `.testagent/research.md`:

```markdown
# Test Generation Research

## Project Overview
- **Path**: C:\work\contoso-billing
- **Language**: PowerShell 7.4
- **Module**: `src/Contoso.Billing.psd1` imports `Contoso.Billing.psm1`
- **Test Framework**: Pester v5

## Coverage Baseline
- **Initial Line Coverage**: unknown
- **Strategy**: broad
- **Existing Test Count**: 0 tests across 0 files

## Build & Test Commands
- **Module load**: `Import-Module ./src/Contoso.Billing.psd1 -Force -ErrorAction Stop`
- **Discovery**: `Invoke-Pester -Configuration @{ Run = @{ Path = './Tests'; PassThru = $true; SkipRun = $true } }`
- **Test**: `Invoke-Pester -Path ./Tests -Output Detailed`

## Files to Test

### High Priority
| File | Functions | Testability | Notes |
|------|-----------|-------------|-------|
| src/Contoso.Billing.psm1 | Get-InvoiceTotal, Get-InvoiceById, Set-InvoicePaid | High | Dependencies are scriptblocks, easy to fake; clock is injectable |

## Testing Patterns
- No existing patterns; recommend Pester v5 `Describe` / `Context` / `It`, `BeforeAll` module import, `-TestCases` for total calculations, and scriptblock fakes for repository operations.
```

## Sample Plan Output

```markdown
# Test Implementation Plan

## Overview
Generate Pester v5 tests for total calculation, repository lookup, and the
paid-state transition. Single phase since there is one module file.

## Commands
- **Import**: `Import-Module ./src/Contoso.Billing.psd1 -Force -ErrorAction Stop`
- **Test**: `Invoke-Pester -Path ./Tests/Contoso.Billing.Tests.ps1 -Output Detailed`

## Phase 1: Contoso.Billing

### Files to Test
- **Source**: `src/Contoso.Billing.psm1`
- **Test File**: `Tests/Contoso.Billing.Tests.ps1`

**Functions to Test**:
1. `Get-InvoiceTotal` — table-driven happy paths and empty-line-items error
2. `Get-InvoiceById` — existing invoice and missing invoice
3. `Set-InvoicePaid` — status/date update and persistence; already-paid error; missing invoice error
```

## Sample Generated Test File

```powershell
# Tests/Contoso.Billing.Tests.ps1
BeforeAll {
    Import-Module (Join-Path $PSScriptRoot '..' 'src' 'Contoso.Billing.psd1') -Force -ErrorAction Stop

    function New-TestInvoice {
        param(
            [int]$Id = 1,
            [InvoiceStatus]$Status = [InvoiceStatus]::Pending,
            [double]$TaxRate = 0.10,
            [object[]]$LineItems = @(@{ Quantity = 1; UnitPrice = 100.00 })
        )

        [pscustomobject]@{
            Id = $Id
            Status = $Status
            TaxRate = $TaxRate
            LineItems = $LineItems
            PaidDate = $null
        }
    }
}

Describe 'Contoso.Billing invoice functions' {
    Context 'Get-InvoiceTotal' {
        It 'returns <Expected> for <Name>' -TestCases @(
            @{ Name = 'single item with tax'; LineItems = @(@{ Quantity = 1; UnitPrice = 100.00 }); TaxRate = 0.10; Expected = 110.00 }
            @{ Name = 'multi quantity zero tax'; LineItems = @(@{ Quantity = 3; UnitPrice = 25.00 }); TaxRate = 0.00; Expected = 75.00 }
            @{ Name = 'rounds to two decimals'; LineItems = @(@{ Quantity = 2; UnitPrice = 9.99 }); TaxRate = 0.07; Expected = 21.38 }
        ) {
            param($LineItems, $TaxRate, $Expected)

            $invoice = New-TestInvoice -LineItems $LineItems -TaxRate $TaxRate

            Get-InvoiceTotal -Invoice $invoice | Should -BeExactly $Expected
        }

        It 'throws when the invoice has no line items' {
            $invoice = New-TestInvoice -LineItems @()

            { Get-InvoiceTotal -Invoice $invoice } | Should -Throw '*no line items*'
        }
    }

    Context 'Get-InvoiceById' {
        It 'returns an existing invoice' {
            $expected = New-TestInvoice -Id 42
            $findInvoice = { param($Id) if ($Id -eq 42) { $expected } }

            $result = Get-InvoiceById -Id 42 -FindInvoice $findInvoice

            $result | Should -BeSame $expected
        }

        It 'throws when the invoice is missing' {
            $findInvoice = { $null }

            { Get-InvoiceById -Id 999 -FindInvoice $findInvoice } | Should -Throw '*999*'
        }
    }

    Context 'Set-InvoicePaid' {
        It 'marks a pending invoice as paid and persists it' {
            $invoice = New-TestInvoice -Id 1
            $script:updatedInvoice = $null
            $fixedNow = [datetime]'2025-01-01T12:00:00Z'
            $findInvoice = { param($Id) if ($Id -eq 1) { $invoice } }
            $updateInvoice = { param($Invoice) $script:updatedInvoice = $Invoice }

            Set-InvoicePaid -Id 1 -FindInvoice $findInvoice -UpdateInvoice $updateInvoice -GetNow { $fixedNow }

            $invoice.Status | Should -Be ([InvoiceStatus]::Paid)
            $invoice.PaidDate | Should -Be $fixedNow
            $script:updatedInvoice | Should -BeSame $invoice
        }

        It 'throws and does not update an already-paid invoice' {
            $invoice = New-TestInvoice -Status ([InvoiceStatus]::Paid)
            $script:updatedInvoice = $null
            $findInvoice = { $invoice }
            $updateInvoice = { param($Invoice) $script:updatedInvoice = $Invoice }

            { Set-InvoicePaid -Id 1 -FindInvoice $findInvoice -UpdateInvoice $updateInvoice } | Should -Throw '*already paid*'
            $script:updatedInvoice | Should -BeNullOrEmpty
        }
    }
}
```

## Sample Fix Cycle

When the implementer hits a Pester discovery or run issue, the fixer agent diagnoses and resolves it.

**Test output:**

```text
CommandNotFoundException: The term 'Get-InvoiceTotal' is not recognized
```

**Fixer diagnosis:** The module import was placed at script top level. Import the module in `BeforeAll` so the Pester run phase sees the exported functions.

**Fix applied:** Move `Import-Module ... -Force` into `BeforeAll` (as shown above).

**Rerun:** `Invoke-Pester -Path ./Tests/Contoso.Billing.Tests.ps1 -Output Detailed` → SUCCESS

## Sample Final Report

```markdown
## Test Generation Report

**Project**: contoso-billing (PowerShell)
**Strategy**: Direct (single module in scope)

### Results
| Metric         | Value |
|----------------|-------|
| Tests created  | 8     |
| Tests passing  | 8     |
| Tests failing  | 0     |
| Files created  | 1     |

### Files Created
- `Tests/Contoso.Billing.Tests.ps1` (8 Pester examples, 3 data-driven total cases)

### Coverage
- Get-InvoiceTotal — 3 happy path, 1 error case
- Get-InvoiceById — found and missing branches
- Set-InvoicePaid — success and already-paid branches

### Build / Test Validation
- Module load: ✅ `Import-Module ./src/Contoso.Billing.psd1 -Force -ErrorAction Stop`
- Discovery: ✅ Pester found 8 tests
- Test run: ✅ `Invoke-Pester -Path ./Tests -Output Detailed`
```
