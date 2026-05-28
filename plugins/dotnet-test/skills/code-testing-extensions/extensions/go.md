# Go Extension

Language-specific guidance for Go test generation.

## Rule #1: Investigate the Repo First

Before writing any test or running any command, read:

1. **Existing tests** — find `*_test.go` files and copy their style (table-driven layout, helper usage, assertion library, build tags)
2. **`go.mod` / `go.sum`** — module path, Go version, dependencies (e.g. `testify`, `gomock`, `mockery`)
3. **Build/CI scripts** — `Makefile`, `magefile.go`, `Taskfile.yml`, `.github/workflows/*.yml`
4. **`go.work`** — if present, you are in a workspace; tests for a module must run from that module's directory or use `-C` (Go 1.20+)

Use whatever assertion style and test layout the repo already uses. Do not introduce `testify` if the repo uses the standard library only.

## Toolchain Detection

| Indicator | Meaning |
|-----------|---------|
| `go.mod` `go 1.x` directive | Minimum Go version — match it locally with `go version` |
| `go.work` at the root | Multi-module workspace; commands resolve dependent modules from sibling directories |
| `vendor/` directory | Vendored deps; many commands implicitly add `-mod=vendor` |
| `tools.go` with `//go:build tools` | Tool versions pinned in `go.mod` (e.g. `mockgen`); install with `go install` from the listed paths |

## Build Commands

| Scope | Command |
|-------|---------|
| Compile a package | `go build ./path/to/pkg` |
| Vet (static analysis) | `go vet ./...` |
| Compile tests without running | `go test -count=1 -run=^$ ./path/to/pkg` |
| Whole module | `go build ./...` |

`go build ./...` is the closest thing to a "does it compile" gate. It does not exercise test files — use `go test -run=^$` to type-check tests as well.

## Test Commands

| Scope | Command |
|-------|---------|
| All tests in a package | `go test ./path/to/pkg` |
| All tests in module | `go test ./...` |
| Single test | `go test -run '^TestName$' ./path/to/pkg` |
| Subtest | `go test -run '^TestName$/^subname$' ./path/to/pkg` |
| Verbose | `go test -v ./path/to/pkg` |
| Race detector | `go test -race ./...` |
| Disable cache | `go test -count=1 ./...` |
| Short mode | `go test -short ./...` |

- `-run` arguments are **regular expressions anchored** with `^...$`; without anchors the pattern matches as a substring
- `go test -count=1` is the canonical way to bypass the test result cache; never use a fake `-count=2` or environment hacks
- `-race` significantly slows tests and requires CGO — only enable if the repo's CI does

## Lint Command

Use the repo's lint script first (`make lint`, `task lint`). Otherwise detect from `.golangci.yml`/`.golangci.yaml`:

- `.golangci.yml` present → `golangci-lint run ./...`
- No config → `gofmt -w .` and `go vet ./...`
- `goimports` config / pre-commit hook → `goimports -w path/to/file.go`

Never disable existing linters in the test files you generate.

## Project Layout and Imports

Go uses package paths derived from the module path in `go.mod`.

| Scenario | Test placement | Package declaration |
|----------|----------------|----------------------|
| Internal-only test (white-box) | `foo_test.go` next to `foo.go` | `package foo` (same as production) |
| External-only test (black-box) | `foo_test.go` next to `foo.go` | `package foo_test` (forces use of public API) |
| Integration / build-tag gated | `foo_integration_test.go` | Add `//go:build integration` at top |

- Test files **must** end with `_test.go` — the toolchain ignores other names
- A package directory may contain both `package foo` and `package foo_test` test files simultaneously
- Helpers shared across tests in one package go in `helpers_test.go` — do not export them; put them in the `_test` package only if integration tests in another package need them
- Imports use the full module path: `import "github.com/org/module/pkg"` — copy the exact module path from `go.mod`

## Test Function Signatures

