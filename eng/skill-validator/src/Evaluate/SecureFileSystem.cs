using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace SkillValidator.Evaluate;

internal static class SecureFileSystem
{
    private const uint WindowsGenericRead = 0x80000000;
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
    private const int WindowsFileBasicInfo = 0;
    private const int WindowsFileStandardInfo = 1;

    private const int UnixReadOnly = 0;
    private const int UnixWriteOnly = 1;
    private const int UnixMissingPath = 2;
    private const int UnixFileTypeMask = 0xF000;
    private const int UnixDirectoryMode = 0x4000;
    private const int UnixRegularFileMode = 0x8000;
    private const int PalUnixWriteOnly = 0x0001;
    private const int PalUnixCloseOnExec = 0x0010;
    private const int PalUnixCreate = 0x0020;
    private const int PalUnixExclusive = 0x0040;
    private const int PalUnixNoFollow = 0x0200;

    internal static async Task WriteAllTextAsync(
        string allowedRoot,
        string path,
        string content,
        bool append,
        CancellationToken cancellationToken,
        Action? beforeLeafOpen = null)
    {
        if (!OperatingSystem.IsWindows() && !append)
        {
            await WriteUnixFileAtomicallyAsync(
                allowedRoot,
                path,
                content,
                cancellationToken,
                beforeLeafOpen);
            return;
        }

        var handle = OperatingSystem.IsWindows()
            ? OpenWindowsFile(allowedRoot, path, beforeLeafOpen)
            : OpenUnixFileForAppend(allowedRoot, path, beforeLeafOpen);
        if (handle is null && !OperatingSystem.IsWindows())
        {
            if (await TryWriteUnixFileIfMissingAsync(
                allowedRoot,
                path,
                content,
                cancellationToken,
                beforeLeafOpen: null))
            {
                return;
            }
            handle = OpenUnixFileForAppend(allowedRoot, path, beforeLeafOpen: null)
                ?? throw new IOException($"File appeared during append but could not be opened: {path}");
        }

        ArgumentNullException.ThrowIfNull(handle);
        using (handle)
            await WriteTextAsync(handle, content, append, cancellationToken);
    }

