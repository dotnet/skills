using System.Runtime.InteropServices;

internal static partial class TextNative
{
    [LibraryImport(
        "textlib",
        EntryPoint = "set_name_utf8",
        StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int SetName(string name);

    [LibraryImport(
        "textlib",
        EntryPoint = "set_title_utf16",
        StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int SetTitle(string title);
}
