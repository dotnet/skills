using System.Text.Json;

namespace DockDispatch;

public static class SnapshotCodec
{
    public static byte[] Encode(DispatchSnapshot snapshot) =>
        JsonSerializer.SerializeToUtf8Bytes(snapshot);
}
