# Ruby Extension

Language-specific guidance for Ruby test generation.

## Rule #1: Investigate the Repo First

Before writing any test or running any command, read:

1. **Existing tests** — find `spec/**/*_spec.rb` (RSpec) or `test/**/*_test.rb` (Minitest) and copy their style (matchers, helpers, factories, contexts)
2. **`Gemfile` / `Gemfile.lock`** — Ruby version, test framework, supporting gems (`rspec`, `minitest`, `factory_bot`, `webmock`, `vcr`, `rails`)
3. **`.ruby-version`** / `.tool-versions` — pinned Ruby version
4. **Test helpers** — `spec/spec_helper.rb`, `spec/rails_helper.rb`, `test/test_helper.rb` — these dictate the load path, requires, and global config
5. **Rake tasks** — `Rakefile` may define a `default` task that runs the full test suite

Use the framework the repo already uses. Do not introduce RSpec into a Minitest project (or vice versa).

## Toolchain Detection

| Indicator | Manager | Run prefix |
|-----------|---------|------------|
| `Gemfile.lock` | Bundler | `bundle exec <cmd>` |
| `.ruby-version` + `rbenv` | rbenv | combine with `bundle exec` |
| `mise.toml` / `asdf` `.tool-versions` | mise/asdf | the wrapper handles version selection; still use `bundle exec` |
| Plain Ruby, no Bundler | system Ruby | `ruby <file>` (rare in real projects) |

Always run inside `bundle exec` if a `Gemfile.lock` is present — otherwise you may pick up a system gem version that disagrees with the lockfile.

## Build Commands

Ruby is interpreted — there is no compile step. The closest validations:

| Scope | Command |
|-------|---------|
| Syntax check | `ruby -c path/to/file.rb` |
| Lint (RuboCop) | `bundle exec rubocop path/to/file.rb` |
| Type check (Sorbet) | `bundle exec srb tc` (only if `sorbet/` dir exists) |
| Type check (RBS/Steep) | `bundle exec steep check` |

For Rails: load all classes once with `bundle exec rails zeitwerk:check` to catch missing constants before running tests.

## Test Commands

### RSpec

| Scope | Command |
|-------|---------|
| All specs | `bundle exec rspec` |
| Single file | `bundle exec rspec spec/models/widget_spec.rb` |
| Single line | `bundle exec rspec spec/models/widget_spec.rb:42` |
| By name | `bundle exec rspec -e "creates a widget"` |
| Tagged | `bundle exec rspec --tag focus` |
| Fail fast | `bundle exec rspec --fail-fast` |
| Documentation format | `bundle exec rspec --format documentation` |

### Minitest

| Scope | Command |
|-------|---------|
| All tests | `bundle exec rake test` (Rails) or `bundle exec ruby -Ilib -Itest -e 'Dir.glob("./test/**/*_test.rb").each { |f| require f }'` |
| Single file | `bundle exec ruby -Itest test/models/widget_test.rb` |
| Single test | `bundle exec ruby -Itest test/models/widget_test.rb -n test_creates_widget` |
| By name pattern | `... -n /pattern/` |

### Rails (any framework)

| Scope | Command |
|-------|---------|
| Default suite | `bin/rails test` (Minitest) or `bundle exec rspec` |
| Single Rails test file | `bin/rails test test/models/widget_test.rb:42` |
| System tests | `bin/rails test:system` |

Always prefer the wrapper script (`bin/rails`, `bin/rspec`) when present — they enforce the project's loader/setup.

## Lint Command

- `bundle exec rubocop` — autocorrect with `bundle exec rubocop -A` (only if existing tests already conform; do not autocorrect unrelated files)
- `bundle exec standardrb --fix` if `standard` is in the Gemfile
- Some Rails projects add `rubocop-rails`, `rubocop-rspec`, `rubocop-performance` — they enforce extra rules

## Project Layout and Loading

| Layout | Test placement |
|--------|----------------|
| Plain gem (RSpec) | `spec/` mirrors `lib/` (e.g. `lib/foo/bar.rb` → `spec/foo/bar_spec.rb`) |
| Plain gem (Minitest) | `test/` mirrors `lib/` (e.g. `test/foo/bar_test.rb`) |
| Rails (RSpec) | `spec/models`, `spec/controllers`, `spec/requests`, `spec/system`, etc. |
| Rails (Minitest) | `test/models`, `test/controllers`, `test/integration`, `test/system` |

**Loading source code:**

- RSpec: `spec/spec_helper.rb` typically does `require 'my_gem'` or sets `$LOAD_PATH`. Match its pattern in new specs by `require 'spec_helper'` (or `require 'rails_helper'` in Rails)
- Minitest: each `_test.rb` typically `require 'test_helper'`
- Rails uses Zeitwerk autoloading — do **not** add `require_relative '../../app/models/widget'`; just `require 'rails_helper'` and reference the constant

