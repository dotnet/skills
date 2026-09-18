---
name: csharp-expert
description: >-
  Implement, debug, or review C# source when the user requests a C# implementation,
  bug fix, or correctness review involving language, compiler, runtime, API design,
  nullability, async/cancellation, exceptions, allocation, or concurrency. USE FOR:
  C# compiler errors; overload or generic inference; nullable warnings; async/await,
  Task, ValueTask, IAsyncEnumerable, CancellationToken; records, pattern matching,
  spans, memory ownership, disposal, exceptions, and focused C# feature or bug-fix
  work. DO NOT USE FOR: behavior-preserving rename, move, extract, inline,
  consolidation, or modernization requests (use csharp-refactoring); standalone
  file-based C# apps (use csharp-scripts); local SDK installation (use setup-local-sdk);
  framework-specific WinForms, ASP.NET Core, MAUI, MSBuild, NuGet, upgrade, or
  diagnostic work when that framework or tool is the primary problem (use its owning skill).
license: MIT
---

# C# Expert

## Purpose

Make the smallest correct C# change that fits the repository's target frameworks, language version,
nullable context, API contracts, and performance constraints. Use compilation and focused tests to
settle language/runtime questions; do not "fix" unfamiliar valid syntax from memory or broaden a
focused C# task into an architecture rewrite.

## Boundaries

Use this skill when the decision that determines correctness is primarily C#:

- Resolving compiler diagnostics, overload resolution, generic constraints, variance, or type inference.
- Implementing or fixing async, cancellation, disposal, exception, nullability, or concurrency behavior.
- Choosing between language/runtime mechanisms such as class versus record, `Task` versus `ValueTask`,
  iterator versus materialization, or array versus span/memory.
- Reviewing a focused C# implementation for correctness, contract safety, and measured hot-path cost.

Do not use it when:

- The requested outcome is structural and behavior-preserving: rename, move, extract, inline, merge,
  split, or analyzer-driven modernization. Use `csharp-refactoring`.
- The user wants a new file-based C# program without a project. Use `csharp-scripts`.
- The primary issue belongs to a framework or tool (for example WinForms designer serialization,
  ASP.NET Core middleware/model binding, MAUI workloads, MSBuild evaluation, NuGet restore, SDK
  installation, framework upgrades, dumps, traces, or counters). Use the owning skill.
- The request is formatting-only or asks to suppress diagnostics without addressing their cause.

For mixed work, keep the boundary explicit: apply this skill only to the C# language/runtime portion
and preserve the owning framework's established conventions. If the entire request is a
behavior-preserving refactor, stop and hand off rather than implementing it here.

## Inputs

| Input | Required | How to obtain it |
|---|---:|---|
| Requested behavior, diagnostic, or review target | Yes | Use the prompt; reproduce the exact failure when possible. |
| Target project and source files | Yes | Discover them in the current repository; do not require the user to provide obvious paths. |
| TFM, SDK, and C# language version | Yes | Inspect the project, `global.json`, `Directory.Build.*`, and effective build diagnostics. |
| Nullable/analyzer/style policy | Yes | Inspect project properties and applicable `.editorconfig` files. |
| Existing tests and build commands | Yes | Follow repository scripts and contribution guidance before inventing commands. |
| Performance or compatibility constraint | No | Require evidence when it changes the implementation; do not assume a hot path or breaking-change budget. |

## Rules That Change the Answer

Use the observed failure to select one mechanism. Do not list several plausible rewrites when the
project and executable contract identify the defect.

