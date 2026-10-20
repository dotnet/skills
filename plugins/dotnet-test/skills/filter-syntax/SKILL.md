---
name: filter-syntax
description: "Reference-only filter syntax for VSTest and MTP with MSTest, NUnit, xUnit v3, and TUnit. Load only after the platform/framework is known and a consumer needs to create or translate a filter. Do not load for unfiltered runs or platform detection. Used by run-tests and mtp-hot-reload; never invoke directly."
user-invocable: false
disable-model-invocation: true
license: MIT
---

# Test Filter Syntax Reference

Filter syntax depends on the **platform** and **test framework**.
This reference owns filter expressions, not runner command selection.
Use the `run-tests` decision table to place the filter flags:
a supported VSTest-mode MTP bridge requires `--`, including legacy MTP 1.x
on SDK 10. MTP 2+ cannot bridge on SDK 10+, regardless of separator. Native
MTP normally passes flags directly (an explicit app-argument delimiter is also
allowed). Mode and package compatibility, not SDK version alone, decide the
command. The MTP examples below are argument fragments.

## VSTest filters (MSTest, xUnit v2, NUnit on VSTest)

```bash
dotnet test --filter <EXPRESSION>
```

Expression syntax: `<Property><Operator><Value>[|&<Expression>]`

**Operators:**

| Operator | Meaning |
|----------|---------|
| `=` | Exact match |
| `!=` | Not exact match |
| `~` | Contains |
| `!~` | Does not contain |

**Combinators:** `|` (OR), `&` (AND). Parentheses for grouping: `(A|B)&C`

**Supported properties by framework:**

| Framework | Properties |
|-----------|-----------|
| MSTest | `FullyQualifiedName`, `Name`, `ClassName`, `Priority`, `TestCategory` |
| xUnit | `FullyQualifiedName`, `DisplayName`, `Traits` |
| NUnit | `FullyQualifiedName`, `Name`, `Priority`, `TestCategory` |

An expression without an operator is treated as `FullyQualifiedName~<value>`.

**Examples (VSTest):**

```bash
# Run tests whose name contains "LoginTest"
dotnet test --filter "Name~LoginTest"

# Run a specific test class
dotnet test --filter "ClassName=MyNamespace.MyTestClass"

# Run tests in a category
dotnet test --filter "TestCategory=Integration"

# Exclude a category
dotnet test --filter "TestCategory!=Slow"

# Combine: class AND category
dotnet test --filter "ClassName=MyNamespace.MyTestClass&TestCategory=Unit"

# Either of two classes
dotnet test --filter "ClassName=MyNamespace.ClassA|ClassName=MyNamespace.ClassB"
```

## MTP filters — MSTest and NUnit

MSTest and NUnit on MTP use the **same `--filter` expression syntax** as
VSTest (same properties, operators and combinators):

```bash
--filter "Name~LoginTest"
```

## MTP filters — xUnit (v3)

xUnit v3 on MTP uses **framework-specific filter flags** instead of the generic `--filter` expression:

| Flag | Description |
|------|-------------|
| `--filter-class "name"` | Run all tests in a given class |
| `--filter-not-class "name"` | Exclude all tests in a given class |
| `--filter-method "name"` | Run a specific test method |
| `--filter-not-method "name"` | Exclude a specific test method |
| `--filter-namespace "name"` | Run all tests in a namespace |
| `--filter-not-namespace "name"` | Exclude all tests in a namespace |
| `--filter-trait "name=value"` | Run tests with a matching trait |
| `--filter-not-trait "name=value"` | Exclude tests with a matching trait |

Multiple values can be specified with a single flag: `--filter-class Foo Bar`.

```bash
--filter-class "MyNamespace.LoginTests"

# Combine: namespace + trait
--filter-namespace "MyApp.Tests.Integration" --filter-trait "Category=Smoke"
```

### xUnit v3 query filter language

For complex expressions, use `--filter-query` with a path-segment syntax:

```text
/<assemblyFilter>/<namespaceFilter>/<classFilter>/<methodFilter>[traitName=traitValue]
```

Each segment matches against: assembly name, namespace, class name, method name. Use `*` for "match all" in any segment. Documentation: <https://xunit.net/docs/query-filter-language>

```shell
# xUnit.net v3 MTP — using query language (assembly/namespace/class/method[trait])
--filter-query "/*/*/*IntegrationTests*/*[Category=Smoke]"
```

## MTP filters — TUnit

TUnit uses `--treenode-filter` with a path-based syntax:

```text
--treenode-filter "/<Assembly>/<Namespace>/<ClassName>/<TestName>"
```

Wildcards (`*`) are supported in any segment. Filter operators can be appended to test names for property-based filtering.

| Operator | Meaning |
|----------|---------|
| `*` | Wildcard match |
| `=` | Exact property match (e.g., `[Category=Unit]`) |
| `!=` | Exclude property value |
| `&` | AND (combine conditions) |
| `\|` | OR (within a segment, requires parentheses) |

**Examples (TUnit):**

```bash
# All tests in a class
--treenode-filter "/*/*/LoginTests/*"

# A specific test
--treenode-filter "/*/*/*/AcceptCookiesTest"

# By namespace prefix (wildcard)
--treenode-filter "/*/MyProject.Tests.Api*/*/*"

# By custom property
--treenode-filter "/*/*/*/*[Category=Smoke]"

# Exclude by property
--treenode-filter "/*/*/*/*[Category!=Slow]"

# OR across classes
--treenode-filter "/*/*/(LoginTests)|(SignupTests)/*"

# Combined: namespace + property
--treenode-filter "/*/MyProject.Tests.Integration/*/*/*[Priority=Critical]"
```

## VSTest → MTP filter translation (for migration)

**MSTest, NUnit, and xUnit.net v2 (with `YTest.MTP.XUnit2`)**: The VSTest `--filter` syntax is identical on both VSTest and MTP. No changes needed.

**xUnit.net v3 (native MTP)**: xUnit.net v3 does NOT support the VSTest `--filter` syntax on MTP. Translate filters using xUnit.net v3's native options:

| VSTest `--filter` syntax | xUnit.net v3 MTP equivalent | Notes |
|---|---|---|
| `FullyQualifiedName~ClassName` | `--filter-class *ClassName*` | Wildcards required for substring match |
| `FullyQualifiedName=Ns.Class.Method` | `--filter-method Ns.Class.Method` | Exact match on fully qualified method |
| `Name=MethodName` | `--filter-method *MethodName*` | Wildcards for substring match |
| `Category=Value` (trait) | `--filter-trait "Category=Value"` | Filter by trait name/value pair |
| Complex expressions | `--filter-query "expr"` | Uses xUnit.net query filter language (see above) |