    internal static async Task<string> ReadAllTextAsync(
        string allowedRoot,
        string path,
        CancellationToken cancellationToken,
        Action? beforeLeafOpen = null)
    {
        using var opened = OpenExisting(
            allowedRoot,
            path,
            requireRegularFile: true,
            beforeLeafOpen);
        await using var stream = new FileStream(
            opened.Handle,
            FileAccess.Read,
            4096,
            isAsync: false);
        using var reader = new StreamReader(
            stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
            detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    internal static bool Exists(
        string allowedRoot,
        string path,
        Action? beforeLeafOpen = null)
    {
        try
        {
            using var opened = OpenExisting(
                allowedRoot,
                path,
                requireRegularFile: false,
                beforeLeafOpen);
            return true;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
    }

    internal static SecureFileStatus GetStatus(
        string allowedRoot,
        string path,
        Action? beforeLeafOpen = null)
    {
        using var opened = OpenExisting(
            allowedRoot,
            path,
            requireRegularFile: false,
            beforeLeafOpen);
        return opened.Status;
    }

    private static async Task WriteTextAsync(
        SafeFileHandle handle,
        string content,
        bool append,
        CancellationToken cancellationToken)
    {
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

    private static OpenedPath OpenExisting(
        string allowedRoot,
        string path,
        bool requireRegularFile,
        Action? beforeLeafOpen)
    {
        return OperatingSystem.IsWindows()
            ? OpenWindowsExisting(
                allowedRoot,
                path,
                requireRegularFile,
                beforeLeafOpen)
            : OpenUnixExisting(
                allowedRoot,
                path,
                requireRegularFile,
                beforeLeafOpen);
    }

    private static OpenedPath OpenWindowsExisting(
        string allowedRoot,
        string path,
        bool requireRegularFile,
        Action? beforeLeafOpen)
    {
        var segments = GetRelativeSegments(allowedRoot, path, allowRoot: true);
        if (segments.Length == 0)
        {
            if (requireRegularFile)
                throw new UnauthorizedAccessException($"Path is a directory: {path}");
            var rootHandle = OpenWindowsDirectory(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(allowedRoot)));
            return new OpenedPath(rootHandle, GetWindowsStatus(rootHandle, path));
        }

        using var parent = OpenWindowsDirectoryChain(
            allowedRoot,
            segments.AsSpan(0, segments.Length - 1),
            createMissing: false);
        beforeLeafOpen?.Invoke();
        var handle = OpenWindowsRelative(
            parent,
            segments[^1],
            isDirectory: requireRegularFile ? false : null,
            createMissing: false,
            path,
            desiredAccess: (requireRegularFile ? WindowsGenericRead : 0)
                | WindowsFileReadAttributes);
        var status = GetWindowsStatus(handle, path);
        if (requireRegularFile && !status.IsFile)
        {
            handle.Dispose();
            throw new UnauthorizedAccessException($"Path is not a regular file: {path}");
        }
        return new OpenedPath(handle, status);
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
            path,
            desiredAccess: WindowsGenericWrite | WindowsFileReadAttributes);

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
        bool? isDirectory,
        bool createMissing,
        string displayPath,
        uint? desiredAccess = null)
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
            (desiredAccess
                ?? (isDirectory == true
                    ? WindowsFileReadAttributes
                    : WindowsGenericWrite | WindowsFileReadAttributes))
                | WindowsSynchronize,
            ref objectAttributes,
            out _,
            0,
            0,
            WindowsFileShareRead | WindowsFileShareWrite | WindowsFileShareDelete,
            createMissing ? WindowsFileOpenIf : WindowsFileOpen,
            WindowsFileSynchronousIoNonAlert
                | WindowsFileOpenReparsePoint
                | (isDirectory == true
                    ? WindowsFileDirectoryFile
                    : isDirectory == false
                        ? WindowsFileNonDirectoryFile
                        : 0),
            0,
            0);
        if (status < 0 || rawHandle == 0 || rawHandle == -1)
        {
            var error = RtlNtStatusToDosError(status);
            if (error is 2 or 3 or 267)
                throw new FileNotFoundException($"Path not found: {displayPath}");
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

    private static OpenedPath OpenUnixExisting(
        string allowedRoot,
        string path,
        bool requireRegularFile,
        Action? beforeLeafOpen)
    {
        var segments = GetRelativeSegments(allowedRoot, path, allowRoot: true);
        if (segments.Length == 0)
        {
            if (requireRegularFile)
                throw new UnauthorizedAccessException($"Path is a directory: {path}");
            var rootHandle = OpenUnixRootDirectory(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(allowedRoot)));
            return new OpenedPath(rootHandle, GetUnixStatus(rootHandle, path));
        }

        using var parent = OpenUnixDirectoryChain(
            allowedRoot,
            segments.AsSpan(0, segments.Length - 1),
            createMissing: false);
        beforeLeafOpen?.Invoke();
        var fd = OpenUnixExistingEntry(
            parent.DangerousGetHandle().ToInt32(),
            segments[^1],
            path);
        if (fd < 0)
            ThrowUnixPathError(path);

        var handle = new SafeFileHandle(new IntPtr(fd), ownsHandle: true);
        var status = GetUnixStatus(handle, path);
        if (!status.IsFile && !status.IsDirectory)
        {
            handle.Dispose();
            throw new UnauthorizedAccessException($"Unsupported filesystem entry: {path}");
        }
        if (requireRegularFile && !status.IsFile)
        {
            handle.Dispose();
            throw new UnauthorizedAccessException($"Path is not a regular file: {path}");
        }
        return new OpenedPath(handle, status);
    }

    private static SafeFileHandle? OpenUnixFileForAppend(
        string allowedRoot,
        string path,
        Action? beforeLeafOpen)
    {
        var segments = GetRelativeSegments(allowedRoot, path, allowRoot: false);
        using var parent = OpenUnixDirectoryChain(
            allowedRoot,
            segments.AsSpan(0, segments.Length - 1),
            createMissing: true);
        beforeLeafOpen?.Invoke();
        var fd = OpenUnixFileForAppend(
            parent.DangerousGetHandle().ToInt32(),
            segments[^1]);
        if (fd < 0 && Marshal.GetLastPInvokeError() == UnixMissingPath)
            return null;
        if (fd < 0)
            ThrowUnixPathError(path);
        var handle = new SafeFileHandle(new IntPtr(fd), ownsHandle: true);
        if (!GetUnixStatus(handle, path).IsFile)
        {
            handle.Dispose();
            throw new UnauthorizedAccessException($"Path is not a regular file: {path}");
        }
        return handle;
    }

    private static int OpenUnixExistingEntry(
        int parentFd,
        string segment,
        string displayPath)
    {
        if (OperatingSystem.IsLinux())
        {
            return OpenUnixPath(
                GetLinuxDescriptorPath(parentFd, segment),
                UnixReadOnly | UnixNonBlock | UnixNoFollow | UnixCloseOnExec);
        }

        return OpenAtUnix(
            parentFd,
            segment,
            UnixReadOnly | UnixNonBlock | UnixNoFollow | UnixCloseOnExec);
    }

    private static async Task WriteUnixFileAtomicallyAsync(
        string allowedRoot,
        string path,
        string content,
        CancellationToken cancellationToken,
        Action? beforeLeafOpen)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(allowedRoot));
        var segments = GetRelativeSegments(root, path, allowRoot: false);
        using var parent = OpenUnixDirectoryChain(
            root,
            segments.AsSpan(0, segments.Length - 1),
            createMissing: true);
        using var rootHandle = OpenUnixRootDirectory(root);
        EnsureSameUnixDevice(rootHandle, parent, path);
        beforeLeafOpen?.Invoke();

