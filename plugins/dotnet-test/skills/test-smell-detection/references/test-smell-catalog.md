# Test Smell Catalog

Extended catalog of test smells based on academic research. This reference provides deeper background on smells beyond those covered in the core skill, including research origins, prevalence data, and real-world examples from open-source projects.

Source: [testsmells.org](https://testsmells.org/) — a research project from the Rochester Institute of Technology.

## Full Smell Taxonomy

The academic literature identifies 19 distinct test smell types. The core skill covers the 10 most impactful ones. This catalog documents all 19 for deeper analysis when requested.

### Smells Covered by the Core Skill

| Smell | Core Skill | Academic Name |
| ----- | ---------- | ------------- |
| Conditional Test Logic | Smell 1 | Conditional Test Logic |
| Mystery Guest | Smell 2 | Mystery Guest |
| Sleepy Test | Smell 3 | Sleepy Test |
| Assertion-Free Test | Smell 4 | Unknown Test / Empty Test |
| Eager Test | Smell 5 | Eager Test |
| Magic Number Test | Smell 6 | Magic Number Test |
| Sensitive Equality | Smell 7 | Sensitive Equality |
| Exception Handling | Smell 8 | Exception Handling |
| General Fixture | Smell 9 | General Fixture |
| Ignored/Disabled Test | Smell 10 | Ignored Test |

### Additional Smells (Extended Analysis)

These smells are not in the core skill but can be reported when the user requests a thorough audit or when they are particularly prevalent.

#### Assertion Roulette

A test method has multiple assertions without descriptive messages. When one fails, it's unclear which assertion caused the failure and why.

**Detection:** A test method containing 3+ assertion statements where none provide an explanation message parameter.

**Example:**

```java
assertThat(repo, hasGitObject("ba1f63e4430bff267d112b1e8afc1d6294db0ccc"));
File readmeFile = new File(repo.getWorkTree(), "README");
assertThat(readmeFile, exists());
assertThat(readmeFile, ofLength(12));
```

Three assertions, no messages — if one fails, you must read the code to determine which property was wrong.

#### Duplicate Assert

The same assertion (same parameters) appears multiple times in a single test method.

**Detection:** Two or more assertion statements within the same test method with identical parameters.

**Example:**

```java
valid = XmlSanitizer.isValid("Fritz-box");
assertEquals("Minus is valid", true, valid);
// ... later in the same method:
valid = XmlSanitizer.isValid("Fritz-box");
assertEquals("Minus is valid", true, valid);
```

#### Lazy Test

Multiple test methods test the same production method. While not always a problem, it may indicate tests that should be parameterized or that explore the same behavior redundantly.

**Detection:** Multiple test methods in the same class calling the same production method as their primary action.

#### Constructor Initialization

Test class uses a constructor instead of the framework's setup method to initialize fields. This bypasses framework lifecycle hooks and can cause issues with test isolation.

**Detection:** Test class has a constructor that initializes fields rather than using the designated setup method.

#### Default Test

The test class retains its auto-generated template name (e.g., `ExampleUnitTest`, `UnitTest1`). This indicates the test file was scaffolded but never properly organized.

**Detection:** Test class named `ExampleUnitTest`, `ExampleInstrumentedTest`, `UnitTest1`, `TestClass1`, or similar template names.

#### Redundant Print

Test methods contain `Console.WriteLine`, `System.out.println`, `print()`, or similar output statements. These are debugging artifacts that add noise and slow execution.

**Detection:** Print/log statements inside test methods that are not part of a logging-focused test.

#### Redundant Assertion

Assertions that are always true or always false regardless of the code under test.

**Detection:** Assertions comparing a value to itself, or asserting literal `true`/`false` constants.

**Example:**

```java
assertEquals(true, true);
```

#### Resource Optimism

Tests that assume external resources (files, services) exist without checking. The test may pass locally but fail in CI or on another developer's machine.

**Detection:** File or resource references used without existence checks or guard assertions.

#### Empty Test

A test method that contains no executable statements — only comments or whitespace. Similar to Assertion-Free Test but even more extreme: no code runs at all.

**Detection:** Test method body contains only comments, whitespace, or commented-out code.

## Research Background

### Prevalence

Research on Android open-source projects (Peruma et al., CASCON 2019) found:

- **Assertion Roulette** and **Eager Test** are the most common smells
- Over 50% of test files contain at least one smell
- Smells tend to accumulate over time — they are rarely refactored away

### Impact on Flakiness

Camara et al. (SAST 2021) found a correlation between test smells and flaky tests — smelly tests are more likely to exhibit non-deterministic failures.

### Severity Thresholds

Spadini et al. (MSR 2020) investigated severity thresholds for test smells, finding that developer perception of smell severity varies significantly. Some smells (Conditional Test Logic, Sleepy Test) are consistently rated as serious, while others (Magic Number, Assertion Roulette) are considered minor annoyances.

## Key Publications

- Peruma et al. (2020). "tsDetect: An Open Source Test Smells Detection Tool." ESEC/FSE 2020.
- Peruma et al. (2019). "On the Distribution of Test Smells in Open Source Android Applications." CASCON 2019.
- Spadini et al. (2020). "Investigating Severity Thresholds for Test Smells." MSR 2020.
- Camara et al. (2021). "On the use of test smells for prediction of flaky tests." SAST 2021.
- Kim et al. (2021). "The secret life of test smells — an empirical study on test smell evolution and maintenance." Empirical Software Engineering, 26(100).
