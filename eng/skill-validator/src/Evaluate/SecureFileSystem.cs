using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace SkillValidator.Evaluate;

internal static class SecureFileSystem
{
    private const uint WindowsGenericWrite = 0x40000000;
    private const uint WindowsFileReadAttributes = 0x00000080;
    private const uint WindowsSynchronize = 0x00100000;
    private const uint WindowsFileShareRead = 0x00000001;
    private const uint WindowsFileShareWrite = 0x00000002;
    private const uint WindowsFileShareDelete = 0x00000004;
    private const uint WindowsOpenExisting = 3;
    private const uint WindowsFileFlagOpenReparsePoint = 0x00200000;
    private const uint WindowsFileFlagBackupSemantics = 0x02000000;
    private const int WindowsFileAttributeTagInfo = 9;
    private const uint WindowsObjectCaseInsensitive = 0x00000040;
    private const uint WindowsFileOpen = 1;
    private const uint WindowsFileOpenIf = 3;
    private const uint WindowsFileDirectoryFile = 0x00000001;
    private const uint WindowsFileSynchronousIoNonAlert = 0x00000020;
    private const uint WindowsFileNonDirectoryFile = 0x00000040;
    private const uint WindowsFileOpenReparsePoint = 0x00200000;

    private const int UnixReadOnly = 0;
    private const int UnixWriteOnly = 1;
    private const int UnixMissingPath = 2;

    internal static async Task WriteAllTextAsync(
        string allowedRoot,
        string path,
        string content,
        bool append,
        CancellationToken cancellationToken,
        Action? beforeLeafOpen = null)
    {
        using var handle = OperatingSystem.IsWindows()
            ? OpenWindowsFile(allowedRoot, path, beforeLeafOpen)
            : OpenUnixFile(allowedRoot, path, append, beforeLeafOpen);
        await using var stream = new FileStream(handle, FileAccess.Write, 4096, isAsync: false);
        if (append)
            stream.Seek(0, SeekOrigin.End);
        else
            stream.SetLength(0);

        await using var writer = new StreamWriter(
            stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
            bufferSize: 1024,
            leaveOpen: false);
        await writer.WriteAsync(content.AsMemory(), cancellationToken);
    }

    internal static void CreateDirectory(string allowedRoot, string path)
    {
        var segments = GetRelativeSegments(allowedRoot, path, allowRoot: true);
        if (segments.Length == 0)
            return;

        if (OperatingSystem.IsWindows())
        {
            using var handles = OpenWindowsDirectoryChain(allowedRoot, segments, createMissing: true);
            return;
        }

        using var handle = OpenUnixDirectoryChain(allowedRoot, segments, createMissing: true);
    }

