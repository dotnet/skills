using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace SkillValidator.Evaluate;

internal static class SecureFileSystem
{
    private const uint WindowsGenericRead = 0x80000000;
    private const uint WindowsGenericWrite = 0x40000000;
    private const uint WindowsDelete = 0x00010000;
    private const uint WindowsFileListDirectory = 0x00000001;
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
    private const int WindowsFileDispositionInfo = 4;
    private const int WindowsFileStandardInfo = 1;
    private const int WindowsFileNamesInformation = 12;
    private const int WindowsNtFileRenameInformation = 10;
    private const int WindowsStatusNoMoreFiles = unchecked((int)0x80000006);

    private const int UnixReadOnly = 0;
    private const int UnixWriteOnly = 1;
    private const int UnixMissingPath = 2;
    private const int UnixFileTypeMask = 0xF000;
    private const int UnixDirectoryMode = 0x4000;
    private const int UnixRegularFileMode = 0x8000;
    private const int UnixSymbolicLinkMode = 0xA000;

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

    internal static IReadOnlyList<SecureDirectoryEntry> EnumerateDirectory(
        string allowedRoot,
        string path,
        Action? afterDirectoryOpen = null)
    {
        try
        {
            if (OperatingSystem.IsWindows())
                return EnumerateWindowsDirectory(allowedRoot, path, afterDirectoryOpen);
            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
                return EnumerateUnixDirectory(allowedRoot, path, afterDirectoryOpen);
            throw new NotSupportedException(
                "Secure directory enumeration is supported only on Windows, Linux, and macOS.");
        }
        catch (FileNotFoundException)
        {
            return [];
        }
        catch (DirectoryNotFoundException)
        {
            return [];
        }
    }

