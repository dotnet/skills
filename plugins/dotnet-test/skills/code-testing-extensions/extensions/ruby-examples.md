# Ruby Pipeline Examples

Concrete input→output examples for the test generation pipeline targeting a Ruby codebase using RSpec. These show what each pipeline phase produces for a small gem-style project.

> Minitest follows the same coverage shape. Replace `RSpec.describe` / `it` / `expect` with `Minitest::Test` methods and assertions, and run through `bundle exec rake test` or the repo's established Minitest command.

## Source Under Test

A simple `InvoiceService` in a Ruby gem:

```text
Gemfile
lib/
  contoso_billing.rb
  contoso_billing/invoice.rb
  contoso_billing/invoice_repository.rb
  contoso_billing/invoice_service.rb
spec/spec_helper.rb
```

```ruby
# lib/contoso_billing/invoice_service.rb
require 'bigdecimal'
require 'time'

module ContosoBilling
  class InvoiceService
    def initialize(repository:, clock: -> { Time.now.utc })
      @repository = repository
      @clock = clock
    end

    def calculate_total(invoice)
      raise ArgumentError, 'invoice must not be nil' if invoice.nil?
      raise ArgumentError, 'Invoice has no line items.' if invoice.line_items.empty?

      subtotal = invoice.line_items.sum { |item| item.quantity * item.unit_price }
      tax = subtotal * invoice.tax_rate
      (subtotal + tax).round(2)
    end

    def get_by_id(id)
      invoice = @repository.find(id)
      raise KeyError, "Invoice #{id} not found." if invoice.nil?

      invoice
    end

    def mark_as_paid(id)
      invoice = get_by_id(id)
      raise StandardError, 'Invoice is already paid.' if invoice.status == :paid

      invoice.status = :paid
      invoice.paid_at = @clock.call
      @repository.update(invoice)
    end
  end
end
```

## Sample Research Output

What `code-testing-researcher` produces in `.testagent/research.md`:

```markdown
# Test Generation Research

## Project Overview
- **Path**: /work/contoso-billing
- **Language**: Ruby 3.3 (from `.ruby-version`)
- **Project Type**: Plain gem
- **Test Framework**: RSpec 3.x (detected in Gemfile.lock and `spec/spec_helper.rb`)
- **Run Prefix**: `bundle exec` required because Gemfile.lock is present

## Coverage Baseline
- **Initial Line Coverage**: unknown
- **Strategy**: broad
- **Existing Test Count**: 0 examples across 0 files

## Build & Test Commands
- **Syntax**: `ruby -c lib/contoso_billing/invoice_service.rb`
- **Discovery**: `bundle exec rspec --dry-run`
- **Single file**: `bundle exec rspec spec/contoso_billing/invoice_service_spec.rb`
- **All specs**: `bundle exec rspec`

## Files to Test

### High Priority
| File | Classes/Methods | Testability | Notes |
|------|-----------------|-------------|-------|
| lib/contoso_billing/invoice_service.rb | InvoiceService: calculate_total, get_by_id, mark_as_paid | High | Repository dependency and clock are injected |

## Testing Patterns
- Existing specs use `RSpec.describe`, `subject`, `let`, and `instance_double`.
- Recommend `instance_double('InvoiceRepository')` for the repository and a fixed clock lambda for time-dependent behavior.
```

## Sample Plan Output

```markdown
# Test Implementation Plan

## Overview
Generate RSpec tests for ContosoBilling::InvoiceService, covering calculation,
lookup, and paid-state transition behavior.

## Commands
- **Syntax**: `ruby -c spec/contoso_billing/invoice_service_spec.rb`
- **Discovery**: `bundle exec rspec --dry-run`
- **Test**: `bundle exec rspec spec/contoso_billing/invoice_service_spec.rb`

## Phase 1: InvoiceService

### Files to Test
- **Source**: `lib/contoso_billing/invoice_service.rb`
- **Test File**: `spec/contoso_billing/invoice_service_spec.rb`

**Methods to Test**:
1. `calculate_total` — tax, zero tax, rounding, nil, and empty line items
2. `get_by_id` — existing invoice and missing invoice
3. `mark_as_paid` — success with fixed clock, already-paid, missing
```

## Sample Generated Test File