    private static SafeFileHandle OpenWindowsFile(
        string allowedRoot,
        string path,
        Action? beforeLeafOpen)
    {
        var segments = GetRelativeSegments(allowedRoot, path, allowRoot: false);
        var parentSegments = segments[..^1];
        using var parent = OpenWindowsDirectoryChain(
            allowedRoot,
            parentSegments,
            createMissing: true);
        beforeLeafOpen?.Invoke();

        var handle = OpenWindowsRelative(
            parent,
            segments[^1],
            isDirectory: false,
            createMissing: true,
            path);

        try
        {
            if (IsWindowsReparsePoint(handle, path))
                throw new UnauthorizedAccessException($"Symbolic-link traversal blocked: {path}");
            return handle;
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    private static SafeFileHandle OpenWindowsDirectoryChain(
        string allowedRoot,
        ReadOnlySpan<string> segments,
        bool createMissing)
    {
        var currentPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(allowedRoot));
        var current = OpenWindowsDirectory(currentPath);
        try
        {
            foreach (var segment in segments)
            {
                currentPath = Path.Combine(currentPath, segment);
                var next = OpenWindowsRelative(
                    current,
                    segment,
                    isDirectory: true,
                    createMissing,
                    currentPath);
                current.Dispose();
                current = next;
            }
            return current;
        }
        catch
        {
            current.Dispose();
            throw;
        }
    }

    private static SafeFileHandle OpenWindowsDirectory(string path)
    {
        var handle = CreateFileWindows(
            path,
            WindowsFileReadAttributes,
            WindowsFileShareRead | WindowsFileShareWrite | WindowsFileShareDelete,
            0,
            WindowsOpenExisting,
            WindowsFileFlagBackupSemantics | WindowsFileFlagOpenReparsePoint,
            0);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            throw new UnauthorizedAccessException($"Unable to open directory without following reparse points: {path} (error {error})");
        }

        try
        {
            if (IsWindowsReparsePoint(handle, path))
                throw new UnauthorizedAccessException($"Symbolic-link traversal blocked: {path}");
            return handle;
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    private static SafeFileHandle OpenWindowsRelative(
        SafeFileHandle parent,
        string name,
        bool isDirectory,
        bool createMissing,
        string displayPath)
    {
        using var nativeName = new NativeUnicodeString(name);
        var objectAttributes = new WindowsObjectAttributes
        {
            Length = Marshal.SizeOf<WindowsObjectAttributes>(),
            RootDirectory = parent.DangerousGetHandle(),
            ObjectName = nativeName.Structure,
            Attributes = WindowsObjectCaseInsensitive,
        };
        var status = NtCreateFile(
            out var rawHandle,
            (isDirectory
                ? WindowsFileReadAttributes
                : WindowsGenericWrite | WindowsFileReadAttributes)
                | WindowsSynchronize,
            ref objectAttributes,
            out _,
            0,
            0,
            WindowsFileShareRead | WindowsFileShareWrite | WindowsFileShareDelete,
            createMissing ? WindowsFileOpenIf : WindowsFileOpen,
            WindowsFileSynchronousIoNonAlert
                | WindowsFileOpenReparsePoint
                | (isDirectory ? WindowsFileDirectoryFile : WindowsFileNonDirectoryFile),
            0,
            0);
        if (status < 0 || rawHandle == 0 || rawHandle == -1)
        {
            var error = RtlNtStatusToDosError(status);
            throw new UnauthorizedAccessException(
                $"Unable to open path without following reparse points: {displayPath} (error {error})");
        }

        var handle = new SafeFileHandle(rawHandle, ownsHandle: true);
        try
        {
            if (IsWindowsReparsePoint(handle, displayPath))
                throw new UnauthorizedAccessException($"Symbolic-link traversal blocked: {displayPath}");
            return handle;
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    private static SafeFileHandle OpenUnixFile(
        string allowedRoot,
        string path,
        bool append,
        Action? beforeLeafOpen)
    {
        var segments = GetRelativeSegments(allowedRoot, path, allowRoot: false);
        using var parent = OpenUnixDirectoryChain(
            allowedRoot,
            segments.AsSpan(0, segments.Length - 1),
            createMissing: true);
        beforeLeafOpen?.Invoke();
        var flags = UnixWriteOnly | UnixCreate | UnixNoFollow | UnixCloseOnExec;
        if (append)
            flags |= UnixAppend;

        var fd = OpenAtUnix(
            parent.DangerousGetHandle().ToInt32(),
            segments[^1],
            flags,
            Convert.ToUInt32("600", 8));
        if (fd < 0)
            ThrowUnixPathError(path);
        return new SafeFileHandle(new IntPtr(fd), ownsHandle: true);
    }

    private static bool IsWindowsReparsePoint(SafeFileHandle handle, string path)
    {
        if (!GetFileInformationByHandleEx(
            handle,
            WindowsFileAttributeTagInfo,
            out var info,
            (uint)Marshal.SizeOf<WindowsFileAttributeTagInformation>()))
        {
            var error = Marshal.GetLastPInvokeError();
            throw new UnauthorizedAccessException($"Unable to inspect opened path: {path} (error {error})");
        }

        return (info.FileAttributes & (uint)FileAttributes.ReparsePoint) != 0;
    }

    private static SafeFileHandle OpenUnixDirectoryChain(
        string allowedRoot,
        ReadOnlySpan<string> segments,
        bool createMissing)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(allowedRoot));
        var fd = OpenUnix(root, UnixReadOnly | UnixDirectory | UnixNoFollow | UnixCloseOnExec, 0);
        if (fd < 0)
            ThrowUnixPathError(root);

        var current = new SafeFileHandle(new IntPtr(fd), ownsHandle: true);
        try
        {
            foreach (var segment in segments)
            {
                var nextFd = OpenAtUnix(
                    current.DangerousGetHandle().ToInt32(),
                    segment,
                    UnixReadOnly | UnixDirectory | UnixNoFollow | UnixCloseOnExec,
                    0);
                if (nextFd < 0 && createMissing && Marshal.GetLastPInvokeError() == UnixMissingPath)
                {
                    if (MakeDirectoryAtUnix(
                        current.DangerousGetHandle().ToInt32(),
                        segment,
                        Convert.ToUInt32("700", 8)) != 0
                        && Marshal.GetLastPInvokeError() != 17)
                    {
                        ThrowUnixPathError(Path.Combine(root, segment));
                    }
                    nextFd = OpenAtUnix(
                        current.DangerousGetHandle().ToInt32(),
                        segment,
                        UnixReadOnly | UnixDirectory | UnixNoFollow | UnixCloseOnExec,
                        0);
                }
                if (nextFd < 0)
                    ThrowUnixPathError(Path.Combine(root, segment));

                var next = new SafeFileHandle(new IntPtr(nextFd), ownsHandle: true);
                current.Dispose();
                current = next;
            }
            return current;
        }
        catch
        {
            current.Dispose();
            throw;
        }
    }

    private static string[] GetRelativeSegments(string allowedRoot, string path, bool allowRoot)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(allowedRoot));
        var full = Path.GetFullPath(path);
        var relative = Path.GetRelativePath(root, full);
        if (relative == ".")
        {
            if (allowRoot)
                return [];
            throw new UnauthorizedAccessException($"A file path must be below the allowed root: {path}");
        }
        if (Path.IsPathRooted(relative)
            || relative == ".."
            || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException($"Path outside allowed root: {path}");
        }

        return relative.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
    }

