namespace BlazorComponentReadiness.Validator.Cli;

internal sealed class CommandOptions
{
    private readonly Dictionary<string, List<string>> _values = new(StringComparer.Ordinal);

    public static CommandOptions Parse(IReadOnlyList<string> args, params string[] allowed)
    {
        var allowedSet = allowed.ToHashSet(StringComparer.Ordinal);
        var result = new CommandOptions();
        for (var index = 0; index < args.Count; index += 2)
        {
            if (index + 1 >= args.Count || !args[index].StartsWith("--", StringComparison.Ordinal))
            {
                throw new UsageException("Options must be provided as '--name value' pairs.");
            }

            if (!allowedSet.Contains(args[index]))
            {
                throw new UsageException($"Unknown option '{args[index]}'.");
            }

            if (!result._values.TryGetValue(args[index], out var values))
            {
                values = [];
                result._values.Add(args[index], values);
            }

            values.Add(args[index + 1]);
        }

        return result;
    }

    public string Single(string name)
    {
        if (!_values.TryGetValue(name, out var values) || values.Count != 1 ||
            string.IsNullOrWhiteSpace(values[0]))
        {
            throw new UsageException($"Option '{name}' is required exactly once with a nonempty value.");
        }

        return values[0];
    }

    public string? Optional(string name)
    {
        if (!_values.TryGetValue(name, out var values))
        {
            return null;
        }

        if (values.Count != 1 || string.IsNullOrWhiteSpace(values[0]))
        {
            throw new UsageException($"Option '{name}' may appear at most once with a nonempty value.");
        }

        return values[0];
    }

    public IReadOnlyList<string> Many(string name)
    {
        if (!_values.TryGetValue(name, out var values))
        {
            return [];
        }

        if (values.Any(string.IsNullOrWhiteSpace))
        {
            throw new UsageException($"Option '{name}' requires nonempty values.");
        }

        return values;
    }
}
