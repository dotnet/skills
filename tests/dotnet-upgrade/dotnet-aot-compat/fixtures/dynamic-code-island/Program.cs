var formatter = FormatterFactory.Create();

if (formatter.Format(42) != "value:42")
{
    throw new InvalidOperationException("Formatting behavior changed.");
}

Console.WriteLine("PASS");
