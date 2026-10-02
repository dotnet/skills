using System;
using System.Runtime.InteropServices;

internal static partial class ResourceNative
{
    [LibraryImport("resourcelib", EntryPoint = "open_resource")]
    private static partial IntPtr OpenResource();

    [LibraryImport("resourcelib", EntryPoint = "use_resource")]
    internal static partial int UseResource(IntPtr resource);

    [LibraryImport("resourcelib", EntryPoint = "close_resource")]
    internal static partial void CloseResource(IntPtr resource);

    public static IntPtr Open() => OpenResource();
}
