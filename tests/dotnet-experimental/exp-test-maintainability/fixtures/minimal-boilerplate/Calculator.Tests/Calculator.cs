namespace Calculator.Tests;

public sealed class Calculator
{
    public int Add(int left, int right) => left + right;

    public int Subtract(int left, int right) => left - right;

    public double Divide(double dividend, double divisor) =>
        divisor == 0
            ? throw new DivideByZeroException()
            : dividend / divisor;
}

public sealed class ScientificCalculator
{
    public double SquareRoot(double value) =>
        value < 0
            ? throw new ArgumentException("Value must be non-negative.", nameof(value))
            : Math.Sqrt(value);

    public double Power(double value, double exponent) => Math.Pow(value, exponent);

    public double Log(double value, double newBase) =>
        value <= 0
            ? throw new ArgumentException("Value must be positive.", nameof(value))
            : Math.Log(value, newBase);
}
