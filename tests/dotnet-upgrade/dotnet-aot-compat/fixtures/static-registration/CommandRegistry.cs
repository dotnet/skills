using System.Reflection;

public static class CommandRegistry
{
    public static IReadOnlyDictionary<string, ICommand> Create()
        => typeof(CommandRegistry).Assembly
            .GetTypes()
            .Where(type => !type.IsAbstract && typeof(ICommand).IsAssignableFrom(type))
            .Select(type => (ICommand)Activator.CreateInstance(type)!)
            .ToDictionary(command => command.Name, StringComparer.Ordinal);
}
