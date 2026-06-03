# Ruby Test Frameworks Reference (RSpec, Minitest)

Reference data for analyzing Ruby test code. Used by the polyglot test analysis skills.

## Capability tags

| Capability | Support |
|------------|---------|
| Test discovery | Strong — `spec/**/*_spec.rb`, `test/**/*_test.rb` |
| Assertion detection | Strong — `expect`, `assert_*` |
| Sleep/delay detection | Strong — `sleep`, `Kernel#sleep` |
| Skip/ignore detection | Strong — `skip`, `pending`, `xit` |
| Setup/teardown detection | Strong — `before`, `setup` |
| Tag support | **auto-edit** — RSpec metadata, Minitest `tag` (via gems) |

## Test File Identification

| Framework | File convention | Test method markers |
|-----------|----------------|---------------------|
| RSpec | `spec/**/*_spec.rb` | `describe`, `context`, `it`, `specify`, `example` |
| Minitest | `test/**/*_test.rb` | `Minitest::Test` subclass with methods starting `test_`, or `Minitest::Spec` with `it` |

## Assertion APIs

| Category | RSpec (`expect`) | Minitest (`assert_*`) |
|----------|------------------|-----------------------|
| Equality | `expect(x).to eq(y)` / `eql(y)` | `assert_equal expected, actual` |
| Identity | `expect(x).to be(y)` | `assert_same expected, actual` |
| Boolean | `expect(x).to be_truthy` / `be_falsey` | `assert x` / `refute x` |
| Nil | `expect(x).to be_nil` | `assert_nil x` / `refute_nil x` |
| Exception | `expect { fn }.to raise_error(SomeError, /msg/)` | `assert_raises(SomeError) { fn }` |
| Type | `expect(x).to be_a(T)` / `be_instance_of(T)` | `assert_kind_of T, x` / `assert_instance_of T, x` |
| Membership | `expect(arr).to include(item)` | `assert_includes arr, item` |
| String | `expect(s).to match(/regex/)` | `assert_match(/regex/, s)` |
| Predicate | `expect(x).to be_empty` (auto: `x.empty?`) | `assert_empty x` |
| Change | `expect { code }.to change(obj, :attr).from(x).to(y)` | manual before/after assertion |
| Throw | `expect { throw :sym }.to throw_symbol(:sym)` | `assert_throws(:sym) { ... }` |
| Output | `expect { puts "x" }.to output("x\n").to_stdout` | `assert_output("x\n") { puts "x" }` |
| Fail | `fail("reason")` (built-in) | `flunk "reason"` |

Third-party libraries: Shoulda Matchers, FactoryBot (for setup, not assertions), Capybara (`have_content`, `have_selector`).

## Sleep/Delay Patterns

| Pattern | Example |
|---------|---------|
| Sleep | `sleep 1` / `sleep(0.5)` |
| Capybara explicit wait (acceptable) | `using_wait_time(5) { find('#x') }` |
| Loop wait | `until condition; sleep 0.1; end` |
| Timecop / ActiveSupport::Testing::TimeHelpers (acceptable) | `travel_to(1.hour.from_now)` instead of real sleep |

## Skip/Ignore Annotations

| Framework | Skip |
|-----------|------|
| RSpec | `skip("reason")`, `xit`, `xdescribe`, `xcontext`, `pending("reason")`, `it("...", :skip)`, `it("...", skip: "reason")`, focused `fit`, `fdescribe`, `fcontext` |
| Minitest | `skip("reason")` inside a test method, `skip_until "<date>", "reason"` (via `minitest-skip-until` gem) |

`fit` / `fdescribe` (focused) committed to source is anti-pattern when `--fail-if-no-examples` / RSpec `--only-failures` isn't gating it.

## Exception Handling — Idiomatic Alternatives

```ruby
# RSpec (preferred):
expect { service.place_order(empty_order) }
  .to raise_error(InvalidOrderError, /at least one item/)

# Minitest:
err = assert_raises(InvalidOrderError) { service.place_order(empty_order) }
assert_match(/at least one item/, err.message)
```

Flag tests with bare `begin/rescue` that swallow exceptions or `rescue => e` patterns without subsequent assertion.

## Mystery Guest — Common Ruby/Rails Patterns

| Indicator | What to look for |
|-----------|------------------|
| File system | `File.read`, `File.open`, `Pathname#read`, hard-coded paths |
| Database | direct `ActiveRecord::Base.connection.execute`, real DB writes outside transactional fixtures |
| Network | `Net::HTTP`, `URI.open`, `RestClient`, `Faraday` against real URLs |
| Environment | `ENV["X"]` (especially without `ENV.fetch("X", default)`) |
| Acceptable | `WebMock`, `VCR`, `Tempfile`, `StringIO`, `ActiveRecord` transactional fixtures, `database_cleaner`, factory builders |

## Integration Test Markers

- Folder convention: `spec/system/`, `spec/features/`, `spec/integration/`, `test/integration/`, `test/system/`
- RSpec metadata: `it "...", type: :system`, `:feature`, `:request`, `:integration`
- Rails: `ActionDispatch::IntegrationTest` subclass, `ActionDispatch::SystemTestCase`
- Capybara involvement implies system/feature test

## Setup/Teardown

| Framework | Per-test | Per-suite |
|-----------|----------|-----------|
| RSpec | `before(:each)` / `before { ... }` | `before(:all)` / `before(:context)` |
| RSpec | `after(:each)` | `after(:all)` |
| RSpec | `around { |ex| ex.run }` (wrapping) | n/a |
| Minitest | `setup` method | `before_all` (via `minitest-hooks` gem) |
| Minitest | `teardown` method | `after_all` (via gem) |
| Rails | `ActiveSupport::TestCase` `setup` / `teardown` blocks | `setup do ... end` |

## Tag/Trait Attributes (for `test-tagging`)

| Framework | Tag mechanism | Example |
|-----------|---------------|---------|
| RSpec | metadata hash | `it "creates order", :positive, :critical_path do ... end` |
| RSpec | metadata key/value | `describe Order, type: :model, tag: :positive do ... end` |
| Minitest | `tag` via `minitest-tagz` / `minitest-tagged` gems | varies by gem |
| Rails | `test_tagged` helper (Rails 7.1+) | `test "x", tag: :positive do ... end` |

RSpec filters can drive tag selection: `rspec --tag positive`, `rspec --tag ~slow`.

## Language-specific calibration notes

- **Predicate matchers** (`be_empty`, `be_valid`) auto-derive from `?` methods on the object. Treat as state/side-effect assertions.
- **`change` matcher** is a state assertion: `expect { code }.to change(obj, :attr)` verifies side effects. Do not treat as missing assertion.
- **Shared examples** (`it_behaves_like "...")` and shared contexts are NOT duplicate tests — they are the consolidated form.
- **`let` / `let!`** for fixtures: `let!` runs eagerly per test, `let` lazily. Tests that create heavy `let!` blocks for fields used by only one test are General Fixture smells.
- **Implicit subject** (`subject { described_class.new(args) }`, `it { is_expected.to be_valid }`) is a valid concise form.
- **FactoryBot `build` vs `create`**: `create` hits the database, `build` does not. Tests that `create` records for assertions that don't need persistence inflate test time — note but don't flag as critical.
- **Capybara `find` without an explicit selector** can be slow/flaky; recommend more specific selectors.
- **RSpec `pending` differs from `skip`**: `pending` runs the test and expects failure; `skip` does not run it.