    internal static void Remove(
        string allowedRoot,
        string path,
        bool recursive,
        Action? afterEntryOpen = null)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                RemoveWindows(allowedRoot, path, recursive, afterEntryOpen);
                return;
            }
            if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
            {
                throw new NotSupportedException(
                    "Secure removal is supported only on Windows, Linux, and macOS.");
            }

            RemoveUnix(allowedRoot, path, recursive, afterEntryOpen);
        }
        catch (FileNotFoundException)
        {
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    internal static void Rename(
        string sourceRoot,
        string sourcePath,
        string destinationRoot,
        string destinationPath,
        Action? afterParentsOpen = null)
    {
        if (!Exists(sourceRoot, sourcePath))
            return;

        if (OperatingSystem.IsWindows())
        {
            RenameWindows(
                sourceRoot,
                sourcePath,
                destinationRoot,
                destinationPath,
                afterParentsOpen);
            return;
        }
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            throw new NotSupportedException(
                "Secure rename is supported only on Windows, Linux, and macOS.");
        }

        RenameUnix(
            sourceRoot,
            sourcePath,
            destinationRoot,
            destinationPath,
            afterParentsOpen);
    }

    private static IReadOnlyList<SecureDirectoryEntry> EnumerateWindowsDirectory(
        string allowedRoot,
        string path,
        Action? afterDirectoryOpen)
    {
        var segments = GetRelativeSegments(allowedRoot, path, allowRoot: true);
        SafeFileHandle directory;
        if (segments.Length == 0)
        {
            directory = OpenWindowsDirectory(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(allowedRoot)),
                WindowsFileReadAttributes | WindowsFileListDirectory);
        }
        else
        {
            using var parent = OpenWindowsDirectoryChain(
                allowedRoot,
                segments.AsSpan(0, segments.Length - 1),
                createMissing: false);
            directory = OpenWindowsRelative(
                parent,
                segments[^1],
                isDirectory: true,
                createMissing: false,
                path,
                desiredAccess: WindowsFileReadAttributes | WindowsFileListDirectory);
        }

        using (directory)
        {
            afterDirectoryOpen?.Invoke();
            var entries = new List<SecureDirectoryEntry>();
            foreach (var name in EnumerateWindowsDirectoryNames(directory, path))
            {
                try
                {
                    using var child = OpenWindowsRelative(
                        directory,
                        name,
                        isDirectory: null,
                        createMissing: false,
                        Path.Combine(path, name),
                        desiredAccess: WindowsFileReadAttributes);
                    var status = GetWindowsStatus(child, Path.Combine(path, name));
                    entries.Add(new SecureDirectoryEntry(name, status.IsDirectory));
                }
                catch (FileNotFoundException)
                {
                }
            }
            return entries;
        }
    }

    private static IReadOnlyList<SecureDirectoryEntry> EnumerateUnixDirectory(
        string allowedRoot,
        string path,
        Action? afterDirectoryOpen)
    {
        using var opened = OpenUnixExisting(
            allowedRoot,
            path,
            requireRegularFile: false,
            beforeLeafOpen: null);
        if (!opened.Status.IsDirectory)
            return [];

        afterDirectoryOpen?.Invoke();
        var entries = new List<SecureDirectoryEntry>();
        foreach (var name in EnumerateUnixDirectoryNames(opened.Handle, path))
        {
            try
            {
                var fd = OpenUnixExistingEntry(
                    opened.Handle.DangerousGetHandle().ToInt32(),
                    name,
                    Path.Combine(path, name));
                if (fd < 0)
                    ThrowUnixPathError(Path.Combine(path, name));
                using var child = new SafeFileHandle(new IntPtr(fd), ownsHandle: true);
                var status = GetUnixStatus(child, Path.Combine(path, name));
                entries.Add(new SecureDirectoryEntry(name, status.IsDirectory));
            }
            catch (FileNotFoundException)
            {
            }
        }
        return entries;
    }

    private static void RemoveWindows(
        string allowedRoot,
        string path,
        bool recursive,
        Action? afterEntryOpen)
    {
        var segments = GetRelativeSegments(allowedRoot, path, allowRoot: false);
        using var parent = OpenWindowsDirectoryChain(
            allowedRoot,
            segments.AsSpan(0, segments.Length - 1),
            createMissing: false);
        using var entry = OpenWindowsRelative(
            parent,
            segments[^1],
            isDirectory: null,
            createMissing: false,
            path,
            desiredAccess: WindowsDelete | WindowsFileReadAttributes | WindowsFileListDirectory);
        var status = GetWindowsStatus(entry, path);
        afterEntryOpen?.Invoke();
        RemoveOpenedWindowsEntry(entry, status, path, recursive);
    }

    private static void RemoveOpenedWindowsEntry(
        SafeFileHandle entry,
        SecureFileStatus status,
        string path,
        bool recursive)
    {
        if (status.IsDirectory && recursive)
        {
            foreach (var childName in EnumerateWindowsDirectoryNames(entry, path))
            {
                var childPath = Path.Combine(path, childName);
                using var child = OpenWindowsRelative(
                    entry,
                    childName,
                    isDirectory: null,
                    createMissing: false,
                    childPath,
                    desiredAccess: WindowsDelete | WindowsFileReadAttributes | WindowsFileListDirectory);
                var childStatus = GetWindowsStatus(child, childPath);
                RemoveOpenedWindowsEntry(
                    child,
                    childStatus,
                    childPath,
                    recursive: true);
            }
        }

        var disposition = new WindowsFileDispositionInformation { DeleteFile = 1 };
        if (!SetFileInformationByHandle(
            entry,
            WindowsFileDispositionInfo,
            ref disposition,
            (uint)Marshal.SizeOf<WindowsFileDispositionInformation>()))
        {
            var error = Marshal.GetLastPInvokeError();
            throw new UnauthorizedAccessException(
                $"Unable to remove opened path securely: {path} (error {error})");
        }
    }

    private static void RemoveUnix(
        string allowedRoot,
        string path,
        bool recursive,
        Action? afterEntryOpen)
    {
        var segments = GetRelativeSegments(allowedRoot, path, allowRoot: false);
        using var parent = OpenUnixDirectoryChain(
            allowedRoot,
            segments.AsSpan(0, segments.Length - 1),
            createMissing: false);
        var fd = OpenUnixExistingEntry(
            parent.DangerousGetHandle().ToInt32(),
            segments[^1],
            path);
        if (fd < 0)
            ThrowUnixPathError(path);
        using var entry = new SafeFileHandle(new IntPtr(fd), ownsHandle: true);
        var status = GetUnixStatus(entry, path);
        afterEntryOpen?.Invoke();
        RemoveOpenedUnixEntry(
            parent,
            segments[^1],
            entry,
            status,
            path,
            recursive);
    }

    private static void RemoveOpenedUnixEntry(
        SafeFileHandle parent,
        string name,
        SafeFileHandle entry,
        SecureFileStatus status,
        string path,
        bool recursive)
    {
        if (status.IsDirectory && recursive)
        {
            foreach (var childName in EnumerateUnixDirectoryNames(entry, path))
            {
                var childPath = Path.Combine(path, childName);
                var childFd = OpenUnixExistingEntry(
                    entry.DangerousGetHandle().ToInt32(),
                    childName,
                    childPath);
                if (childFd < 0)
                {
                    if (Marshal.GetLastPInvokeError() == UnixMissingPath)
                        continue;
                    ThrowUnixPathError(childPath);
                }
                using var child = new SafeFileHandle(new IntPtr(childFd), ownsHandle: true);
                var childStatus = GetUnixStatus(child, childPath);
                RemoveOpenedUnixEntry(
                    entry,
                    childName,
                    child,
                    childStatus,
                    childPath,
                    recursive: true);
            }
        }

        if (UnlinkAtUnix(
            parent.DangerousGetHandle().ToInt32(),
            name,
            status.IsDirectory ? UnixRemoveDirectory : 0) != 0)
        {
            var error = Marshal.GetLastPInvokeError();
            if (error == UnixMissingPath)
                return;
            ThrowUnixPathError(path, error);
        }
    }

    private static void RenameWindows(
        string sourceRoot,
        string sourcePath,
        string destinationRoot,
        string destinationPath,
        Action? afterParentsOpen)
    {
        var sourceSegments = GetRelativeSegments(sourceRoot, sourcePath, allowRoot: false);
        var destinationSegments = GetRelativeSegments(destinationRoot, destinationPath, allowRoot: false);
        using var sourceParent = OpenWindowsDirectoryChain(
            sourceRoot,
            sourceSegments.AsSpan(0, sourceSegments.Length - 1),
            createMissing: false);
        using var source = OpenWindowsRelative(
            sourceParent,
            sourceSegments[^1],
            isDirectory: null,
            createMissing: false,
            sourcePath,
            desiredAccess: WindowsDelete | WindowsFileReadAttributes);
        var sourceStatus = GetWindowsStatus(source, sourcePath);
        using var destinationParent = OpenWindowsDirectoryChain(
            destinationRoot,
            destinationSegments.AsSpan(0, destinationSegments.Length - 1),
            createMissing: true);
        afterParentsOpen?.Invoke();
        RenameOpenedWindowsEntry(
            source,
            destinationParent,
            destinationSegments[^1],
            replaceExisting: sourceStatus.IsFile,
            destinationPath);
    }

    private static void RenameOpenedWindowsEntry(
        SafeFileHandle source,
        SafeFileHandle destinationParent,
        string destinationName,
        bool replaceExisting,
        string destinationPath)
    {
        var nameBytes = Encoding.Unicode.GetBytes(destinationName);
        var rootOffset = IntPtr.Size == 8 ? 8 : 4;
        var nameLengthOffset = rootOffset + IntPtr.Size;
        var nameOffset = nameLengthOffset + sizeof(uint);
        var bufferSize = checked(nameOffset + nameBytes.Length);
        var buffer = Marshal.AllocHGlobal(bufferSize);
        try
        {
            for (var i = 0; i < nameOffset; i++)
                Marshal.WriteByte(buffer, i, 0);
            Marshal.WriteByte(buffer, replaceExisting ? (byte)1 : (byte)0);
            Marshal.WriteIntPtr(
                buffer,
                rootOffset,
                destinationParent.DangerousGetHandle());
            Marshal.WriteInt32(buffer, nameLengthOffset, nameBytes.Length);
            Marshal.Copy(nameBytes, 0, buffer + nameOffset, nameBytes.Length);
            var status = NtSetInformationFile(
                source,
                out _,
                buffer,
                (uint)bufferSize,
                WindowsNtFileRenameInformation);
            if (status < 0)
            {
                var error = RtlNtStatusToDosError(status);
                if (error == 17)
                {
                    throw new NotSupportedException(
                        $"Secure cross-volume rename is not supported: {destinationPath}");
                }
                throw new UnauthorizedAccessException(
                    $"Unable to rename opened path securely: {destinationPath} (error {error})");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static void RenameUnix(
        string sourceRoot,
        string sourcePath,
        string destinationRoot,
        string destinationPath,
        Action? afterParentsOpen)
    {
        var sourceSegments = GetRelativeSegments(sourceRoot, sourcePath, allowRoot: false);
        var destinationSegments = GetRelativeSegments(destinationRoot, destinationPath, allowRoot: false);
        using var sourceParent = OpenUnixDirectoryChain(
            sourceRoot,
            sourceSegments.AsSpan(0, sourceSegments.Length - 1),
            createMissing: false);
        using var destinationParent = OpenUnixDirectoryChain(
            destinationRoot,
            destinationSegments.AsSpan(0, destinationSegments.Length - 1),
            createMissing: true);
        var sourceFd = OpenUnixExistingEntry(
            sourceParent.DangerousGetHandle().ToInt32(),
            sourceSegments[^1],
            sourcePath);
        if (sourceFd < 0)
            ThrowUnixPathError(sourcePath);
        using var source = new SafeFileHandle(new IntPtr(sourceFd), ownsHandle: true);
        afterParentsOpen?.Invoke();
        if (RenameAtUnix(
            sourceParent.DangerousGetHandle().ToInt32(),
            sourceSegments[^1],
            destinationParent.DangerousGetHandle().ToInt32(),
            destinationSegments[^1]) != 0)
        {
            var error = Marshal.GetLastPInvokeError();
            if (error == 18)
            {
                throw new NotSupportedException(
                    $"Secure cross-device rename is not supported: {destinationPath}");
            }
            ThrowUnixPathError(destinationPath, error);
        }
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

    private static IReadOnlyList<string> EnumerateWindowsDirectoryNames(
        SafeFileHandle directory,
        string path)
    {
        const int bufferSize = 64 * 1024;
        var buffer = Marshal.AllocHGlobal(bufferSize);
        try
        {
            var names = new List<string>();
            var restartScan = true;
            while (true)
            {
                var status = NtQueryDirectoryFile(
                    directory,
                    0,
                    0,
                    0,
                    out var ioStatus,
                    buffer,
                    (uint)bufferSize,
                    WindowsFileNamesInformation,
                    returnSingleEntry: false,
                    0,
                    restartScan);
                restartScan = false;
                if (status == WindowsStatusNoMoreFiles)
                    break;
                if (status < 0)
                {
                    var error = RtlNtStatusToDosError(status);
                    throw new UnauthorizedAccessException(
                        $"Unable to enumerate opened directory securely: {path} (error {error})");
                }

                var bytesReturned = checked((int)ioStatus.Information);
                if (bytesReturned == 0)
                    break;

                var offset = 0;
                while (true)
                {
                    if (offset < 0 || offset + 12 > bytesReturned)
                    {
                        throw new UnauthorizedAccessException(
                            $"Invalid directory enumeration data returned for: {path}");
                    }

                    var entry = buffer + offset;
                    var nextOffset = Marshal.ReadInt32(entry);
                    var nameByteLength = Marshal.ReadInt32(entry, 8);
                    if (nameByteLength < 0
                        || (nameByteLength & 1) != 0
                        || offset + 12 + nameByteLength > bytesReturned)
                    {
                        throw new UnauthorizedAccessException(
                            $"Invalid directory entry returned for: {path}");
                    }

                    var name = Marshal.PtrToStringUni(entry + 12, nameByteLength / sizeof(char))
                        ?? throw new UnauthorizedAccessException(
                            $"Invalid directory entry name returned for: {path}");
                    if (name is not "." and not "..")
                        names.Add(name);

                    if (nextOffset == 0)
                        break;
                    if (nextOffset < 12 || offset + nextOffset >= bytesReturned)
                    {
                        throw new UnauthorizedAccessException(
                            $"Invalid directory entry offset returned for: {path}");
                    }
                    offset += nextOffset;
                }
            }
            return names;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static IReadOnlyList<string> EnumerateUnixDirectoryNames(
        SafeFileHandle directory,
        string path)
    {
        var supportedArchitecture =
            (OperatingSystem.IsLinux() && IntPtr.Size == 8)
            || (OperatingSystem.IsMacOS()
                && RuntimeInformation.ProcessArchitecture == Architecture.Arm64);
        if (!supportedArchitecture)
        {
            throw new NotSupportedException(
                "Secure directory enumeration requires 64-bit Linux or arm64 macOS.");
        }

        var duplicate = DuplicateFileDescriptorUnix(
            directory.DangerousGetHandle().ToInt32());
        if (duplicate < 0)
            ThrowUnixPathError(path);

        var nativeDirectory = OpenDirectoryFromFileDescriptorUnix(duplicate);
        if (nativeDirectory == 0)
        {
            var error = Marshal.GetLastPInvokeError();
            CloseFileDescriptorUnix(duplicate);
            ThrowUnixPathError(path, error);
        }

        try
        {
            var names = new List<string>();
            while (true)
            {
                Marshal.SetLastSystemError(0);
                var entry = ReadDirectoryUnix(nativeDirectory);
                if (entry == 0)
                {
                    var error = Marshal.GetLastPInvokeError();
                    if (error != 0)
                        ThrowUnixPathError(path, error);
                    break;
                }

                // Linux places d_name after ino64, off64, reclen, and type.
                // Darwin also stores namlen before type, shifting d_name by 2 bytes.
                var name = Marshal.PtrToStringUTF8(entry + UnixDirectoryEntryNameOffset)
                    ?? throw new UnauthorizedAccessException(
                        $"Invalid directory entry name returned for: {path}");
                if (name is not "." and not "..")
                    names.Add(name);
            }
            return names;
        }
        finally
        {
            CloseDirectoryUnix(nativeDirectory);
        }
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

    private static SafeFileHandle OpenWindowsDirectory(
        string path,
        uint desiredAccess = WindowsFileReadAttributes)
    {
        var handle = CreateFileWindows(
            path,
            desiredAccess,
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
            return OpenLinuxVerifiedPath(
                GetLinuxDescriptorPath(parentFd, segment),
                UnixReadOnly | UnixNonBlock | UnixNoFollow | UnixCloseOnExec,
                UnixEntryKind.RegularFileOrDirectory);
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
        try
        {
            await WriteUnixTemporaryFileAsync(
                rootHandle,
                temporaryName,
                Path.Combine(root, temporaryName),
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
            if (UnlinkAtUnix(
                rootHandle.DangerousGetHandle().ToInt32(),
                temporaryName,
                0) != 0
                && Marshal.GetLastPInvokeError() != UnixMissingPath)
            {
                ThrowUnixPathError(Path.Combine(root, temporaryName));
            }
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
        try
        {
            await WriteUnixTemporaryFileAsync(
                rootHandle,
                temporaryName,
                Path.Combine(root, temporaryName),
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
            if (UnlinkAtUnix(
                rootHandle.DangerousGetHandle().ToInt32(),
                temporaryName,
                0) != 0
                && Marshal.GetLastPInvokeError() != UnixMissingPath)
            {
                ThrowUnixPathError(Path.Combine(root, temporaryName));
            }
        }
    }

    private static async Task WriteUnixTemporaryFileAsync(
        SafeFileHandle root,
        string temporaryName,
        string displayPath,
        string content,
        CancellationToken cancellationToken)
    {
        var rawHandle = OpenAtUnixCreate(
            root.DangerousGetHandle().ToInt32(),
            temporaryName,
            UnixWriteOnly | UnixCloseOnExec | UnixCreate | UnixExclusive | UnixNoFollow,
            Convert.ToInt32("600", 8));
        if (rawHandle < 0)
            ThrowUnixPathError(displayPath);

        using var temporaryHandle = new SafeFileHandle(new IntPtr(rawHandle), ownsHandle: true);
        if (!GetUnixStatus(temporaryHandle, displayPath).IsFile)
            throw new UnauthorizedAccessException($"Temporary path is not a regular file: {displayPath}");
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

        return OpenLinuxVerifiedPath(
            GetLinuxDescriptorPath(parentFd, segment),
            UnixReadOnly | UnixNonBlock | UnixNoFollow | UnixCloseOnExec,
            UnixEntryKind.Directory);
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

        return OpenLinuxVerifiedPath(
            GetLinuxDescriptorPath(parentFd, segment),
            UnixWriteOnly | UnixAppend | UnixNonBlock | UnixNoFollow | UnixCloseOnExec,
            UnixEntryKind.RegularFile);
    }

    private static string GetLinuxDescriptorPath(int parentFd, string segment) =>
        $"/proc/self/fd/{parentFd}/{segment}";

    private static int OpenLinuxVerifiedPath(
        string path,
        int flags,
        UnixEntryKind expectedKind)
    {
        if (GetPathStatusSystemNative(path, out var before) != 0)
        {
            var error = Marshal.GetLastPInvokeError();
            Marshal.SetLastPInvokeError(error);
            return -1;
        }
        ValidateUnixEntryKind(before, expectedKind, path);

        var fd = OpenUnixPath(path, flags);
        if (fd < 0)
            return fd;

        using var handle = new SafeFileHandle(new IntPtr(fd), ownsHandle: true);
        if (GetFileStatusSystemNative(handle.DangerousGetHandle(), out var opened) != 0
            || GetPathStatusSystemNative(path, out var after) != 0)
        {
            throw new UnauthorizedAccessException($"Filesystem entry changed while opening: {path}");
        }
        ValidateUnixEntryKind(opened, expectedKind, path);
        ValidateUnixEntryKind(after, expectedKind, path);
        if (before.Device != opened.Device
            || before.Inode != opened.Inode
            || after.Device != opened.Device
            || after.Inode != opened.Inode)
        {
            throw new UnauthorizedAccessException($"Filesystem entry changed while opening: {path}");
        }

        handle.SetHandleAsInvalid();
        return fd;
    }

    private static void ValidateUnixEntryKind(
        UnixFileStatus status,
        UnixEntryKind expectedKind,
        string path)
    {
        var type = status.Mode & UnixFileTypeMask;
        if (type == UnixSymbolicLinkMode)
            throw new UnauthorizedAccessException($"Symbolic-link traversal blocked: {path}");
        if (expectedKind == UnixEntryKind.Directory
            && type == UnixRegularFileMode)
        {
            throw new FileNotFoundException($"Path component is not a directory: {path}");
        }

        var valid = expectedKind switch
        {
            UnixEntryKind.Directory => type == UnixDirectoryMode,
            UnixEntryKind.RegularFile => type == UnixRegularFileMode,
            UnixEntryKind.RegularFileOrDirectory =>
                type is UnixRegularFileMode or UnixDirectoryMode,
            _ => false,
        };
        if (!valid)
            throw new UnauthorizedAccessException($"Unsupported filesystem entry: {path}");
    }

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
        var fd = OperatingSystem.IsLinux()
            ? OpenLinuxVerifiedPath(
                root,
                UnixReadOnly | UnixNonBlock | UnixNoFollow | UnixCloseOnExec,
                UnixEntryKind.Directory)
            : OpenUnixPath(
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
    private static int UnixCreate => OperatingSystem.IsMacOS() ? 0x0200 : 0x0040;
    private static int UnixExclusive => OperatingSystem.IsMacOS() ? 0x0800 : 0x0080;
    private static int UnixNonBlock => OperatingSystem.IsMacOS() ? 0x0004 : 0x0800;
    private static int UnixNoFollow => OperatingSystem.IsMacOS() ? 0x0100 : 0x20000;
    private static int UnixCloseOnExec => OperatingSystem.IsMacOS() ? 0x1000000 : 0x80000;
    private static int UnixRemoveDirectory => OperatingSystem.IsMacOS() ? 0x0080 : 0x0200;
    private static int UnixDirectoryEntryNameOffset => OperatingSystem.IsMacOS() ? 21 : 19;

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
    private static extern int NtQueryDirectoryFile(
        SafeFileHandle fileHandle,
        nint eventHandle,
        nint apcRoutine,
        nint apcContext,
        out WindowsIoStatusBlock ioStatusBlock,
        nint fileInformation,
        uint length,
        int fileInformationClass,
        [MarshalAs(UnmanagedType.U1)] bool returnSingleEntry,
        nint fileName,
        [MarshalAs(UnmanagedType.U1)] bool restartScan);

    [DllImport("ntdll.dll")]
    private static extern int NtSetInformationFile(
        SafeFileHandle fileHandle,
        out WindowsIoStatusBlock ioStatusBlock,
        nint fileInformation,
        uint length,
        int fileInformationClass);

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

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(
        SafeFileHandle file,
        int fileInformationClass,
        ref WindowsFileDispositionInformation fileInformation,
        uint bufferSize);

    [DllImport("System.Native", EntryPoint = "SystemNative_FStat", SetLastError = true)]
    private static extern int GetFileStatusSystemNative(
        nint fileDescriptor,
        out UnixFileStatus status);

    [DllImport("System.Native", EntryPoint = "SystemNative_LStat", SetLastError = true)]
    private static extern int GetPathStatusSystemNative(
        string path,
        out UnixFileStatus status);

    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int OpenUnixPath(string path, int flags);

    [DllImport("libc", EntryPoint = "openat", SetLastError = true)]
    private static extern int OpenAtUnix(int directoryFd, string path, int flags);

    [DllImport("libc", EntryPoint = "openat", SetLastError = true)]
    private static extern int OpenAtUnixCreate(
        int directoryFd,
        string path,
        int flags,
        int mode);

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

    [DllImport("libc", EntryPoint = "unlinkat", SetLastError = true)]
    private static extern int UnlinkAtUnix(
        int directoryFd,
        string path,
        int flags);

    [DllImport("libc", EntryPoint = "fdopendir", SetLastError = true)]
    private static extern nint OpenDirectoryFromFileDescriptorUnix(int fileDescriptor);

    [DllImport("libc", EntryPoint = "readdir", SetLastError = true)]
    private static extern nint ReadDirectoryUnix(nint directory);

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

    private enum UnixEntryKind
    {
        Directory,
        RegularFile,
        RegularFileOrDirectory,
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
    private struct WindowsFileDispositionInformation
    {
        internal int DeleteFile;
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

internal sealed record SecureDirectoryEntry(
    string Name,
    bool IsDirectory);