```ruby
# spec/contoso_billing/invoice_service_spec.rb
require 'spec_helper'
require 'bigdecimal'
require 'ostruct'
require 'time'
require 'contoso_billing/invoice_service'

RSpec.describe ContosoBilling::InvoiceService do
  subject(:service) { described_class.new(repository: repository, clock: clock) }

  let(:repository) { instance_double('InvoiceRepository') }
  let(:fixed_time) { Time.utc(2025, 1, 1, 12, 0, 0) }
  let(:clock) { -> { fixed_time } }

  def build_invoice(id: 1, status: :pending, tax_rate: BigDecimal('0.10'), line_items: [OpenStruct.new(quantity: 1, unit_price: BigDecimal('100.00'))])
    OpenStruct.new(id: id, status: status, tax_rate: tax_rate, line_items: line_items, paid_at: nil)
  end

  describe '#calculate_total' do
    it 'returns the total for a single item with tax' do
      expect(service.calculate_total(build_invoice)).to eq(BigDecimal('110.00'))
    end

    it 'returns the subtotal when tax is zero' do
      invoice = build_invoice(
        tax_rate: BigDecimal('0'),
        line_items: [OpenStruct.new(quantity: 3, unit_price: BigDecimal('25.00'))]
      )

      expect(service.calculate_total(invoice)).to eq(BigDecimal('75.00'))
    end

    it 'rounds to two decimals' do
      invoice = build_invoice(
        tax_rate: BigDecimal('0.07'),
        line_items: [OpenStruct.new(quantity: 2, unit_price: BigDecimal('9.99'))]
      )

      expect(service.calculate_total(invoice)).to eq(BigDecimal('21.38'))
    end

    it 'raises for a nil invoice' do
      expect { service.calculate_total(nil) }.to raise_error(ArgumentError, /must not be nil/)
    end

    it 'raises when there are no line items' do
      expect { service.calculate_total(build_invoice(line_items: [])) }.to raise_error(ArgumentError, /no line items/)
    end
  end

  describe '#get_by_id' do
    it 'returns an existing invoice' do
      invoice = build_invoice(id: 42)
      allow(repository).to receive(:find).with(42).and_return(invoice)

      expect(service.get_by_id(42)).to be(invoice)
    end

    it 'raises KeyError for a missing invoice' do
      allow(repository).to receive(:find).with(999).and_return(nil)

      expect { service.get_by_id(999) }.to raise_error(KeyError, /999/)
    end
  end

  describe '#mark_as_paid' do
    it 'marks a pending invoice as paid and persists it' do
      invoice = build_invoice(id: 1)
      allow(repository).to receive(:find).with(1).and_return(invoice)
      allow(repository).to receive(:update)

      service.mark_as_paid(1)

      expect(invoice.status).to eq(:paid)
      expect(invoice.paid_at).to eq(fixed_time)
      expect(repository).to have_received(:update).with(invoice)
    end

    it 'raises and does not update an already-paid invoice' do
      invoice = build_invoice(id: 1, status: :paid)
      allow(repository).to receive(:find).with(1).and_return(invoice)
      allow(repository).to receive(:update)

      expect { service.mark_as_paid(1) }.to raise_error(StandardError, /already paid/)
      expect(repository).not_to have_received(:update)
    end
  end
end
```

## Sample Fix Cycle

When the implementer encounters a load or mock issue, the fixer agent diagnoses and resolves it.

**Test output:**

```text
LoadError: cannot load such file -- contoso_billing/invoice_service
```

**Fixer diagnosis:** The generated spec omitted `require 'spec_helper'`, so the gem's load-path setup did not run.

**Fix applied:** Add `require 'spec_helper'` as the first require and keep source requires consistent with existing specs.

**Rerun:** `bundle exec rspec spec/contoso_billing/invoice_service_spec.rb` → SUCCESS

---

**Another common cycle — verifying double mismatch:**

**Test output:**

```text
The InvoiceRepository class does not implement the instance method: find_by_id
```

**Fixer diagnosis:** `instance_double` caught a typo in the test setup. The production code calls `repository.find`, not `find_by_id`.

**Fix applied:** Stub `find` with the expected id instead of `find_by_id`.

**Rerun:** SUCCESS

## Sample Final Report

```markdown
## Test Generation Report

**Project**: contoso-billing (Ruby)
**Strategy**: Direct (single source file in scope)

### Results
| Metric         | Value |
|----------------|-------|
| Tests created  | 9     |
| Tests passing  | 9     |
| Tests failing  | 0     |
| Files created  | 1     |

### Files Created
- `spec/contoso_billing/invoice_service_spec.rb` (9 RSpec examples)

### Coverage
- InvoiceService#calculate_total — 3 happy path, 2 error cases
- InvoiceService#get_by_id — found and missing branches
- InvoiceService#mark_as_paid — success and already-paid branches

### Build / Test Validation
- Syntax: ✅ `ruby -c spec/contoso_billing/invoice_service_spec.rb`
- Discovery: ✅ `bundle exec rspec --dry-run` found the new examples
- Test run: ✅ `bundle exec rspec spec/contoso_billing/invoice_service_spec.rb`
```
