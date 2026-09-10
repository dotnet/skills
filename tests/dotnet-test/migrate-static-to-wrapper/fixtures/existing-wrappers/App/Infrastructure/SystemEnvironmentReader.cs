using App.Abstractions;

namespace App.Infrastructure;

public sealed class SystemEnvironmentReader : IEnvironmentReader
{
    public string? GetEnvironmentVariable(string name)
        => Environment.GetEnvironmentVariable(name);
}
