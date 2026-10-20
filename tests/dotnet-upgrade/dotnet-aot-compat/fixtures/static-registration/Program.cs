var commands = CommandRegistry.Create();

if (commands.Count != 2 ||
    commands["add"].Execute() != 7 ||
    commands["multiply"].Execute() != 12)
{
    throw new InvalidOperationException("Registration behavior changed.");
}

Console.WriteLine("PASS");
