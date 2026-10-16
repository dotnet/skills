using System;
using System.Runtime.InteropServices;

internal static partial class VersionNative
{
    [LibraryImport("widget", EntryPoint = "widget_get_version")]
    private static partial IntPtr GetVersion();

    [LibraryImport("widget", EntryPoint = "widget_free_version")]
    private static partial void FreeVersion(IntPtr value);

    internal static string ReadVersion()
    {
        IntPtr ptr = GetVersion();
        if (ptr == IntPtr.Zero)
        {
            throw new InvalidOperationException("No version was returned.");
        }

        try
        {
            return Marshal.PtrToStringUTF8(ptr)
                ?? throw new InvalidOperationException("The version was not valid UTF-8.");
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }
}
