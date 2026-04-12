namespace StaticHeavy.Services;

public class ConfigLoader
{
    public Dictionary<string, string> LoadConfig(string configPath)
    {
        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException($"Config file not found: {configPath}");
        }

        var content = File.ReadAllText(configPath);
        var lines = content.Split('\n');
        var config = new Dictionary<string, string>();

        foreach (var line in lines)
        {
            var parts = line.Split('=', 2);
            if (parts.Length == 2)
            {
                config[parts[0].Trim()] = parts[1].Trim();
            }
        }

        Console.WriteLine($"Loaded {config.Count} config entries from {configPath}");
        return config;
    }

    public void SaveConfig(string configPath, Dictionary<string, string> config)
    {
        var dir = Path.GetDirectoryName(configPath);
        if (dir != null && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var lines = config.Select(kv => $"{kv.Key}={kv.Value}");
        File.WriteAllText(configPath, string.Join('\n', lines));
    }
}
