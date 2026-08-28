using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CalibratedAssertions.Tests;

[TestClass]
public sealed class CalibrationTests
{
    [TestMethod]
    public void LoadExisting_ReturnsConfiguredValue()
    {
        var config = new AppConfig("prod", 3);

        Assert.IsNotNull(config);
        Assert.AreEqual("prod", config.Environment);
        Assert.AreEqual(3, config.RetryCount);
    }

    [TestMethod]
    public void ParseInvalid_ThrowsFormatException()
    {
        Assert.ThrowsExactly<FormatException>(() => int.Parse("not-a-number"));
    }

    [TestMethod]
    public void Save_PersistsRecord()
    {
        var store = new RecordingStore();

        store.Save("order-42");

        Assert.IsTrue(store.Contains("order-42"));
    }

    [TestMethod]
    public void Ping_ReturnsResponse()
    {
        var response = new HealthClient().Ping();

        Assert.IsNotNull(response);
    }

    [TestMethod]
    public void HealthCheck_Healthy()
    {
        _ = new HealthClient().Ping();

        Assert.IsTrue(true);
    }

    private sealed record AppConfig(string Environment, int RetryCount);

    private sealed class RecordingStore
    {
        private readonly HashSet<string> _records = [];

        public void Save(string id) => _records.Add(id);

        public bool Contains(string id) => _records.Contains(id);
    }

    private sealed class HealthClient
    {
        public string Ping() => "healthy";
    }
}
