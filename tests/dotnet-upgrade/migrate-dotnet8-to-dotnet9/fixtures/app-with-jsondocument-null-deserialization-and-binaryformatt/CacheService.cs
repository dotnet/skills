using System.Runtime.Serialization.Formatters.Binary;
class CacheService
{
    void Save(Stream stream, object sessionData)
    {
        AppContext.SetSwitch("System.Runtime.Serialization.EnableUnsafeBinaryFormatterSerialization", true);
        var formatter = new BinaryFormatter();
        formatter.Serialize(stream, sessionData);
    }
}
