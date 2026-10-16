using System.Runtime.InteropServices;

internal static class NativeMethods
{
    [DllImport("compresslib", EntryPoint = "compress_buffer")]
    internal static extern unsafe int CompressBuffer(
        byte* input,
        ulong inputLength,
        byte* output,
        ulong outputLength,
        ulong* bytesWritten);
}
