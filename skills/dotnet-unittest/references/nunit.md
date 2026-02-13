# NUnit Framework Reference

This reference contains NUnit-specific conventions for the [dotnet-unittest](../SKILL.md) skill. Apply these conventions when the project uses the NUnit testing framework.

## Package References

- Microsoft.NET.Test.Sdk
- NUnit
- NUnit3TestAdapter

## Test Class Structure

- Apply `[TestFixture]` attribute to test classes
- Use `[Test]` attribute for test methods
- Use `[SetUp]` and `[TearDown]` for per-test setup and teardown
- Use `[OneTimeSetUp]` and `[OneTimeTearDown]` for per-class setup and teardown
- Use `[SetUpFixture]` for assembly-level setup and teardown

### Initialization

- Prefer using `[SetUp]` for per-test initialization of dependencies and test subjects
- Use `[OneTimeSetUp]` for expensive setup that can be shared across tests

## Parameterized Tests

- Use `[TestCase]` for inline test data
- Use `[TestCaseSource]` for programmatically generated test data
- Use `[Values]` for simple parameter combinations
- Use `[ValueSource]` for property or method-based data sources
- Use `[Random]` for random numeric test values
- Use `[Range]` for sequential numeric test values
- Use `[Combinatorial]` or `[Pairwise]` for combining multiple parameters
- Prefer parameterized tests using `[TestCase]` over multiple similar test methods
- Combine logically related test cases into a single parameterized test method

## Assertions

Prefer the constraint-based model (`Assert.That`) for better readability:

- `Assert.That(actual, Is.EqualTo(expected))` for value equality
- `Assert.That(actual, Is.Null)` / `Assert.That(actual, Is.Not.Null)` for null checks
- `Assert.That(actual, Is.SameAs(expected))` for reference equality
- `Assert.That(actual, Is.GreaterThan(value))` / `Is.LessThan(value)` for comparisons
- `Assert.That(collection, Contains.Item(value))` for collection assertions
- `Assert.That(actual, Is.True)` / `Assert.That(actual, Is.False)` for boolean conditions
- `Assert.Throws<TException>(() => method())` to test exceptions
- `Assert.ThrowsAsync<TException>(async () => await method())` for async exception testing
- `Assert.That(() => method(), Throws.TypeOf<TException>())` for constraint-based exception syntax

Classic-style alternatives (less preferred):
- `Assert.AreEqual(expected, actual)` for simple value equality
- `CollectionAssert` for collection comparisons
- `StringAssert` for string-specific assertions

## Setup and Teardown

- Use `[SetUp]` for per-test setup when needed
- Use `[TearDown]` for per-test cleanup
- Use `[OneTimeSetUp]` for one-time class-level setup
- Use `[OneTimeTearDown]` for one-time class-level cleanup
- Avoid shared state between tests

## Skipping Tests

- Use `[Ignore("Reason")]` to temporarily skip tests or mark inconclusive tests

## Sample Test Class

```csharp
using Moq;
using NUnit.Framework;

namespace MyApp.Services.UnitTests
{
    /// <summary>
    /// Tests for the Calculator class.
    /// </summary>
    [TestFixture]
    public partial class CalculatorTests
    {
        private Mock<ILogger> _mockLogger;
        private Calculator _calculator;

        /// <summary>
        /// Sets up test dependencies before each test.
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            _mockLogger = new Mock<ILogger>();
            _calculator = new Calculator(_mockLogger.Object);
        }

        /// <summary>
        /// Tests that Add returns the correct sum when called with two positive numbers.
        /// Input: 3 and 5
        /// Expected: 8
        /// </summary>
        [Test]
        public void Add_WithPositiveNumbers_ReturnsCorrectSum()
        {
            // Arrange
            int firstNumber = 3;
            int secondNumber = 5;
            int expected = 8;

            // Act
            int actual = _calculator.Add(firstNumber, secondNumber);

            // Assert
            Assert.That(actual, Is.EqualTo(expected));
        }

        /// <summary>
        /// Tests that Add handles various edge cases correctly using parameterized inputs.
        /// </summary>
        [TestCase(0, 0, 0, TestName = "Zero plus zero")]
        [TestCase(int.MaxValue, 0, int.MaxValue, TestName = "Max value plus zero")]
        [TestCase(-5, 5, 0, TestName = "Negative and positive cancel out")]
        [TestCase(100, -100, 0, TestName = "Large positive and negative")]
        public void Add_WithEdgeCases_HandlesCorrectly(int first, int second, int expected)
        {
            // Arrange & Act
            int actual = _calculator.Add(first, second);

            // Assert
            Assert.That(actual, Is.EqualTo(expected));
        }

        /// <summary>
        /// Tests that Divide throws ArgumentException when divisor is zero.
        /// Input: divisor = 0
        /// Expected: ArgumentException with specific message
        /// </summary>
        [Test]
        public void Divide_WithZeroDivisor_ThrowsArgumentException()
        {
            // Arrange
            int dividend = 10;
            int divisor = 0;

            // Act & Assert
            ArgumentException exception = Assert.Throws<ArgumentException>(() =>
                _calculator.Divide(dividend, divisor));

            Assert.That(exception.Message, Is.EqualTo("Divisor cannot be zero. (Parameter 'divisor')"));
        }

        /// <summary>
        /// Tests that Calculate logs the operation when logging is enabled.
        /// </summary>
        [Test]
        public void Calculate_WhenLoggingEnabled_LogsOperation()
        {
            // Arrange
            _mockLogger.Setup(x => x.IsEnabled).Returns(true);

            // Act
            _calculator.Add(2, 3);

            // Assert
            _mockLogger.Verify(x => x.Log(It.IsAny<string>()), Times.Once);
        }
    }
}
```
