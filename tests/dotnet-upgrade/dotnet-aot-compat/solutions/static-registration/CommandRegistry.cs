public static class CommandRegistry
{
    public static IReadOnlyDictionary<string, ICommand> Create()
    {
        ICommand[] commands =
        [
            new AddCommand(),
            new MultiplyCommand(),
        ];

        return commands.ToDictionary(command => command.Name, StringComparer.Ordinal);
    }
}
