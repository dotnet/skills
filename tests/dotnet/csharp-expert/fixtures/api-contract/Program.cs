AssertValid("1", 1);
AssertValid("65535", 65535);
AssertInvalid(null);
AssertInvalid("");
AssertInvalid("0");
AssertInvalid("-1");
AssertInvalid("65536");
AssertInvalid("http");
Console.WriteLine("api-ok");

static void AssertValid(string text, int expected)
{
    if (!PortNumber.TryParse(text, out var actual) || actual != expected)
    {
        throw new InvalidOperationException($"Expected valid port {expected}.");
    }
}

static void AssertInvalid(string? text)
{
    if (PortNumber.TryParse(text, out var actual) || actual != 0)
    {
        throw new InvalidOperationException($"Expected '{text}' to be rejected with a zero value.");
    }
}
