# Go Test Framework Reference (`testing` package, testify)

Reference data for analyzing Go test code. Used by the polyglot test analysis skills.

## Capability tags

| Capability | Support |
|------------|---------|
| Test discovery | Strong — `*_test.go`, `func TestXxx(t *testing.T)` |
| Assertion detection | Moderate — bare `if … { t.Errorf(...) }` patterns; stronger with testify |
| Sleep/delay detection | Strong — `time.Sleep`, `<-time.After` |
| Skip/ignore detection | Strong — `t.Skip`, `t.SkipNow`, build tags |
| Setup/teardown detection | Strong — `TestMain`, `t.Cleanup`, subtests |
| Tag support | **report-only** by default — no canonical attribute; build tags can scope tests but are coarse |

## Test File Identification

| Convention | Description |
|------------|-------------|
| `*_test.go` | Test files (must end with `_test.go`) |
| `func TestXxx(t *testing.T)` | Standard tests |
| `func BenchmarkXxx(b *testing.B)` | Benchmarks |
| `func ExampleXxx()` | Documentation examples (act as tests when they have `// Output:` blocks) |
| `func FuzzXxx(f *testing.F)` | Fuzz tests (Go 1.18+) |
| `t.Run("subtest", func(t *testing.T) {...})` | Subtests / table-driven cases |

Test packages may be `foo` (white-box) or `foo_test` (black-box). The latter only sees exported names.

## Assertion APIs

Go's `testing` package has no built-in assertion library. Tests fail by calling `t.Error*` / `t.Fatal*`.

| Category | Standard `testing` | testify (`require` / `assert`) |
|----------|-------------------|--------------------------------|
| Equality | `if got != want { t.Errorf("got %v, want %v", got, want) }` | `assert.Equal(t, want, got)` |
| Boolean | `if !ok { t.Error("expected ok") }` | `assert.True(t, ok)` |
| Nil | `if v != nil { t.Error(...) }` | `assert.Nil(t, v)` / `assert.NotNil(t, v)` |
| Error | `if err != nil { t.Fatal(err) }` | `require.NoError(t, err)` / `assert.Error(t, err)` / `assert.ErrorIs(t, err, target)` |
| Panic | `defer func() { if r := recover(); r == nil { t.Error("expected panic") } }()` | `assert.Panics(t, func() {...})` |
| Type | `if _, ok := v.(T); !ok { t.Error(...) }` | `assert.IsType(t, T{}, v)` |
| Membership | manual loop or `slices.Contains` | `assert.Contains(t, slice, item)` |
| String | `if !strings.Contains(...) { t.Error(...) }` | `assert.Contains(t, s, sub)` |
| Fail | `t.Fail()` / `t.FailNow()` / `t.Fatal(...)` / `t.Fatalf(...)` | `t.FailNow()` / `require.Fail(t, "...")` |

**`require` vs `assert` (testify):** `require.*` calls `t.FailNow()` and stops the test; `assert.*` records the failure and continues. Tests that need preconditions before further work should use `require.NoError(t, err)`.

**Bare `if ... { t.Error... }` is the canonical Go assertion form.** Do NOT flag these as missing-framework-API smells.

Other libraries: `gotest.tools/v3` (`assert.Check`, `assert.Equal`), `go-cmp` (`cmp.Diff`).

## Sleep/Delay Patterns

| Pattern | Example |
|---------|---------|
| Hard sleep | `time.Sleep(time.Second)` |
| Timer wait | `<-time.After(time.Second)` |
| Loop wait | `for !ready() { time.Sleep(10*time.Millisecond) }` |
| Acceptable wait | `<-ctx.Done()` or `<-done` channels driven by the SUT |
| Deadline | `ctx, cancel := context.WithTimeout(...)` |

## Skip/Ignore Annotations

| Mechanism | Example |
|-----------|---------|
| `t.Skip("reason")` | Inline skip at any point in the test body |
| `t.SkipNow()` | Skip without a message |
| Build tag at top of file | `//go:build integration` (excludes file unless `-tags=integration`) |
| `testing.Short()` guard | `if testing.Short() { t.Skip("skipping in short mode") }` |
| `t.Skipf` | Formatted skip messages |

