---
name: dotnet-unittest
description: Provides guidance for generating comprehensive C# unit tests in .NET projects. Use when writing, scaffolding, or improving unit tests for C# code using MSTest, NUnit, or xUnit frameworks. Covers framework detection, test structure, edge-case analysis, mocking rules, and best practices.
---

# .NET Unit Test Generation

Guidance for producing high-quality, comprehensive C# unit tests in .NET projects. This skill covers how to detect the test framework in use (MSTest, NUnit, or xUnit) and how to structure well-formed tests that follow best practices, cover edge cases, and avoid common pitfalls.

## When to Use

- Writing unit tests for new or existing C# code
- Scaffolding a test class for a production class
- Improving test coverage with edge-case and error-condition tests
- Generating parameterized tests for methods with varied inputs

## When Not to Use

- The target file contains only interfaces or delegates (no testable logic)
- Writing integration tests or end-to-end tests (different patterns apply)
- The project is not a .NET / C# project

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Source code to test | Yes | The C# class, method, or file that needs tests |
| Existing test project | Recommended | Helps detect the test framework and coding conventions |

## Workflow

### Step 1: Detect the test framework

Check the project for existing test framework references:

1. Look for NuGet package references in `.csproj` files
2. Scan existing test files for framework-specific attributes
3. Match against known frameworks:
   - **MSTest**: `MSTest` or `Microsoft.VisualStudio.TestTools.UnitTesting` package
   - **NUnit**: `NUnit` package, `NUnit3TestAdapter`
   - **xUnit**: `xunit` package, `xunit.runner.visualstudio`
4. If no framework is detected, ask the user which framework to use

### Step 2: Load framework-specific guidance

Based on the detected framework, load the corresponding reference:

- MSTest → [references/mstest.md](references/mstest.md)
- NUnit → [references/nunit.md](references/nunit.md)
- xUnit → [references/xunit.md](references/xunit.md)

### Step 3: Analyze the source code

Before generating tests, analyze the code thoroughly:

1. Read the source code line by line, understanding what each section does
2. Document all parameters, their purposes, constraints, and valid/invalid value ranges
3. Identify potential edge cases and error conditions based on implementation details
4. Describe the expected behavior of each method under different input conditions
5. Simulate an execution flow to understand how different inputs affect the code path
6. Note any dependencies, interfaces, or external systems that will need to be mocked
7. Consider how concurrency, resource management, or other special conditions might affect behavior
8. Identify any domain-specific validation or business rules that need testing

### Step 4: Generate the test class

Apply the framework-specific conventions from Step 2 and the common guidelines below. Produce a complete, compilable test file.

### Step 5: Validate

- [ ] Test file compiles without errors
- [ ] All using directives are present and sorted alphabetically
- [ ] Test class and methods follow naming conventions
- [ ] Framework-specific attributes are applied correctly
- [ ] Arrange-Act-Assert pattern is clearly followed
- [ ] Edge cases and error conditions are covered
- [ ] No fake/stub/dummy classes were created — only mocking framework is used
- [ ] Code respects the project's C# language version and nullable reference type settings

## Key Testing Goals

- **Minimal but Comprehensive**: Avoid redundant tests. Prefer parameterized tests over duplicate methods.
- **Logical Coverage and Bug Detection**: Focus primarily on meaningful edge cases, domain-specific inputs, boundary values, and scenarios likely to reveal functional bugs or unexpected behavior.
- **Clarity and Best Practices**: Clearly use the Arrange-Act-Assert pattern, proper naming conventions (`Method_Condition_ExpectedResult`), and XML comments explaining test purposes.

## When NOT to Generate Tests

If the provided file or scope only contains interfaces or delegates, do not generate tests.

## Minimizing Hallucinations and Improving Accuracy

- NEVER invent or assume behaviors, methods, constructors, or properties that are not explicitly present in the provided source code.
- If you encounter incomplete or unclear source code or design, explicitly generate a partial test method with explanatory comments guiding users on how to proceed. Prefer this approach over generating incorrect or invalid test code.
- NEVER add additional types other than test classes — no dummy, fake, or stub classes. Use mocking instead. The only exception is a helper class inside the test class or production type overrides for exposing protected members. Even then, never place those outside the test class.

## Test Class Structure

For each public, protected, or internal class in the provided scope:

- Create a corresponding test class named `[ClassName]Tests`
- Place the test class in namespace `[SourceNamespace].UnitTests`
- If the tested source class is partial, the test class must also be partial
- Follow the initialization pattern that matches the detected test framework (see framework references)

## Test Method Conventions

### Naming

- Use meaningful, descriptive, and standardized naming: `MethodName_Condition_ExpectedOutcome`
- Follow the existing test naming conventions found in the project
- Each test method must include a clear XML documentation comment explaining:
  - The test purpose
  - The specific input conditions being tested
  - The expected result or exception

### Structure

- Clearly follow the Arrange-Act-Assert (AAA) pattern
- Keep tests focused on a single behavior
- Avoid testing multiple behaviors in one test method
- Avoid using multiple assertions in one test method — prefer multiple focused tests
- When testing multiple preconditions, write a separate test for each
- When testing multiple outcomes for one precondition, use parameterized tests
- Tests should be able to run in any order or in parallel

## Input and Edge Case Analysis

For **every parameter type**, ensure the following edge cases are tested:

### Numeric Parameters
- `int.MinValue`, `int.MaxValue`, `0`, negative and positive boundary values
- For floating-point: `double.NaN`, `double.PositiveInfinity`, `double.NegativeInfinity`

