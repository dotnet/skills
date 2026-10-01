using System;
using System.Runtime.InteropServices;

internal static class CallbackNative
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void Callback(int level, IntPtr message);

    [DllImport("callbacklib", EntryPoint = "set_log_callback", CallingConvention = CallingConvention.Cdecl)]
    private static extern void SetCallback(Callback? callback);

    internal static void Register(Action<int, string> sink)
    {
        Callback callback = (level, message) =>
            sink(level, Marshal.PtrToStringUTF8(message) ?? string.Empty);
        SetCallback(callback);
    }
}
