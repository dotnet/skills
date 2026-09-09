namespace App.Abstractions;

public interface IEnvironmentReader
{
    string? GetEnvironmentVariable(string name);
}
