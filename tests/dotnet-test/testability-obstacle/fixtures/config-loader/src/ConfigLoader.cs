namespace Configuration;

public sealed class ConfigLoader
{
    public string? Load(string path)
    {
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }
}
