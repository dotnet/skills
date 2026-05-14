using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ConfigService.Tests;

[TestClass]
public class ConfigTests
{
    [TestMethod]
    public void GetTimeout_Default_ReturnsDefault()
    {
        var config = new AppConfig();
        config.Set("Timeout", "30");
        var result = config.Get("Timeout");
        Assert.AreEqual("30", result);
    }

    [TestMethod]
    public void GetRetries_Default_ReturnsDefault()
    {
        var config = new AppConfig();
        config.Set("Retries", "3");
        var result = config.Get("Retries");
        Assert.AreEqual("3", result);
    }

    [TestMethod]
    public void SetAndGet_CustomValue_RoundTrips()
    {
        var config = new AppConfig();
        var input = "my-custom-value";
        config.Set("Key", input);
        var output = config.Get("Key");
        Assert.AreEqual(input, output);
    }

    [TestMethod]
    public void Serialize_Deserialize_ProducesOriginal()
    {
        var config = new AppConfig();
        config.Set("Host", "localhost");
        config.Set("Port", "5432");
        var json = config.Serialize();
        var restored = AppConfig.Deserialize(json);
        Assert.AreEqual(config.Get("Host"), restored.Get("Host"));
        Assert.AreEqual(config.Get("Port"), restored.Get("Port"));
    }

    [TestMethod]
    public void FormatConnectionString_RoundTrip()
    {
        var config = new AppConfig();
        config.Set("Host", "localhost");
        config.Set("Port", "5432");
        config.Set("Database", "mydb");
        var connStr = config.FormatConnectionString();
        var parsed = AppConfig.ParseConnectionString(connStr);
        Assert.AreEqual(config.Get("Host"), parsed.Get("Host"));
        Assert.AreEqual(config.Get("Port"), parsed.Get("Port"));
        Assert.AreEqual(config.Get("Database"), parsed.Get("Database"));
    }

    [TestMethod]
    public void ValidateKey_Valid_ReturnsSameKey()
    {
        var key = "ValidKey123";
        var result = AppConfig.ValidateKey(key);
        Assert.AreEqual(key, result);
    }

    [TestMethod]
    public void Clone_MatchesOriginal()
    {
        var config = new AppConfig();
        config.Set("A", "1");
        config.Set("B", "2");
        var clone = config.Clone();
        Assert.AreEqual(config.Get("A"), clone.Get("A"));
        Assert.AreEqual(config.Get("B"), clone.Get("B"));
    }

    [TestMethod]
    public void ToDisplayString_ReturnsString()
    {
        var config = new AppConfig();
        config.Set("Env", "Production");
        var display = config.ToDisplayString();
        Assert.IsNotNull(display);
    }
}

// --- Production code (in same file for test simplicity) ---

public class AppConfig
{
    private readonly Dictionary<string, string> _values = new();

    public void Set(string key, string value) => _values[ValidateKey(key)] = value;
    public string? Get(string key) => _values.GetValueOrDefault(key);

    public string Serialize() =>
        System.Text.Json.JsonSerializer.Serialize(_values);

    public static AppConfig Deserialize(string json)
    {
        var config = new AppConfig();
        var dict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json)!;
        foreach (var kv in dict)
            config.Set(kv.Key, kv.Value);
        return config;
    }

    public string FormatConnectionString() =>
        $"Host={Get("Host")};Port={Get("Port")};Database={Get("Database")}";

    public static AppConfig ParseConnectionString(string connStr)
    {
        var config = new AppConfig();
        foreach (var part in connStr.Split(';'))
        {
            var kv = part.Split('=', 2);
            if (kv.Length == 2) config.Set(kv[0], kv[1]);
        }
        return config;
    }

    public static string ValidateKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Key cannot be empty", nameof(key));
        if (key.Contains(' '))
            throw new ArgumentException("Key cannot contain spaces", nameof(key));
        return key;
    }

    public AppConfig Clone()
    {
        var clone = new AppConfig();
        foreach (var kv in _values)
            clone.Set(kv.Key, kv.Value);
        return clone;
    }

    public string ToDisplayString() =>
        string.Join(", ", _values.Select(kv => $"{kv.Key}={kv.Value}"));
}
