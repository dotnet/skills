# Evaluation Test Cases for `diagnosing-dotnet-aot`

These tests validate that the skill provides value **beyond what an unskilled LLM already knows**. Each test targets a specific knowledge gap where base models consistently give wrong or incomplete answers.

## How to Run

For each test case:
1. Start a fresh Claude session with the `diagnosing-dotnet-aot` skill loaded
2. Use the prompt provided
3. Evaluate the response against the success criteria
4. Mark pass/fail for each criterion

## Test 1: `#pragma warning disable` Doesn't Work for Trim Warnings

**Asset:** [assets/pragma-warning-suppress.cs](assets/pragma-warning-suppress.cs)

**Prompt:**
> Review this code for AOT compatibility. The developer has suppressed the AOT warnings — is this correct?

**Success Criteria:**
- [ ] Identifies that `#pragma warning disable` is **not preserved in IL** and the trimmer/ILC ignores it
- [ ] Recommends `[UnconditionalSuppressMessage]` as the correct suppression mechanism
- [ ] Notes that even with proper suppression, the underlying code (`Type.GetType(string)`, `MakeGenericType`) is still incompatible — suppression is just silencing the warning, not fixing the problem
- [ ] Suggests redesigning to eliminate reflection (e.g., static factory pattern) rather than just suppressing

**Why This Test Matters:** Base LLMs frequently suggest `#pragma warning disable` for trim warnings, treating them like any other C# warning. This is one of the most common mistakes developers make.

---

## Test 2: MakeGenericType — Reference Types vs Value Types

**Asset:** [assets/make-generic-type-mixed.cs](assets/make-generic-type-mixed.cs)

**Prompt:**
> Analyze this code for Native AOT compatibility. Which MakeGenericType calls are safe and which are dangerous?

**Success Criteria:**
- [ ] Correctly identifies `CreateRepository` as **safe** because `entityType` is always a reference type (classes share canonical code)
- [ ] Correctly identifies `CreateAggregator` as **unsafe** because `numericType` could be a value type (int, float, double each need dedicated compiled code)
- [ ] Correctly identifies `CreateHandler<T>` as **safe** because `T` is constrained to `class`
- [ ] Explains the fundamental AOT rule: reference types share one code path; value types each need their own
- [ ] Does NOT blanket-flag all three methods as dangerous

**Why This Test Matters:** Base LLMs tend to flag ALL `MakeGenericType` calls as AOT-incompatible. The ref/value type distinction is nuanced and critical — over-flagging creates false positives that erode developer trust.

---

## Test 3: Expression.Compile() Silent Performance Cliff

**Asset:** [assets/expression-compile-hotpath.cs](assets/expression-compile-hotpath.cs)

**Prompt:**
> Check this code for AOT issues. Pay attention to both the PropertyAccessorCache and the OrderRepository.

**Success Criteria:**
- [ ] Flags `lambda.Compile()` in `PropertyAccessorCache.RegisterType` as a **critical issue** — falls back to 10-100x slower interpreter in AOT
- [ ] Notes that **no IL warning is emitted** for this — it's a silent performance degradation
- [ ] Identifies that `GetValue` is called on a hot path, making the perf impact severe
- [ ] Correctly identifies the EF Core LINQ query in `OrderRepository` as **safe** — expression trees are translated to SQL, never compiled to delegates
- [ ] Suggests a concrete alternative (e.g., direct delegate, source generator, or reflection with caching)

**Why This Test Matters:** This is invisible to the warning system. Base LLMs either miss it entirely or incorrectly flag EF Core LINQ expressions as problematic too.

---

## Test 4: EventSource False Positive

**Asset:** [assets/eventsource-false-positive.cs](assets/eventsource-false-positive.cs)

**Prompt:**
> I'm getting IL2026 warnings on my EventSource methods. How do I fix them for AOT?

**Success Criteria:**
- [ ] Identifies that `WriteEvent` with >3 parameters triggers IL2026 as a **known false positive**
- [ ] Confirms that passing **only primitive types** (string, int, long) to `WriteEvent` is safe in AOT
- [ ] Recommends `[UnconditionalSuppressMessage]` (not `#pragma`) with a justification mentioning primitive types
- [ ] Does NOT suggest rewriting the EventSource methods or removing parameters

**Why This Test Matters:** Base LLMs treat all IL2026 warnings as real problems and suggest invasive refactoring. Knowing when to suppress is expert-level knowledge.

---

## Test 5: Incomplete Annotation Propagation Chain

**Asset:** [assets/annotation-chain-incomplete.cs](assets/annotation-chain-incomplete.cs)

**Prompt:**
> The `CreateInstance` method has the right annotation but I'm still getting warnings. Why?

**Success Criteria:**
- [ ] Traces the call chain: `Resolve` → `CreateFromConfig` → `CreateInstance`
- [ ] Identifies that `CreateFromConfig` is missing `[DynamicallyAccessedMembers]` on its `serviceType` parameter
- [ ] Identifies that `CreateService<T>` is missing `[DynamicallyAccessedMembers]` on its type parameter `T`
- [ ] Identifies that `Resolve` also needs annotation (or `[RequiresUnreferencedCode]`) since it's the entry point
- [ ] Explains the propagation rule: annotations must flow from the reflection call site **all the way back** to the public API boundary
- [ ] Shows the correctly annotated chain (concrete code fix for each method)

**Why This Test Matters:** Base LLMs understand `[DynamicallyAccessedMembers]` exists but rarely walk through multi-hop propagation correctly. They often annotate only the immediate caller.

---

## Test 6: IsAotCompatible Property Cascade

**Asset:** [assets/isaotcompatible-cascade.cs](assets/isaotcompatible-cascade.cs)

**Prompt:**
> Review my library's project file for AOT setup. Am I missing anything?

**Success Criteria:**
- [ ] Identifies that `IsTrimmable`, `EnableTrimAnalyzer`, `EnableSingleFileAnalyzer`, `EnableAotAnalyzer` are **redundant** because `IsAotCompatible=true` automatically enables all four
- [ ] Recommends removing the redundant properties to reduce confusion
- [ ] Identifies that `TrimmerSingleWarn` should be set to `false` to see individual warnings instead of one per assembly
- [ ] Does NOT say the redundant properties are wrong or harmful — they're just unnecessary

**Why This Test Matters:** Base LLMs list these properties independently in their AOT setup advice without knowing the cascade relationship. This leads to cargo-cult project files.

---

## Scoring

| Test | Target Behavior | Pass Threshold |
|------|----------------|----------------|
| 1. #pragma suppress | Catch the #pragma mistake | All 4 criteria |
| 2. MakeGenericType | Distinguish ref vs value types | All 5 criteria |
| 3. Expression.Compile | Flag silent perf cliff, spare EF Core | All 5 criteria |
| 4. EventSource | Recognize false positive | All 4 criteria |
| 5. Annotation chain | Walk full propagation chain | 5 of 6 criteria |
| 6. IsAotCompatible | Know the cascade | 3 of 4 criteria |

**Overall pass:** 5 of 6 tests pass at threshold.