| Kind | Signature |
|------|-----------|
| Standard test | `func TestThing(t *testing.T)` |
| Subtests | `t.Run("name", func(t *testing.T) { ... })` |
| Benchmark | `func BenchmarkThing(b *testing.B)` |
| Example (godoc) | `func ExampleThing()` with `// Output:` comment |
| Fuzz (Go 1.18+) | `func FuzzThing(f *testing.F)` |
| Per-package setup | `func TestMain(m *testing.M)` — call `m.Run()` and `os.Exit` with its code |

Use **table-driven tests** when generating multiple cases for the same behavior — this is idiomatic Go and matches what most repos already use:

```go
func TestAdd(t *testing.T) {
    tests := []struct {
        name string
        a, b int
        want int
    }{
        {"positives", 2, 3, 5},
        {"negatives", -1, -1, -2},
    }
    for _, tt := range tests {
        t.Run(tt.name, func(t *testing.T) {
            if got := Add(tt.a, tt.b); got != tt.want {
                t.Errorf("Add(%d,%d) = %d, want %d", tt.a, tt.b, got, tt.want)
            }
        })
    }
}
```

When iterating with `t.Run` over a loop variable on Go < 1.22, capture it with `tt := tt` to avoid closure-over-loop-variable bugs.

## Common Errors

| Error | Fix |
|-------|-----|
| `package X is not in std` / `cannot find module providing package X` | Add the import to `go.mod`: `go get path/to/module@version`, then `go mod tidy` |
| `import cycle not allowed in test` | Move shared helpers to a separate package, or switch to a `_test` package for black-box tests |
| `undefined: X` in `_test` package | The symbol is unexported; either use `package foo` (white-box) or export it intentionally |
| `t.Parallel called multiple times` | Each subtest can call `t.Parallel()` once; do not call it twice in the same test |
| `panic: test executed panic(nil) or runtime.Goexit` | A goroutine called `t.Fatal` outside the test goroutine; only the main test goroutine may call `Fatal`/`FailNow` |
| `flag provided but not defined: -X` | Flags registered in `init()` of test files must use `flag.NewFlagSet` carefully; place test-only flags in `TestMain` |
| `go: cannot find main module` | Run inside the module directory (where `go.mod` lives), or use `-C path` (Go 1.20+) |
| `build constraints exclude all Go files in...` | Build tags filtered out every file — match the repo's tag with `-tags=integration` etc. |
| `missing go.sum entry for module` | Run `go mod download` or `go mod tidy` |
| Race detector reports data race | Fix the race; do not silence it. CGO must be enabled |

## Mocking Rules

Go has no reflection-based mocking framework that's universally adopted. Pick what the repo already uses:

- **Interfaces + hand-written fakes** (most idiomatic) — define a small interface in the consumer package and pass a struct that implements it
- **`gomock` / `mockgen`** — if the repo has `//go:generate mockgen ...` directives or `mocks/` directories, regenerate via `go generate ./...` rather than editing generated files
- **`testify/mock`** — used in many repos; instantiate with `new(MockX)` and chain `.On("Method", ...).Return(...)`
- **`httptest`** — for HTTP clients/servers; spin up `httptest.NewServer` instead of mocking `http.Client`

Always prefer dependency injection over global function patching. If a test needs more than 3 mocks, flag it as a design smell.

## Concurrency and Cleanup

- Use `t.Cleanup(func() { ... })` instead of deferring in test bodies — runs even if `t.FailNow` fires
- Use `t.TempDir()` for temp files — auto-cleaned at test end
- Use `t.Context()` (Go 1.24+) or pass an explicit `context.Background()` — never call real network or filesystem APIs without one in long-running tests

## Dependency Installation (Last Resort)

Only install packages after investigation confirms they are missing:

```
go get github.com/stretchr/testify@latest
go mod tidy
```

Run `go mod tidy` after any `go get` to keep `go.sum` consistent. Never edit `go.sum` by hand.

## Skip Coverage Tools

Do not configure or run coverage tools (`-cover`, `-coverprofile`, `go tool cover`). Coverage is measured separately by the evaluation harness.