There is no `@Disabled`-style permanent disable. Build tags and skip guards are the idiomatic way to gate tests.

## Exception Handling — Idiomatic Alternatives

Go uses error returns and panics; there is no `try/catch`. Testing patterns:

```go
// Error return:
if _, err := svc.PlaceOrder(empty); err == nil {
    t.Error("expected error, got nil")
}
// Better with testify:
_, err := svc.PlaceOrder(empty)
require.Error(t, err)
assert.Contains(t, err.Error(), "at least one item")

// Error-target match (Go 1.13+):
assert.ErrorIs(t, err, ErrEmptyOrder)
assert.ErrorAs(t, err, &validationErr)

// Panic:
assert.PanicsWithValue(t, "bad input", func() { mustParse("xxx") })
```

Flag tests that ignore returned errors (`_, _ = svc.Foo()`) without subsequent assertion.

## Mystery Guest — Common Go Patterns

| Indicator | What to look for |
|-----------|------------------|
| File system | `os.ReadFile`, `os.Open`, hard-coded absolute paths |
| Database | `sql.Open` against a real DB connection string, raw `pgx.Connect` |
| Network | `http.Get`, `http.Post` to real URLs, raw `net.Dial` |
| Environment | `os.Getenv("X")` (especially in test body without `t.Setenv`) |
| Acceptable | `t.TempDir()`, `t.Setenv()`, `httptest.NewServer`, `sqlmock`, `dockertest` / `testcontainers-go` (integration-acknowledged), in-memory `bytes.Buffer` |

## Integration Test Markers

- Build tag at file top: `//go:build integration` / `//go:build e2e` (run via `go test -tags=integration`)
- File name suffix: `*_integration_test.go`, `*_e2e_test.go`
- Package directory: `tests/integration/`, `internal/integrationtests/`
- `testing.Short()` guard pattern: `if testing.Short() { t.Skip("integration test") }`

## Setup/Teardown

| Mechanism | Description |
|-----------|-------------|
| `TestMain(m *testing.M)` | Package-level setup/teardown — runs `m.Run()` between setup and teardown |
| `t.Cleanup(fn)` | Per-test cleanup that runs after the test (even on failure) |
| Helper functions | `func setupFoo(t *testing.T) (*Foo, func())` returning a teardown closure |
| Subtests with shared setup | `func TestX(t *testing.T) { foo := setup(t); t.Run("a", ...); t.Run("b", ...) }` |
| testify suites | `type FooSuite struct{ suite.Suite }` with `SetupTest`, `TearDownTest`, `SetupSuite`, `TearDownSuite` |

## Tag/Trait Attributes (for `test-tagging`)

**Default mode: report-only.** Go has no per-test tag attribute. Strategies:

- **Build tags** scope an entire file (coarse): `//go:build integration`
- **Subtest names** can encode tags: `t.Run("[positive] valid input returns ok", ...)`
- **Test name prefixes**: `func TestNegative_InvalidInput_Returns400`
- **testify suites** with grouping methods

When the project already follows one of these conventions, switch to `auto-edit` mode and apply it consistently.

## Language-specific calibration notes

- **Table-driven tests** with `for _, tc := range tests { t.Run(tc.name, func(t *testing.T) { ... }) }` are idiomatic — **do NOT flag the `for` loop as Conditional Test Logic.**
- **Bare `if … t.Errorf` patterns** are the canonical assertion form. Do NOT flag as "no framework API used."
- **Goroutine leaks in tests** are a real smell — recommend `goleak.VerifyNone(t)` or `t.Cleanup`.
- **`t.Parallel()`** in tests: races on shared fixture data are a smell; tests calling `t.Setenv` then `t.Parallel` will fail in newer Go versions.
- **`require` vs `assert` mixing**: subsequent code after a failed `assert.*` may panic on `nil`. Prefer `require.*` for preconditions.
- **Examples with `// Output:`** are tests; treat the `// Output:` block as the assertion.
- **Fuzz tests** without `f.Add(...)` seed inputs may only run with `-fuzz`; flag as a coverage gap.
- **Generated mocks** (mockery, mockgen) — verify call expectations count as assertions.
- **Missing `t.Helper()`** in helper functions is not a smell per se but degrades failure location reporting.
