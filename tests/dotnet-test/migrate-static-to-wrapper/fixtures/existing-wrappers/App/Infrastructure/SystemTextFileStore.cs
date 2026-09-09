using App.Abstractions;

namespace App.Infrastructure;

public sealed class SystemTextFileStore : ITextFileStore
{
    public void WriteAllText(string path, string contents) => File.WriteAllText(path, contents);

    public string ReadAllText(string path) => File.ReadAllText(path);
}
