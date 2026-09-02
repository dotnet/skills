namespace Calculator.Tests;

public sealed class ScientificCalculator
{
    private readonly List<string> _history = [];

    public int Add(int left, int right)
    {
        _history.Add($"Add({left}, {right})");
        return left + right;
    }

    public double Divide(double dividend, double divisor)
    {
        if (divisor == 0)
        {
            throw new DivideByZeroException();
        }

        _history.Add($"Divide({dividend}, {divisor})");
        return dividend / divisor;
    }

    public double SquareRoot(double value)
    {
        if (value < 0)
        {
            throw new ArgumentException("Value must be non-negative.", nameof(value));
        }

        _history.Add($"SquareRoot({value})");
        return Math.Sqrt(value);
    }

    public double Exp(double value) => Math.Exp(value);

    public IReadOnlyList<string> GetHistory() => _history;

    public void ClearHistory() => _history.Clear();
}
