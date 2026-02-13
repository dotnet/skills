# xUnit Framework Reference

This reference contains xUnit-specific conventions for the [dotnet-unittest](../SKILL.md) skill. Apply these conventions when the project uses the xUnit testing framework.

## Package References

- Microsoft.NET.Test.Sdk
- xunit
- xunit.runner.visualstudio

## Test Class Structure

- No class-level attributes required
- Use `[Fact]` attribute for simple tests
- Use `[Theory]` combined with multiple `[InlineData]` attributes for parameterized tests
- Use constructor for setup and `IDisposable.Dispose()` for teardown
- Use `ITestOutputHelper` (obtained via constructor parameter) for test diagnostics

### Initialization

- Prefer constructor initialization with readonly fields for dependencies and test subjects
- Implement `IDisposable` for cleanup when resources need to be disposed

## Parameterized Tests

- Use `[Theory]` with `[InlineData]` for inline test data
- Use `[MemberData]` for programmatically generated test data
- Prefer parameterized tests using `[Theory]` and `[InlineData]` over multiple similar test methods
- Combine logically related test cases into a single parameterized test method
- Use `[MemberData]` for complex or programmatically generated test data

## Assertions

- Use `Assert.Equal(expected, actual)` for value equality
- Use `Assert.Same(expected, actual)` for reference equality
- Use `Assert.True(condition)` / `Assert.False(condition)` for boolean conditions
- Use `Assert.Null(value)` / `Assert.NotNull(value)` for null checks
- Use `Assert.Contains(item, collection)` / `Assert.DoesNotContain(item, collection)` for collections
- Use `Assert.Matches(pattern, actual)` / `Assert.DoesNotMatch(pattern, actual)` for regex
- Use `Assert.Throws<TException>(() => method())` to test exceptions
- Use `await Assert.ThrowsAsync<TException>(async () => await method())` for async exception testing

## Setup and Teardown

- Use constructor for setup
- Implement `IDisposable.Dispose()` for teardown/cleanup
- Avoid shared state between tests

## Skipping Tests

- Use `[Fact(Skip = "Reason")]` or `[Theory(Skip = "Reason")]` to skip tests

## Sample Test Class

```csharp
using Moq;
using Xunit;

namespace MyApp.Services.UnitTests
{
    /// <summary>
    /// Tests for the Calculator class.
    /// </summary>
    public partial class CalculatorTests : IDisposable
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
        [Fact]
        public void Add_WithPositiveNumbers_ReturnsCorrectSum()
        {
            // Arrange
            int firstNumber = 3;
            int secondNumber = 5;
            int expected = 8;

            // Act
            int actual = _calculator.Add(firstNumber, secondNumber);

            // Assert
            Assert.Equal(expected, actual);
        }

        /// <summary>
        /// Tests that Add handles various edge cases correctly using parameterized inputs.
        /// </summary>
        [Theory]
        [InlineData(0, 0, 0)] // Zero plus zero
        [InlineData(int.MaxValue, 0, int.MaxValue)] // Max value plus zero
        [InlineData(-5, 5, 0)] // Negative and positive cancel out
        [InlineData(100, -100, 0)] // Large positive and negative
        public void Add_WithEdgeCases_HandlesCorrectly(int first, int second, int expected)
        {
            // Arrange & Act
            int actual = _calculator.Add(first, second);

            // Assert
            Assert.Equal(expected, actual);
        }

        /// <summary>
        /// Tests that Divide throws ArgumentException when divisor is zero.
        /// Input: divisor = 0
        /// Expected: ArgumentException with specific message
        /// </summary>
        [Fact]
        public void Divide_WithZeroDivisor_ThrowsArgumentException()
        {
            // Arrange
            int dividend = 10;
            int divisor = 0;

            // Act & Assert
            ArgumentException exception = Assert.Throws<ArgumentException>(() =>
                _calculator.Divide(dividend, divisor));

            Assert.Equal("Divisor cannot be zero. (Parameter 'divisor')", exception.Message);
        }

        /// <summary>
        /// Tests that Calculate logs the operation when logging is enabled.
        /// </summary>
        [Fact]
        public void Calculate_WhenLoggingEnabled_LogsOperation()
        {
            // Arrange
            _mockLogger.Setup(x => x.IsEnabled).Returns(true);

            // Act
            _calculator.Add(2, 3);

            // Assert
            _mockLogger.Verify(x => x.Log(It.IsAny<string>()), Times.Once);
        }

        /// <summary>
        /// Cleanup resources.
        /// </summary>
        public void Dispose()
        {
            // Cleanup if needed
        }
    }
}
```
