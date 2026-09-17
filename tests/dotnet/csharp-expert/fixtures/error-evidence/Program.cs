AssertCode("  alpha  ", "ALPHA");
AssertRejected(null);
AssertRejected(42);

Console.WriteLine("error-evidence-ok");

static void AssertCode(object? value, string expected)
{
    if (CodeReader.Read(value) != expected)
    {
        throw new InvalidOperationException("The valid code changed.");
    }
}

static void AssertRejected(object? value)
{
    try
    {
        CodeReader.Read(value);
        throw new InvalidOperationException("Invalid input was accepted.");
    }
    catch (ArgumentException exception) when (exception.ParamName == "value")
    {
    }
}

internal static class CodeReader
{
    public static string Read(object? value)
    {
        return ((string)value!).Trim().ToUpperInvariant();
    }
}
