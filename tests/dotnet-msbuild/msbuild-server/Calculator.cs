namespace SlowCliBuilds;

public class Calculator
{
    public int Add(int a, int b) => a + b;
    public int Multiply(int a, int b) => a * b;
    public double Divide(int a, int b) => b == 0 ? throw new DivideByZeroException() : (double)a / b;

    public static void Main() => Console.WriteLine(new Calculator().Add(1, 2));
}
