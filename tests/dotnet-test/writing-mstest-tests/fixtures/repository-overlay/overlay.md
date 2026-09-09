---
core: dotnet-test/writing-mstest-tests
binding-revision: "1"
mode: extend
---

# Repository MSTest conventions

- Name tests `Should_<ExpectedBehavior>_When_<Condition>`.
- Mark tests handled by this component with `[TestCategory("Fast")]`.
- Do not seal test classes; this repository generates partial derived fixtures.
