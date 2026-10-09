namespace MissingStore;

public sealed class DocumentReader
{
    public string Read(string path) => File.ReadAllText(path);
}
