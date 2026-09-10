namespace NeedsWrappers.Services;

public sealed class ConsolePrompter
{
    public string ReadName()
    {
        Console.Write("Name: ");
        return Console.ReadLine() ?? string.Empty;
    }
}