| Signal | Do | Never | Verify |
|---|---|---|---|
| Cancellation is requested but work finishes normally or too late | Find the first blocking/cancellable operation and pass the same token through every affected call; for a timeout policy, cancel a linked source and still await the worker | Race only the await with `Task.Delay`/`Task.WhenAny` while the original work continues | The underlying operation stops promptly and the caller observes cancellation |
| A timeout is reported but state changes later | Give the timeout policy ownership of a cancellation source for the worker, distinguish timeout from caller cancellation, and observe the worker before returning | Return from `WaitAsync`, `WhenAny`, or a delay race while the abandoned task can still mutate state or fault unobserved | Wait beyond the worker's normal duration and prove that no completion side effect occurs |
| A wrapper disposes a caller-supplied stream | Dispose the wrapper with `leaveOpen: true` when the caller retains ownership; preserve position only if the API contract requires it | Leave the wrapper undisposed or close a resource the caller owns | Use the supplied stream again after the method returns |
| Deferred enumeration touches a stream, reader, context, or pooled buffer | Keep the resource lifetime inside the iterator/async iterator or materialize before returning | Return a query or iterator whose backing resource was disposed by the creating method | Enumerate after return and confirm disposal occurs when enumeration completes |
| Returned memory changes after the method exits | Copy into caller-owned storage before returning the rented array, or transfer ownership through an explicit disposable owner | Return `Memory<T>`, `ReadOnlyMemory<T>`, or an array segment backed by storage already returned to a pool | Poison/re-rent the pooled array after return and confirm the result stays unchanged |
| Equal domain identifiers fail hash lookup | Prefer a record/record struct when that matches the type's contract; otherwise implement typed equality and a compatible hash code together | Add `Equals` without `GetHashCode`, use mutable equality members, or change inheritance semantics accidentally | Compare distinct equal instances and use one to find the other in a hash-based collection |
| A single numeric update loses concurrent writes | Use `Interlocked` for an independent atomic value; use the subsystem's async-compatible lock only when a larger invariant spans awaits | Hold `lock` across `await` or serialize unrelated caller/test work | Stress concurrent operations and assert the exact final value |
| A `TryParse`-style API leaks partial output or throws for ordinary invalid input | Initialize the `out` value to its failure default, parse without exceptions, apply range/culture rules, and assign only on success | Leave stale output on failure or catch broad exceptions as control flow | Test null/empty, malformed, sign, boundaries, overflow, and a valid value |
| Code uses syntax newer than the configured language version | Preserve project policy and rewrite only the unsupported syntax into the nearest equivalent form | Raise the SDK, TFM, package, nullable mode, or `<LangVersion>` to make one file compile | Compile the exact project under its pinned SDK/language version |
| A proposed optimization is justified by shorter source or one noisy timing | Check semantic equivalence first, including overflow, ordering, exceptions, allocation, and side effects; collect repeated Release measurements and stabilize JIT/process effects when results cross over | Recommend from complexity alone, cherry-pick one sample, or alter production code before evidence supports it | Report the measurement method, sample range or representative values, semantic differences, and a decisive adopt/reject conclusion |
| The implementation already satisfies the observable contract | Leave source and project files unchanged and report why no edit is warranted | Modernize, extract, rename, or add low-level machinery during the review | Run the existing focused verifier/build and confirm the working tree remains unchanged |

## Decision Rules

### Compatibility before syntax

- Follow the repository's current target frameworks and language version. Do not change the TFM,
  SDK, `<LangVersion>`, nullable mode, package versions, or analyzer severity unless requested.
- Confirm unfamiliar syntax against the actual compiler. A source read or remembered version table
  is not proof that code compiles in this project.
- In multi-targeted code, account for every target and conditional branch. Prefer APIs available on
  all targets or use the repository's established compatibility pattern.
- Never edit generated output (`*.g.cs`, `*.generated.cs`, or files marked auto-generated). Change
  the generator input or owning source.

### API and nullability contracts

- Preserve public signatures, accessibility, exception behavior, serialization shape, and nullable
  annotations unless the request explicitly authorizes a contract change.
- Use the narrowest accessibility that satisfies existing consumers; do not introduce an interface,
  wrapper, service, or pattern solely for hypothetical reuse or testing.
- Treat `!` as an assertion that needs evidence, not a substitute for flow analysis. Prefer a guard,
  a better type/state model, or an annotation supported by the real invariant.
- For argument validation, match repository conventions and throw a precise exception. Do not return
  success-shaped defaults for invalid states or catch `Exception` merely to continue.

### Async, cancellation, and disposal

- Keep I/O asynchronous end-to-end. Never use `.Result`, `.Wait()`, or `GetAwaiter().GetResult()` in
  an async path unless a fixed synchronous boundary is proven and documented by existing code.
- Accept and propagate `CancellationToken` when the operation can block or loop. Cancellation must
  cancel the underlying work, not only stop awaiting it.
- Await started work or give it explicit, repository-standard ownership and error reporting. Do not
  create accidental fire-and-forget tasks.
- Default to `Task`; use `ValueTask` only when the API shape and measured completion pattern justify
  its consumption constraints.
- Use `using`/`await using` according to the resource's disposal contract. Preserve stream and buffer
  ownership; never return pooled storage after exposing it to a caller.
- Use `ConfigureAwait(false)` only where the repository or library boundary calls for it; do not
  blanket-add or remove it.

### Performance and concurrency

- Optimize only a demonstrated hot path or an explicit allocation/latency requirement. Preserve the
  simple implementation when no measurement justifies complexity.
- Before using `Span<T>`, `Memory<T>`, pooling, `unsafe`, or custom synchronization, identify lifetime,
  aliasing, thread-safety, and ownership constraints and add focused coverage for them.
- Avoid holding locks across `await`. Protect shared state with the synchronization primitive already
  used by the subsystem, and make atomicity requirements explicit.
- Stream large or unbounded data instead of materializing it, but do not return lazy enumeration over
  a resource that has already been disposed.

### Tests

- Use the test framework and naming style already present. Test the observable behavior through the
  normal API; do not widen production visibility solely for a test.
- Add or update tests for the changed behavior, including the edge case that caused the defect.
- Keep tests deterministic and independent. Do not replace a meaningful assertion with a snapshot or
  broad "does not throw" check unless that is the actual contract.

## Workflow

### 1. Classify and reproduce

1. Confirm that the load-bearing issue is C# language/runtime behavior rather than a pure refactor or
   framework/tooling concern.