        var temporaryName = $".skill-validator-{Guid.NewGuid():N}.tmp";
        var temporaryPath = Path.Combine(root, temporaryName);
        try
        {
            await WriteUnixTemporaryFileAsync(
                temporaryPath,
                content,
                cancellationToken);

            if (RenameAtUnix(
                rootHandle.DangerousGetHandle().ToInt32(),
                temporaryName,
                parent.DangerousGetHandle().ToInt32(),
                segments[^1]) != 0)
            {
                ThrowUnixPathError(path);
            }
        }
        finally
        {
            try { File.Delete(temporaryPath); } catch (FileNotFoundException) { }
        }
    }

    private static async Task<bool> TryWriteUnixFileIfMissingAsync(
        string allowedRoot,
        string path,
        string content,
        CancellationToken cancellationToken,
        Action? beforeLeafOpen)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(allowedRoot));
        var segments = GetRelativeSegments(root, path, allowRoot: false);
        using var parent = OpenUnixDirectoryChain(
            root,
            segments.AsSpan(0, segments.Length - 1),
            createMissing: true);
        using var rootHandle = OpenUnixRootDirectory(root);
        EnsureSameUnixDevice(rootHandle, parent, path);
        beforeLeafOpen?.Invoke();

        var temporaryName = $".skill-validator-{Guid.NewGuid():N}.tmp";
        var temporaryPath = Path.Combine(root, temporaryName);
        try
        {
            await WriteUnixTemporaryFileAsync(
                temporaryPath,
                content,
                cancellationToken);

            if (LinkAtUnix(
                rootHandle.DangerousGetHandle().ToInt32(),
                temporaryName,
                parent.DangerousGetHandle().ToInt32(),
                segments[^1],
                0) == 0)
            {
                return true;
            }

            var error = Marshal.GetLastPInvokeError();
            if (error == 17)
                return false;
            ThrowUnixPathError(path, error);
            return false;
        }
        finally
        {
            try { File.Delete(temporaryPath); } catch (FileNotFoundException) { }
        }
    }

    private static async Task WriteUnixTemporaryFileAsync(
        string temporaryPath,
        string content,
        CancellationToken cancellationToken)
    {
        var rawHandle = OpenSystemNative(
            temporaryPath,
            PalUnixWriteOnly
                | PalUnixCloseOnExec
                | PalUnixCreate
                | PalUnixExclusive
                | PalUnixNoFollow,
            Convert.ToInt32("600", 8));
        if (rawHandle == -1)
            ThrowUnixPathError(temporaryPath);

        using var temporaryHandle = new SafeFileHandle(rawHandle, ownsHandle: true);
        await WriteTextAsync(
            temporaryHandle,
            content,
            append: false,
            cancellationToken);
    }

    private static bool IsWindowsReparsePoint(SafeFileHandle handle, string path)
    {
        if (!GetFileInformationByHandleEx(
            handle,
            WindowsFileAttributeTagInfo,
            out WindowsFileAttributeTagInformation info,
            (uint)Marshal.SizeOf<WindowsFileAttributeTagInformation>()))
        {
            var error = Marshal.GetLastPInvokeError();
            throw new UnauthorizedAccessException($"Unable to inspect opened path: {path} (error {error})");
        }

        return (info.FileAttributes & (uint)FileAttributes.ReparsePoint) != 0;
    }

    private static SecureFileStatus GetWindowsStatus(
        SafeFileHandle handle,
        string path)
    {
        if (!GetFileInformationByHandleEx(
            handle,
            WindowsFileBasicInfo,
            out WindowsFileBasicInformation basic,
            (uint)Marshal.SizeOf<WindowsFileBasicInformation>())
            || !GetFileInformationByHandleEx(
                handle,
                WindowsFileStandardInfo,
                out WindowsFileStandardInformation standard,
                (uint)Marshal.SizeOf<WindowsFileStandardInformation>()))
        {
            var error = Marshal.GetLastPInvokeError();
            throw new UnauthorizedAccessException($"Unable to inspect opened path: {path} (error {error})");
        }

        return new SecureFileStatus(
            IsFile: standard.Directory == 0,
            IsDirectory: standard.Directory != 0,
            Size: standard.EndOfFile,
            Mtime: DateTimeOffset.FromFileTime(basic.LastWriteTime),
            Birthtime: DateTimeOffset.FromFileTime(basic.CreationTime));
    }

    private static SecureFileStatus GetUnixStatus(
        SafeFileHandle handle,
        string path)
    {
        if (GetFileStatusSystemNative(handle.DangerousGetHandle(), out var status) != 0)
            ThrowUnixPathError(path);

        var type = status.Mode & UnixFileTypeMask;
        return new SecureFileStatus(
            IsFile: type == UnixRegularFileMode,
            IsDirectory: type == UnixDirectoryMode,
            Size: status.Size,
            Mtime: FromUnixTime(status.ModificationTime, status.ModificationTimeNanoseconds),
            Birthtime: status.BirthTime != 0
                ? FromUnixTime(status.BirthTime, status.BirthTimeNanoseconds)
                : FromUnixTime(status.ChangeTime, status.ChangeTimeNanoseconds));
    }

    private static DateTimeOffset FromUnixTime(long seconds, long nanoseconds) =>
        DateTimeOffset.FromUnixTimeSeconds(seconds).AddTicks(nanoseconds / 100);

    private static SafeFileHandle OpenUnixDirectoryChain(
        string allowedRoot,
        ReadOnlySpan<string> segments,
        bool createMissing)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(allowedRoot));
        var current = OpenUnixRootDirectory(root);
        try
        {
            foreach (var segment in segments)
            {
                var nextFd = OpenUnixDirectoryEntry(
                    current.DangerousGetHandle().ToInt32(),
                    segment);
                if (nextFd < 0 && createMissing && Marshal.GetLastPInvokeError() == UnixMissingPath)
                {
                    if (MakeDirectoryAtUnix(
                        current.DangerousGetHandle().ToInt32(),
                        segment,
                        Convert.ToInt32("700", 8)) != 0
                        && Marshal.GetLastPInvokeError() != 17)
                    {
                        ThrowUnixPathError(Path.Combine(root, segment));
                    }
                    nextFd = OpenUnixDirectoryEntry(
                        current.DangerousGetHandle().ToInt32(),
                        segment);
                }
                if (nextFd < 0)
                    ThrowUnixPathError(Path.Combine(root, segment));

                var next = new SafeFileHandle(new IntPtr(nextFd), ownsHandle: true);
                if (!IsUnixDirectory(next))
                {
                    next.Dispose();
                    throw new FileNotFoundException($"Path component is not a directory: {segment}");
                }
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

    private static int OpenUnixDirectoryEntry(int parentFd, string segment)
    {
        if (!OperatingSystem.IsLinux())
        {
            return OpenAtUnix(
                parentFd,
                segment,
                UnixReadOnly | UnixNonBlock | UnixNoFollow | UnixCloseOnExec);
        }

        var descriptorPath = GetLinuxDescriptorPath(parentFd, segment);
        return OpenUnixPath(
            descriptorPath,
            UnixReadOnly | UnixNonBlock | UnixNoFollow | UnixCloseOnExec);
    }

    private static int OpenUnixFileForAppend(int parentFd, string segment)
    {
        if (!OperatingSystem.IsLinux())
        {
            return OpenAtUnix(
                parentFd,
                segment,
                UnixWriteOnly | UnixAppend | UnixNoFollow | UnixCloseOnExec);
        }

        return OpenUnixPath(
            GetLinuxDescriptorPath(parentFd, segment),
            UnixWriteOnly | UnixAppend | UnixNonBlock | UnixNoFollow | UnixCloseOnExec);
    }

    private static string GetLinuxDescriptorPath(int parentFd, string segment) =>
        $"/proc/self/fd/{parentFd}/{segment}";

    private static bool IsUnixDirectory(SafeFileHandle handle)
    {
        var duplicate = DuplicateFileDescriptorUnix(handle.DangerousGetHandle().ToInt32());
        if (duplicate < 0)
            return false;

        var directory = OpenDirectoryFromFileDescriptorUnix(duplicate);
        if (directory == 0)
        {
            CloseFileDescriptorUnix(duplicate);
            return false;
        }

        CloseDirectoryUnix(directory);
        return true;
    }

    private static void EnsureSameUnixDevice(
        SafeFileHandle root,
        SafeFileHandle parent,
        string path)
    {
        if (GetFileStatusSystemNative(root.DangerousGetHandle(), out var rootStatus) != 0)
            ThrowUnixPathError(path);
        if (GetFileStatusSystemNative(parent.DangerousGetHandle(), out var parentStatus) != 0)
            ThrowUnixPathError(path);
        if (rootStatus.Device != parentStatus.Device)
            throw new UnauthorizedAccessException($"Mount-point traversal blocked: {path}");
    }

    private static SafeFileHandle OpenUnixRootDirectory(string root)
    {
        var fd = OpenUnixPath(
            root,
            UnixReadOnly | UnixNonBlock | UnixNoFollow | UnixCloseOnExec);
        if (fd < 0)
            ThrowUnixPathError(root);

        var handle = new SafeFileHandle(new IntPtr(fd), ownsHandle: true);
        if (!GetUnixStatus(handle, root).IsDirectory)
        {
            handle.Dispose();
            throw new FileNotFoundException($"Allowed root is not a directory: {root}");
        }
        return handle;
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

    private static void ThrowUnixPathError(string path, int? errorCode = null)
    {
        var error = errorCode ?? Marshal.GetLastPInvokeError();
        if (error is UnixMissingPath or 20)
            throw new FileNotFoundException($"Path not found: {path}");
        throw new UnauthorizedAccessException(
            $"Unable to access path without following symbolic links: {path} (errno {error})");
    }

    private static int UnixAppend => OperatingSystem.IsMacOS() ? 0x0008 : 0x0400;
    private static int UnixNonBlock => OperatingSystem.IsMacOS() ? 0x0004 : 0x0800;
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

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(
        SafeFileHandle file,
        int fileInformationClass,
        out WindowsFileBasicInformation fileInformation,
        uint bufferSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(
        SafeFileHandle file,
        int fileInformationClass,
        out WindowsFileStandardInformation fileInformation,
        uint bufferSize);

    [DllImport("System.Native", EntryPoint = "SystemNative_Open", SetLastError = true)]
    private static extern nint OpenSystemNative(string path, int flags, int mode);

    [DllImport("System.Native", EntryPoint = "SystemNative_FStat", SetLastError = true)]
    private static extern int GetFileStatusSystemNative(
        nint fileDescriptor,
        out UnixFileStatus status);

    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int OpenUnixPath(string path, int flags);

    [DllImport("libc", EntryPoint = "openat", SetLastError = true)]
    private static extern int OpenAtUnix(int directoryFd, string path, int flags);

    [DllImport("libc", EntryPoint = "mkdirat", SetLastError = true)]
    private static extern int MakeDirectoryAtUnix(int directoryFd, string path, int mode);

    [DllImport("libc", EntryPoint = "renameat", SetLastError = true)]
    private static extern int RenameAtUnix(
        int oldDirectoryFd,
        string oldPath,
        int newDirectoryFd,
        string newPath);

    [DllImport("libc", EntryPoint = "linkat", SetLastError = true)]
    private static extern int LinkAtUnix(
        int oldDirectoryFd,
        string oldPath,
        int newDirectoryFd,
        string newPath,
        int flags);

    [DllImport("libc", EntryPoint = "fdopendir", SetLastError = true)]
    private static extern nint OpenDirectoryFromFileDescriptorUnix(int fileDescriptor);

    [DllImport("libc", EntryPoint = "dup", SetLastError = true)]
    private static extern int DuplicateFileDescriptorUnix(int fileDescriptor);

    [DllImport("libc", EntryPoint = "close", SetLastError = true)]
    private static extern int CloseFileDescriptorUnix(int fileDescriptor);

    [DllImport("libc", EntryPoint = "closedir", SetLastError = true)]
    private static extern int CloseDirectoryUnix(nint directory);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct UnixFileStatus
    {
        internal readonly int Flags;
        internal readonly int Mode;
        internal readonly uint UserId;
        internal readonly uint GroupId;
        internal readonly long Size;
        internal readonly long AccessTime;
        internal readonly long AccessTimeNanoseconds;
        internal readonly long ModificationTime;
        internal readonly long ModificationTimeNanoseconds;
        internal readonly long ChangeTime;
        internal readonly long ChangeTimeNanoseconds;
        internal readonly long BirthTime;
        internal readonly long BirthTimeNanoseconds;
        internal readonly long Device;
        internal readonly long RawDevice;
        internal readonly long Inode;
        internal readonly uint UserFlags;
        internal readonly uint HardLinkCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct WindowsFileAttributeTagInformation
    {
        internal readonly uint FileAttributes;
        internal readonly uint ReparseTag;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct WindowsFileBasicInformation
    {
        internal readonly long CreationTime;
        internal readonly long LastAccessTime;
        internal readonly long LastWriteTime;
        internal readonly long ChangeTime;
        internal readonly uint FileAttributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct WindowsFileStandardInformation
    {
        internal readonly long AllocationSize;
        internal readonly long EndOfFile;
        internal readonly uint NumberOfLinks;
        internal readonly byte DeletePending;
        internal readonly byte Directory;
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

    private sealed record OpenedPath(
        SafeFileHandle Handle,
        SecureFileStatus Status) : IDisposable
    {
        public void Dispose() => Handle.Dispose();
    }
}

internal sealed record SecureFileStatus(
    bool IsFile,
    bool IsDirectory,
    long Size,
    DateTimeOffset Mtime,
    DateTimeOffset Birthtime);
