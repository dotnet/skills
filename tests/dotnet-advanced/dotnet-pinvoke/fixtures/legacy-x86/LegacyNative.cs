using System.Runtime.InteropServices;

internal static partial class LegacyNative
{
    [LibraryImport("compresslib", EntryPoint = "compress_buffer")]
    internal static unsafe partial int CompressBuffer(
        byte* input,
        nuint inputLength,
        byte* output,
        nuint outputLength,
        nuint* bytesWritten);
}