### String Parameters
- `null` (if nullable), empty strings (`""`), whitespace-only strings
- Very long strings, strings with special/control/invalid characters

### Domain-Specific Parameters
- Identify and test invalid, boundary, and special-case values based on parameter names and context
- Examples: for `"path"` → invalid file paths; for `"age"` → negative, zero, unreasonably large; for `"email"` → invalid formats

### Nullable Parameters
- Explicitly test `null` inputs
- Strictly follow the source code's nullability annotations

### Collections and Arrays
- `null` (if nullable), empty, single-item, duplicates, invalid elements

### Enums
- Test all defined values
- Test values outside the defined range (using casting) if possible

## Exception and Error Condition Testing

For all methods, explicitly test scenarios expected to throw exceptions:

- Invalid arguments, `null` values for non-nullable parameters, out-of-range values, invalid state
- Ensure the correct exception type is validated
- Where possible, validate the exception message
- If the method is not expected to throw, verify that no exception is thrown for valid inputs
- Use framework-specific async exception assertion methods for async code

## Dependency Handling and Mocking

### Strict Prohibition on Custom Implementations

- **STRICT PROHIBITION**: DO NOT create any custom, fake, stub, or dummy classes for dependencies under any circumstances
  - DO NOT create stub classes (e.g., `StubLogger`, `StubRepository`)
  - DO NOT create dummy classes (e.g., `DummyService`, `DummyContext`)
  - DO NOT create fake types (e.g., `FakeDatabase`, `FakeDependency`)
  - DO NOT create test-specific implementations of interfaces or abstract classes
- NEVER create a fake or custom implementation for abstract classes — all major .NET mocking frameworks support mocking abstract classes directly
- NEVER attempt to subclass or override any dependency or framework type for testing
- Any code that is not contained within the test class is strictly forbidden

### Mocking Guidelines

- Assume all external dependencies are provided via dependency injection
- ALWAYS mock dependencies EXCLUSIVELY using the project's mocking framework
- Mock ONLY interfaces and overridable methods
- DO NOT attempt to mock or fake non-overridable types, sealed classes, or methods
- If a dependency cannot be properly mocked, provide a partial test with explanatory comments and mark it as skipped/inconclusive
- When mocking an API that uses optional parameters with Moq, specify all parameters explicitly in the mock setup
- External dependencies can be mocked — never mock code whose implementation is part of the solution under test

### FluentAssertions / AwesomeAssertions

If FluentAssertions or AwesomeAssertions libraries are already in use in the project, prefer them for assertions:

- `result.Should().Be(expected)`
- `collection.Should().Contain(item)`
- `action.Should().Throw<InvalidOperationException>()`

## C# Version and Language Features

Strictly respect the language version and features in use by the project:

### Nullable Reference Types

- If the project uses nullable reference types: use nullable annotations (`Type?`), null-conditional (`?.`), and null-coalescing (`??`) operators appropriately. Ensure mock return values match nullability.
- If the project does NOT use nullable reference types: do not use nullable annotations.

### Language Version Constraints

- Use only language features available in the project's C# version
- Check the target framework and any explicit `<LangVersion>` in project files
- C# 12+ (.NET 8+): primary constructors, collection expressions
- C# 11+ (.NET 7+): raw string literals, generic attributes, required members
- C# 10+ (.NET 6+): record structs, file-scoped namespaces, global usings
- C# 9+ (.NET 5+): records, init-only setters, pattern matching enhancements
- C# 8+ (.NET Core 3+): nullable reference types, async streams, ranges

## Code Quality and Conventions

The generated C# test file MUST:

- Be fully self-contained and compilable without requiring manual edits
- Include all necessary using directives, sorted alphabetically
- Follow the code style demonstrated by existing tests in the project
- Use descriptive variable and parameter names consistently
- Clearly separate Arrange, Act, and Assert sections for readability
- Respect preprocessor directives found in the source code (e.g., `#if NETSTANDARD`)
- DO NOT use reflection to access private or inaccessible members — test through the public interface
- Place helper or utility classes as inner classes within the test class

## Testing Async Code

- Use async test methods when testing async code (return type `async Task`)
- Use `await` when calling async methods under test
- Use framework-specific async assertion methods for exception testing
- Test cancellation scenarios using `CancellationToken`
- Test timeout scenarios appropriately

## Additional Test Utilities

When additional test utility packages are available in the project, utilize them:

- **AutoFixture**: generate test data and reduce boilerplate
- **Bogus**: generate realistic fake data for complex object graphs
- **Verify**: snapshot testing for complex objects
- **Aspire Test Helpers**: for Aspire-based applications
- **Microsoft.AspNetCore.TestHost**: for middleware isolation testing
- **Grpc.Core.Testing**: for gRPC service and client testing
- **Microsoft.Extensions.\* Testing Fakes**: fake logging, configuration, etc.

## Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| Creating fake/stub/dummy classes | Use the project's mocking framework exclusively |
| Inventing methods or properties not in source | Only test what is explicitly present in the code |
| Using features from a newer C# version | Check the project's target framework and `LangVersion` |
| Testing private members via reflection | Test through the public API surface |
| Multiple unrelated assertions in one test | Split into focused, single-behavior tests |
| Ignoring nullability annotations | Respect the project's nullable reference type settings |
| Hardcoding framework choice | Always detect from the project's existing dependencies |
