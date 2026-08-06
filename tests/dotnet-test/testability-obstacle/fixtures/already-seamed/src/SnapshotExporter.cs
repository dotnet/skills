namespace Snapshots;

public interface ITextStore
{
    void Write(string path, string content);
}

public sealed class SnapshotExporter(TimeProvider timeProvider, ITextStore store)
{
    public string Export(string content)
    {
        var path = $"snapshot-{timeProvider.GetUtcNow():yyyyMMdd-HHmmss}.txt";
        store.Write(path, content);
        return path;
    }
}
