# MSTest Framework Reference

This reference contains MSTest-specific conventions for the [dotnet-unittest](../SKILL.md) skill. Apply these conventions when the project uses the MSTest testing framework.

## Package References

- MSTest (includes `Microsoft.VisualStudio.TestTools.UnitTesting`)

## Test Class Structure

- Use `[TestClass]` attribute for test classes
- Use `[TestMethod]` attribute for test methods
- Use `[TestInitialize]` and `[TestCleanup]` for per-test setup and teardown
- Use `[ClassInitialize]` and `[ClassCleanup]` for per-class setup and teardown
- Use `[AssemblyInitialize]` and `[AssemblyCleanup]` for assembly-level setup and teardown
- `[ClassInitialize]` methods must have signature: `public static void MethodName(TestContext context)`

### Initialization

- Prefer constructor initialization with readonly fields for dependencies and test subjects
- Use `[TestInitialize]` only when per-test setup logic is required or constructor initialization is not feasible

## Parameterized Tests

- Use `[TestMethod]` combined with data source attributes
- Use `[DataRow]` for inline test data (can have multiple `[DataRow]` attributes on one method)
- Use `[DynamicData]` for programmatically generated test data
- Use `[DataTestMethod]` as an alternative to `[TestMethod]` for data-driven tests
- Use `[TestProperty]` to add metadata to tests
- Use meaningful parameter names in data-driven tests
- Prefer parameterized tests using `[DataRow]` over multiple similar test methods
- Combine logically related test cases into a single parameterized test method

## Assertions

- Use `Assert.AreEqual(expected, actual)` for value equality (expected value first, then actual)
- Use `Assert.AreSame(expected, actual)` for reference equality
- Use `Assert.IsTrue(condition)` / `Assert.IsFalse(condition)` for boolean conditions
- Use `Assert.IsNull(value)` / `Assert.IsNotNull(value)` for null checks
- Use `CollectionAssert.Contains(collection, item)` for collection assertions
- Use `CollectionAssert.AreEqual(expected, actual)` for collection equality
- Use `StringAssert.Contains(value, substring)` for string-specific assertions
- Use `Assert.ThrowsException<TException>(() => method())` to test exceptions
- Use `await Assert.ThrowsExceptionAsync<TException>(async () => await method())` for async exception testing

## Setup and Teardown

- Use `[TestInitialize]` for per-test setup when needed
- Use `[TestCleanup]` for per-test cleanup
- Use `[ClassInitialize]` for one-time class-level setup (must be static with `TestContext` parameter)
- Use `[ClassCleanup]` for one-time class-level cleanup (must be static)
- Avoid shared state between tests

## Skipping Tests

- Use `[Ignore("Reason")]` to mark partial or inconclusive tests

## Sample Test Class

```csharp
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace MyApp.Services.UnitTests
{
    /// <summary>
    /// Tests for the Calculator class.
    /// </summary>
    [TestClass]
    public partial class CalculatorTests
    {
        private readonly Mock<ILogger> _mockLogger;
        private readonly Calculator _calculator;

        /// <summary>
        /// Initializes a new instance of the test class.
        /// </summary>
        public CalculatorTests()
        {
            _mockLogger = new Mock<ILogger>();
            _calculator = new Calculator(_mockLogger.Object);
        }

        /// <summary>
        /// Tests that Add returns the correct sum when called with two positive numbers.
        /// Input: 3 and 5
        /// Expected: 8
        /// </summary>
        [TestMethod]
        public void Add_WithPositiveNumbers_ReturnsCorrectSum()
        {
            // Arrange
            int firstNumber = 3;
            int secondNumber = 5;
            int expected = 8;

            // Act
            int actual = _calculator.Add(firstNumber, secondNumber);

            // Assert
            Assert.AreEqual(expected, actual);
        }

        /// <summary>
        /// Tests that Add handles various edge cases correctly using parameterized inputs.
        /// </summary>
        [DataTestMethod]
        [DataRow(0, 0, 0, DisplayName = "Zero plus zero")]
        [DataRow(int.MaxValue, 0, int.MaxValue, DisplayName = "Max value plus zero")]
        [DataRow(-5, 5, 0, DisplayName = "Negative and positive cancel out")]
        [DataRow(100, -100, 0, DisplayName = "Large positive and negative")]
        public void Add_WithEdgeCases_HandlesCorrectly(int first, int second, int expected)
        {
            // Arrange & Act
            int actual = _calculator.Add(first, second);

            // Assert
            Assert.AreEqual(expected, actual);
        }

        /// <summary>
        /// Tests that Divide throws ArgumentException when divisor is zero.
        /// Input: divisor = 0
        /// Expected: ArgumentException with specific message
        /// </summary>
        [TestMethod]
        public void Divide_WithZeroDivisor_ThrowsArgumentException()
        {
            // Arrange
            int dividend = 10;
            int divisor = 0;

            // Act & Assert
            ArgumentException exception = Assert.ThrowsException<ArgumentException>(() =>
                _calculator.Divide(dividend, divisor));

            Assert.AreEqual("Divisor cannot be zero. (Parameter 'divisor')", exception.Message);
        }

        /// <summary>
        /// Tests that Calculate logs the operation when logging is enabled.
        /// </summary>
        [TestMethod]
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