## Test File Naming

| Framework | File suffix | Class/example |
|-----------|-------------|---------------|
| RSpec | `_spec.rb` | `RSpec.describe Widget do ... end`, `it "..." do ... end` |
| Minitest (classic) | `_test.rb` | `class WidgetTest < Minitest::Test`, methods `def test_...` |
| Minitest (spec) | `_test.rb` | `describe Widget do ... it "..." do ... end end` |
| Rails Minitest | `_test.rb` | `class WidgetTest < ActiveSupport::TestCase` |

## RSpec Template

```ruby
require 'spec_helper'
require 'calculator'

RSpec.describe Calculator do
  subject(:calculator) { described_class.new }

  describe '#add' do
    it 'returns the sum of two positive numbers' do
      expect(calculator.add(2, 3)).to eq(5)
    end

    context 'with negative numbers' do
      it 'returns the correct sum' do
        expect(calculator.add(-1, 1)).to eq(0)
      end
    end

    it 'raises when given non-numeric input' do
      expect { calculator.add('a', 1) }.to raise_error(TypeError)
    end
  end
end
```

## Common Errors

| Error | Fix |
|-------|-----|
| `LoadError: cannot load such file -- foo` | Missing `require` or load path; check `spec_helper.rb` for the established pattern instead of patching `$LOAD_PATH` ad hoc |
| `NameError: uninitialized constant X` | Constant isn't loaded — in Rails, ensure you require `rails_helper`; in plain Ruby, add the appropriate `require` |
| `ArgumentError: wrong number of arguments (given X, expected Y)` | Read the method signature; pass keyword vs positional args correctly |
| `NoMethodError: undefined method 'foo' for nil:NilClass` | Test setup left a value `nil`; check `let`/`before` ordering and factory data |
| `Failure/Error: ... received :foo with unexpected arguments` (RSpec) | Tighten the matcher: `with(hash_including(...))` or relax to `with(any_args)` deliberately |
| `expected #<...> to receive :foo (1 time) but received it 0 times` | Either the code path didn't call the stub, or you stubbed the wrong receiver |
| `DEPRECATION WARNING` (Rails) | Address the deprecation rather than silencing it; tests that warn today break tomorrow |
| `ActiveRecord::PendingMigrationError` | Run `bin/rails db:migrate RAILS_ENV=test` before tests |
| `Mysql2::Error / PG::ConnectionBad` in CI | Tests need a database — check `config/database.yml` and CI service containers |
| `Capybara::ElementNotFound` (system tests) | Use `find` with explicit waits; do not add `sleep` |

## Mocking Rules (RSpec)

- Use `instance_double(Klass)` and `class_double(Klass)` — they verify that the method actually exists, unlike `double`
- `allow(obj).to receive(:method).and_return(value)` for stubs; `expect(obj).to receive(:method)` for interaction expectations
- Prefer `instance_double` over plain `double`; prefer dependency injection over `allow_any_instance_of`
- Use `let` for memoized helpers; use `let!` only when the side effect must run before each example
- Avoid global state mutation in tests — wrap in `around` blocks or use `ClimateControl` for env vars
- For HTTP, use `webmock` (`stub_request(:get, ...)`) or `vcr` cassettes if the project already uses them
- If a test needs more than 3 mocks, flag it as a design smell

## Mocking Rules (Minitest)

- Use `Minitest::Mock` for simple cases: `mock = Minitest::Mock.new; mock.expect(:method, return_value, [arg])`
- For richer mocking, projects commonly add `mocha`: `obj.expects(:method).returns(value)` (in `test_helper.rb`: `require 'mocha/minitest'`)
- Always verify mocks at end of test (`mock.verify` for `Minitest::Mock`); Mocha verifies automatically

## Rails Specifics

- Use the **smallest** spec type that covers the behavior: model spec for pure logic, request spec for HTTP, system spec only when JS/UI matters
- `rails-controller-testing` gem must be present for `assigns(:foo)` and `assert_template`
- `ActiveJob::TestHelper` and `ActiveSupport::Testing::TimeHelpers` (`travel_to`) come with Rails — use them instead of `Timecop` if Rails ≥ 5
- Use fixtures only if the project already uses them; `factory_bot` is more common in modern Rails apps
- Database transactions wrap each test by default — for system tests with browser drivers, use `DatabaseCleaner` strategies the project already configures

## Dependency Installation (Last Resort)

Only add gems after investigation confirms they are missing. Edit `Gemfile`:

```ruby
group :test do
  gem 'rspec'
  gem 'webmock'
end
```

Then run:

```
bundle install
```

Never `gem install` outside Bundler — it bypasses the lockfile and changes the global Ruby environment.

## Skip Coverage Tools

Do not configure or run coverage tools (SimpleCov). Coverage is measured separately by the evaluation harness.
