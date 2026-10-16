using System.ComponentModel;
using System.Runtime.InteropServices;

internal static partial class DeviceNative
{
    [LibraryImport(
        "devicelib",
        EntryPoint = "open_device_w",
        StringMarshalling = StringMarshalling.Utf16)]
    private static partial int OpenDeviceNative(string path);

    internal static int Open(string path)
    {
        int handle = OpenDeviceNative(path);
        if (handle == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        return handle;
    }
}