2. Record the exact compiler diagnostic, failing test, exception, incorrect result, or review contract.
3. If the request is review-only, do not edit; define the concrete correctness and compatibility risks
   to inspect.

### 2. Establish project constraints

1. Read the complete affected types and their callers/callees where behavior depends on them.
2. Inspect the project TFM(s), SDK pin, language version, nullable mode, analyzer rules, package
   management, conditional compilation, and repository build/test instructions.
3. Check nearby code for established error, logging, cancellation, disposal, and testing patterns.
4. For a public or externally observed surface, identify source/binary, serialization, reflection,
   interop, and configuration boundaries before changing it.

### 3. Choose the smallest correct design

1. State the invariant the code must maintain and select the first simple mechanism that satisfies it.
2. Preserve existing contracts unless a change is explicitly requested.
3. Gate advanced mechanisms (`ValueTask`, spans, pooling, `unsafe`, custom synchronization) on a
   concrete need and verifiable lifetime/ownership rules.
4. If the proposed work has become a behavior-preserving structural operation, hand that portion to
   `csharp-refactoring` instead of silently expanding this workflow.

### 4. Implement surgically

1. Change only the owning source and directly coupled tests or documentation.
2. Propagate nullability, cancellation, disposal, and exception behavior through the entire affected
   call path rather than patching only the visible method.
3. Reuse repository helpers and conventions. Avoid speculative abstractions and unrelated cleanup.
4. Let compiler errors reveal missed type or call-site changes; do not hide them with broad casts,
   blanket null-forgiving operators, analyzer suppression, or catch-all handlers.

### 5. Validate in increasing-cost order

1. Run the narrowest compile/build command that covers every changed target framework and conditional
   branch that can be selected in the current environment.
2. Run the focused tests for the changed behavior, then the broader affected test project when practical.
3. Run applicable analyzers/format checks through the repository's own commands.
4. Reproduce the original failure or exercise the requested success path and confirm the observable
   result, not merely a zero exit code.
5. For a claimed performance improvement, compare an existing benchmark or a representative,
   repeatable measurement. If ratios change direction across runs, treat the result as noise and
   stabilize warmup, tiered compilation, process lifetime, or the benchmark harness before deciding.
   Check semantic equivalence separately: a faster candidate that changes overflow, ordering,
   exception, allocation, or side-effect behavior is not a drop-in optimization.

### 6. Report evidence truthfully

Report:

- **Change:** the C# behavior or decision implemented and the files/symbols affected.
- **Validation:** exact commands/checks and their observed results.
- **Compatibility:** any public, nullable, multi-targeting, serialization, ownership, or concurrency
  boundary preserved.
- **Not run / Blocked:** every required check that could not run, with the concrete reason.

If a command fails, report the command and first actionable error. Distinguish a change-caused failure
from a missing SDK/workload/tool, unavailable target platform, restore/network problem, locked file, or
pre-existing repository failure. Fix failures caused by the change and rerun them. Never claim the work
is fully validated after a failed or skipped required check.

## Observable Completion Criteria

A C# change is complete only when all applicable statements are observable:

- The affected project compiles for every relevant target that can be built in the environment, with
  no new compiler or analyzer diagnostics.
- Focused tests pass and exercise the requested behavior or original defect.
- The implementation preserves documented public/nullability/serialization/ownership contracts unless
  the request explicitly changed them.
- Async work is awaited or explicitly owned, cancellation reaches the underlying operation, and
  disposable or pooled resources have an unambiguous lifetime.
- Any performance claim is backed by a repeatable measurement.
- Skipped targets, tests, runtime reproduction, or measurements are labeled **not run**, not implied green.

## Common Pitfalls

| Pitfall | Corrective rule |
|---|---|
| Treating the newest C# syntax as automatically available | Inspect the effective TFM/language version and compile the exact project. |
| Changing TFM, SDK, packages, or nullable mode to make code compile | Preserve project policy unless that change is explicitly requested and owned by the proper workflow. |
| Using `!`, a broad cast, suppression, or `catch (Exception)` to silence evidence | Fix the state/type/error-flow cause or report the unresolved contract. |
| Adding interfaces and patterns by default | Add abstraction only for a current boundary or demonstrated variation. |
| Fixing one async method but dropping cancellation in a callee | Trace and validate the complete affected call path. |
| Timing out only the await while work continues | Cancel and observe the underlying operation. |
| Returning lazy data backed by a disposed stream/context | Materialize within the lifetime or transfer ownership explicitly. |
| Using `ValueTask`, spans, pooling, or `unsafe` without evidence | Prefer the simple safe form until measurement and lifetime rules justify complexity. |
| Holding a lock across `await` | Redesign the critical section or use the subsystem's async-compatible synchronization. |
| Claiming performance from inspection | Run a representative benchmark or report that performance was not measured. |
| Treating a green default build as multi-target proof | Build each relevant TFM/branch explicitly or report what was not run. |
| Performing a rename/extract/modernization as part of a bug fix | Keep behavior work here and route the structural operation to `csharp-refactoring`. |