    private static void ThrowUnixPathError(string path)
    {
        var error = Marshal.GetLastPInvokeError();
        throw new UnauthorizedAccessException(
            $"Unable to access path without following symbolic links: {path} (errno {error})");
    }

    private static int UnixCreate => OperatingSystem.IsMacOS() ? 0x0200 : 0x0040;
    private static int UnixAppend => OperatingSystem.IsMacOS() ? 0x0008 : 0x0400;
    private static int UnixDirectory => OperatingSystem.IsMacOS() ? 0x100000 : 0x10000;
    private static int UnixNoFollow => OperatingSystem.IsMacOS() ? 0x0100 : 0x20000;
    private static int UnixCloseOnExec => OperatingSystem.IsMacOS() ? 0x1000000 : 0x80000;

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFileWindows(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        nint securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        nint templateFile);

    [DllImport("ntdll.dll")]
    private static extern int NtCreateFile(
        out nint fileHandle,
        uint desiredAccess,
        ref WindowsObjectAttributes objectAttributes,
        out WindowsIoStatusBlock ioStatusBlock,
        nint allocationSize,
        uint fileAttributes,
        uint shareAccess,
        uint createDisposition,
        uint createOptions,
        nint eaBuffer,
        uint eaLength);

    [DllImport("ntdll.dll")]
    private static extern uint RtlNtStatusToDosError(int status);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(
        SafeFileHandle file,
        int fileInformationClass,
        out WindowsFileAttributeTagInformation fileInformation,
        uint bufferSize);

    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int OpenUnix(string path, int flags, uint mode);

    [DllImport("libc", EntryPoint = "openat", SetLastError = true)]
    private static extern int OpenAtUnix(int directoryFd, string path, int flags, uint mode);

    [DllImport("libc", EntryPoint = "mkdirat", SetLastError = true)]
    private static extern int MakeDirectoryAtUnix(int directoryFd, string path, uint mode);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct WindowsFileAttributeTagInformation
    {
        internal readonly uint FileAttributes;
        internal readonly uint ReparseTag;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowsObjectAttributes
    {
        internal int Length;
        internal nint RootDirectory;
        internal nint ObjectName;
        internal uint Attributes;
        internal nint SecurityDescriptor;
        internal nint SecurityQualityOfService;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct WindowsIoStatusBlock
    {
        internal readonly nint Status;
        internal readonly nuint Information;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct WindowsUnicodeString
    {
        internal readonly ushort Length;
        internal readonly ushort MaximumLength;
        internal readonly nint Buffer;

        internal WindowsUnicodeString(ushort length, ushort maximumLength, nint buffer)
        {
            Length = length;
            MaximumLength = maximumLength;
            Buffer = buffer;
        }
    }

    private sealed class NativeUnicodeString : IDisposable
    {
        private readonly nint _buffer;
        internal nint Structure { get; }

        internal NativeUnicodeString(string value)
        {
            _buffer = Marshal.StringToHGlobalUni(value);
            var byteLength = checked((ushort)(value.Length * sizeof(char)));
            var unicode = new WindowsUnicodeString(
                byteLength,
                checked((ushort)(byteLength + sizeof(char))),
                _buffer);
            Structure = Marshal.AllocHGlobal(Marshal.SizeOf<WindowsUnicodeString>());
            Marshal.StructureToPtr(unicode, Structure, fDeleteOld: false);
        }

        public void Dispose()
        {
            Marshal.FreeHGlobal(Structure);
            Marshal.FreeHGlobal(_buffer);
        }
    }
}
