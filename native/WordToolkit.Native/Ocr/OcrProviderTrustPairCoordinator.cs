using System.ComponentModel;
using Microsoft.Win32.SafeHandles;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WordToolkit.Native.Ocr;

internal static class OcrProviderTrustPairCoordinator
{
    private const uint FileAttributeReparsePoint = 0x00000400;
    private const int ErrorFileNotFound = 2;
    private const int ErrorPathNotFound = 3;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint FileShareDelete = 0x00000004;
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const uint FileFlagOpenReparsePoint = 0x00200000;
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint DeleteAccess = 0x00010000;
    private const uint FileAttributeTemporary = 0x00000100;
    private const int FileAttributeTagInfo = 9;
    private const int FileDispositionInfo = 4;
    private const int FileRenameInformation = 10;
    private const uint SynchronizeAccess = 0x00100000;
    private const uint ObjectCaseInsensitive = 0x00000040;
    private const uint FileCreate = 2;
    private const uint FileWriteThrough = 0x00000002;
    private const uint FileSynchronousIoNonAlert = 0x00000020;
    private const uint FileNonDirectoryFile = 0x00000040;

    [StructLayout(LayoutKind.Sequential)]
    private struct FileAttributeTagInformation
    {
        internal uint FileAttributes;
        internal uint ReparseTag;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UnicodeString
    {
        internal ushort Length;
        internal ushort MaximumLength;
        internal IntPtr Buffer;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ObjectAttributes
    {
        internal int Length;
        internal IntPtr RootDirectory;
        internal IntPtr ObjectName;
        internal uint Attributes;
        internal IntPtr SecurityDescriptor;
        internal IntPtr SecurityQualityOfService;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoStatusBlock
    {
        internal IntPtr Status;
        internal UIntPtr Information;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile
    );

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(
        SafeFileHandle file,
        int informationClass,
        out FileAttributeTagInformation information,
        uint bufferSize
    );

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(
        SafeFileHandle file,
        StringBuilder path,
        uint pathLength,
        uint flags
    );

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumePathNameW(
        string fileName,
        StringBuilder volumePathName,
        uint bufferLength
    );

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern uint GetDriveTypeW(string rootPathName);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(
        SafeFileHandle file,
        int fileInformationClass,
        IntPtr fileInformation,
        uint bufferSize
    );

    [DllImport("ntdll.dll")]
    private static extern int NtCreateFile(
        out IntPtr fileHandle,
        uint desiredAccess,
        ref ObjectAttributes objectAttributes,
        out IoStatusBlock ioStatusBlock,
        IntPtr allocationSize,
        uint fileAttributes,
        uint shareAccess,
        uint createDisposition,
        uint createOptions,
        IntPtr eaBuffer,
        uint eaLength
    );

    [DllImport("ntdll.dll")]
    private static extern int NtSetInformationFile(
        SafeFileHandle fileHandle,
        out IoStatusBlock ioStatusBlock,
        IntPtr fileInformation,
        uint length,
        int fileInformationClass
    );

    [DllImport("ntdll.dll")]
    private static extern uint RtlNtStatusToDosError(int status);

    internal static string? LockRootOverride { get; set; }

    private static string CoordinationRoot()
    {
        var root = LockRootOverride
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WordToolkit",
                "ocr-trust-locks"
            );
        return Path.GetFullPath(root);
    }
    internal static void ValidateNoReparsePoints(string path)
    {
        var full = Path.GetFullPath(path);
        // Walk lexically; never resolve/follow a link while checking ancestors.
        for (var current = full; current is not null; current = Path.GetDirectoryName(current))
        {
            if (IsReparseOrLink(current))
                throw new OcrProviderTrustPathValidationException(current, new IOException("Reparse point detected."));
            var root = Path.GetPathRoot(current);
            if (string.Equals(Path.TrimEndingDirectorySeparator(current), Path.TrimEndingDirectorySeparator(root ?? current), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                break;
        }
    }
    private static bool IsReparseOrLink(string path)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                using var handle = CreateFileW(
                    path,
                    0,
                    FileShareRead | FileShareWrite | FileShareDelete,
                    IntPtr.Zero,
                    OpenExisting,
                    FileFlagOpenReparsePoint | FileFlagBackupSemantics,
                    IntPtr.Zero
                );
                if (handle.IsInvalid)
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error is ErrorFileNotFound or ErrorPathNotFound)
                        return false;
                    throw new Win32Exception(error);
                }
                if (!GetFileInformationByHandleEx(
                    handle,
                    FileAttributeTagInfo,
                    out var information,
                    (uint)Marshal.SizeOf<FileAttributeTagInformation>()
                ))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
                return (information.FileAttributes & FileAttributeReparsePoint) != 0;
            }

            if (File.Exists(path))
            {
                // On some Windows/.NET builds GetAttributes follows a file symlink and
                // reports only target attributes. LinkTarget is the non-following check.
                if (new FileInfo(path).LinkTarget is not null)
                    return true;
                return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
            }
            if (Directory.Exists(path))
            {
                if (new DirectoryInfo(path).LinkTarget is not null)
                    return true;
                return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
            }
            // LinkTarget is available even for dangling links and does not follow the target.
            return new FileInfo(path).LinkTarget is not null || new DirectoryInfo(path).LinkTarget is not null;
        }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
        // Inspecting a link is a security boundary. Runtime/platform-specific failures from
        // FileSystemInfo.LinkTarget must fail closed as an invalid trust path; publication,
        // lock and journal I/O happens outside this helper and keeps its ordinary failure code.
        catch (Exception exception)
        {
            throw new OcrProviderTrustPathValidationException(path, exception);
        }
    }
    internal sealed record Journal(
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("primary_path")] string PrimaryPath,
        [property: JsonPropertyName("secondary_path")] string SecondaryPath,
        [property: JsonPropertyName("primary_sha256")] string PrimarySha256,
        [property: JsonPropertyName("secondary_sha256")] string SecondarySha256,
        [property: JsonPropertyName("transaction_id")] string TransactionId);
    internal static IDisposable Acquire(string manifestPath, string storePath)
    {
        var manifest = Path.GetFullPath(manifestPath);
        var store = Path.GetFullPath(storePath);
        // Reject links before any directory creation, journal-path computation, or lock-file open.
        ValidateNoReparsePoints(manifest);
        ValidateNoReparsePoints(store);
        var identity = (OperatingSystem.IsWindows() ? manifest.ToUpperInvariant() : manifest)
            + "\n" + (OperatingSystem.IsWindows() ? store.ToUpperInvariant() : store);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
        var dir = CoordinationRoot();
        Directory.CreateDirectory(dir);
        ValidateNoReparsePoints(dir);
        var path = Path.Combine(dir, hash + ".lock");
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (true)
        {
            try { return new Lease(new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)); }
            catch (IOException) when (DateTime.UtcNow < deadline) { Thread.Sleep(25); }
        }
    }

    internal static StableOutputDirectoryLease AcquireStableOutputDirectories(
        string primaryPath,
        string secondaryPath,
        Func<string, DriveType>? driveTypeResolver = null
    )
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException(
                "OCR trust publication directory leases require Windows."
            );

        var paths = new[] { Path.GetFullPath(primaryPath), Path.GetFullPath(secondaryPath) };
        foreach (var path in paths)
            ValidateNoReparsePoints(path);
        var primaryDirectory = Path.GetDirectoryName(paths[0])
            ?? throw new OcrProviderTrustPathValidationException(
                primaryPath,
                new IOException("The output directory is unavailable.")
            );
        var secondaryDirectory = Path.GetDirectoryName(paths[1])
            ?? throw new OcrProviderTrustPathValidationException(
                secondaryPath,
                new IOException("The output directory is unavailable.")
            );
        if (!string.Equals(
            primaryDirectory,
            secondaryDirectory,
            StringComparison.OrdinalIgnoreCase
        ))
        {
            throw new OcrProviderTrustPathValidationException(
                primaryPath,
                new IOException("The output pair must share one verified directory.")
            );
        }

        SafeFileHandle? handle = null;
        try
        {
            handle = CreateFileW(
                primaryDirectory,
                GenericRead,
                FileShareRead | FileShareWrite | FileShareDelete,
                IntPtr.Zero,
                OpenExisting,
                FileFlagOpenReparsePoint | FileFlagBackupSemantics,
                IntPtr.Zero
            );
            if (handle.IsInvalid)
            {
                var error = Marshal.GetLastWin32Error();
                throw new Win32Exception(error);
            }
            if (!GetFileInformationByHandleEx(
                handle,
                FileAttributeTagInfo,
                out var information,
                (uint)Marshal.SizeOf<FileAttributeTagInformation>()
            ) || (information.FileAttributes & FileAttributeReparsePoint) != 0)
            {
                var error = Marshal.GetLastWin32Error();
                throw new Win32Exception(error == 0 ? 4390 : error);
            }
            var finalDirectory = FinalPath(handle);
            var volume = new StringBuilder(32_768);
            if (!GetVolumePathNameW(finalDirectory, volume, (uint)volume.Capacity)
                || (driveTypeResolver?.Invoke(volume.ToString())
                    ?? (DriveType)GetDriveTypeW(volume.ToString())) == DriveType.Network)
            {
                throw new IOException("The resolved OCR trust output filesystem is not local.");
            }
            foreach (var path in paths)
                ValidateNoReparsePoints(path);
            return new StableOutputDirectoryLease(
                handle,
                primaryDirectory,
                finalDirectory
            );
        }
        catch (Exception exception)
        {
            handle?.Dispose();
            throw exception is OcrProviderTrustPathValidationException
                ? exception
                : new OcrProviderTrustPathValidationException(primaryPath, exception);
        }
    }

    internal static string JournalPath(string primaryPath, string secondaryPath) =>
        Path.Combine(CoordinationRoot(), PairHash(primaryPath, secondaryPath) + ".journal.json");
    internal static void Recover(string primaryPath, string secondaryPath) => Recover(primaryPath, secondaryPath, null);
    internal static void Recover(string primaryPath, string secondaryPath, Func<byte[], byte[], bool>? validator)
    {
        var journalPath = JournalPath(primaryPath, secondaryPath);
        if (!File.Exists(journalPath)) return;
        Journal? journal;
        try { journal = JsonSerializer.Deserialize<Journal>(File.ReadAllText(journalPath, Encoding.UTF8)); }
        catch { throw new IOException("OCR provider trust recovery journal is invalid."); }
        if (journal is null || string.IsNullOrWhiteSpace(journal.Kind) || !Same(journal.PrimaryPath, primaryPath) || !Same(journal.SecondaryPath, secondaryPath))
            throw new IOException("OCR provider trust recovery journal does not match the pair.");
        var primary = File.Exists(primaryPath); var secondary = File.Exists(secondaryPath);
        if (!primary && !secondary) { File.Delete(journalPath); return; }
        if (!primary && secondary)
        {
            throw new IOException("OCR provider trust recovery is incomplete; public files are preserved.");
        }
        if (primary && secondary) { var pb=File.ReadAllBytes(primaryPath); var sb=File.ReadAllBytes(secondaryPath); if(!FixedEquals(HashFile(primaryPath),journal.PrimarySha256)||!FixedEquals(HashFile(secondaryPath),journal.SecondarySha256)||(validator is not null&&!validator(pb,sb))) throw new IOException("OCR provider trust recovery hashes or cryptographic validation do not match the journal."); File.Delete(journalPath); return; }
        throw new IOException("OCR provider trust recovery is incomplete; public files are preserved.");
    }
    internal static void WriteJournal(string primaryPath, string secondaryPath, byte[] secondaryBytes, string transactionId)
    {
        Directory.CreateDirectory(CoordinationRoot());
        var path = JournalPath(primaryPath, secondaryPath);
        var journal = new Journal("pair", Path.GetFullPath(primaryPath), Path.GetFullPath(secondaryPath), HashFile(primaryPath, allowMissing:true), Convert.ToHexString(SHA256.HashData(secondaryBytes)).ToLowerInvariant(), transactionId);
        var json = JsonSerializer.Serialize(journal) + "\n";
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough);
        var bytes = Encoding.UTF8.GetBytes(json); stream.Write(bytes); stream.Flush(true);
    }
    internal static void WriteJournal(string primaryPath, string secondaryPath, byte[] secondaryBytes, string transactionId, string kind, byte[] primaryBytes)
    {
        Directory.CreateDirectory(CoordinationRoot());
        var path = JournalPath(primaryPath, secondaryPath);
        var journal = new Journal(kind, Path.GetFullPath(primaryPath), Path.GetFullPath(secondaryPath), Convert.ToHexString(SHA256.HashData(primaryBytes)).ToLowerInvariant(), Convert.ToHexString(SHA256.HashData(secondaryBytes)).ToLowerInvariant(), transactionId);
        var json = JsonSerializer.Serialize(journal) + "\n";
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough);
        var bytes = Encoding.UTF8.GetBytes(json); stream.Write(bytes); stream.Flush(true);
    }
    internal static void DeleteJournal(string primaryPath, string secondaryPath) => TryDelete(JournalPath(primaryPath, secondaryPath));
    private static string PairHash(string primaryPath, string secondaryPath)
    {
        var identity = (OperatingSystem.IsWindows() ? Path.GetFullPath(primaryPath).ToUpperInvariant() : Path.GetFullPath(primaryPath)) + "\n" + (OperatingSystem.IsWindows() ? Path.GetFullPath(secondaryPath).ToUpperInvariant() : Path.GetFullPath(secondaryPath));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
    }
    private static bool Same(string a, string b) => string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    private static string HashFile(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    private static string HashFile(string path, bool allowMissing) => File.Exists(path) ? HashFile(path) : "";
    private static bool FixedEquals(string a,string b) => a.Length==b.Length && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(a),Encoding.ASCII.GetBytes(b));
    private static void TryDelete(string path) { try { File.Delete(path); } catch { } }
    private sealed class Lease(FileStream stream) : IDisposable
    {
        public void Dispose() => stream.Dispose();
    }

    internal sealed class StableOutputDirectoryLease : IDisposable
    {
        private readonly SafeFileHandle _directoryHandle;
        private readonly string _lexicalDirectory;
        private readonly string _verifiedFinalDirectory;
        private bool _disposed;

        internal StableOutputDirectoryLease(
            SafeFileHandle directoryHandle,
            string lexicalDirectory,
            string verifiedFinalDirectory
        )
        {
            _directoryHandle = directoryHandle;
            _lexicalDirectory = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(lexicalDirectory)
            );
            _verifiedFinalDirectory = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(verifiedFinalDirectory)
            );
        }

        internal void PublishCreateNew(string destinationPath, ReadOnlySpan<byte> bytes)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var destination = Path.GetFullPath(destinationPath);
            var destinationDirectory = Path.GetDirectoryName(destination)
                ?? throw new OcrProviderTrustPathValidationException(
                    destinationPath,
                    new IOException("The output directory is unavailable.")
                );
            if (!SamePath(destinationDirectory, _lexicalDirectory))
            {
                throw new OcrProviderTrustPathValidationException(
                    destinationPath,
                    new IOException("The output does not belong to the verified directory.")
                );
            }

            EnsureDirectoryBinding();
            SafeFileHandle? stageHandle = null;
            var published = false;
            try
            {
                var stageName = "." + Path.GetFileName(destination) + "."
                    + Guid.NewGuid().ToString("N") + ".tmp";
                stageHandle = CreateRelativeFile(
                    stageName,
                    GenericRead | GenericWrite | DeleteAccess | SynchronizeAccess
                );

                RandomAccess.Write(stageHandle, bytes, 0);
                RandomAccess.FlushToDisk(stageHandle);
                EnsureHandleIsInVerifiedDirectory(stageHandle);
                EnsureDirectoryBinding();
                RenameRelativeToVerifiedDirectory(
                    stageHandle,
                    Path.GetFileName(destination)
                );
                EnsureDirectoryBinding();
                var finalPublishedPath = FinalPath(stageHandle);
                var expectedPublishedPath = Path.Combine(
                    _verifiedFinalDirectory,
                    Path.GetFileName(destination)
                );
                if (!SamePath(finalPublishedPath, expectedPublishedPath))
                {
                    throw new IOException(
                        "The published OCR trust output escaped the verified directory binding."
                    );
                }
                published = true;
            }
            catch (Exception exception)
            {
                throw exception is OcrProviderTrustPathValidationException
                    ? exception
                    : new OcrProviderTrustPathValidationException(destinationPath, exception);
            }
            finally
            {
                if (!published && stageHandle is { IsInvalid: false })
                    DeleteByHandle(stageHandle);
                stageHandle?.Dispose();
            }
        }

        private void EnsureDirectoryBinding()
        {
            ValidateNoReparsePoints(_lexicalDirectory);
            if (!SamePath(FinalPath(_directoryHandle), _verifiedFinalDirectory))
            {
                throw new IOException(
                    "The verified OCR trust output directory was renamed during publication."
                );
            }

            using var currentHandle = CreateFileW(
                _lexicalDirectory,
                GenericRead,
                FileShareRead | FileShareWrite | FileShareDelete,
                IntPtr.Zero,
                OpenExisting,
                FileFlagOpenReparsePoint | FileFlagBackupSemantics,
                IntPtr.Zero
            );
            if (currentHandle.IsInvalid)
                throw new Win32Exception(Marshal.GetLastWin32Error());
            if (!GetFileInformationByHandleEx(
                currentHandle,
                FileAttributeTagInfo,
                out var information,
                (uint)Marshal.SizeOf<FileAttributeTagInformation>()
            ) || (information.FileAttributes & FileAttributeReparsePoint) != 0)
            {
                var error = Marshal.GetLastWin32Error();
                throw new Win32Exception(error == 0 ? 4390 : error);
            }
            if (!SamePath(FinalPath(currentHandle), _verifiedFinalDirectory))
            {
                throw new IOException(
                    "The OCR trust output namespace no longer identifies the verified directory."
                );
            }
        }

        private void EnsureHandleIsInVerifiedDirectory(SafeFileHandle handle)
        {
            var stageDirectory = Path.GetDirectoryName(FinalPath(handle));
            if (stageDirectory is null || !SamePath(stageDirectory, _verifiedFinalDirectory))
            {
                throw new IOException(
                    "The staged OCR trust output escaped the verified directory binding."
                );
            }
        }

        private void RenameRelativeToVerifiedDirectory(
            SafeFileHandle stageHandle,
            string destinationFileName
        )
        {
            var nameBytes = Encoding.Unicode.GetBytes(destinationFileName);
            var rootOffset = IntPtr.Size == 8 ? 8 : 4;
            var lengthOffset = rootOffset + IntPtr.Size;
            var nameOffset = lengthOffset + sizeof(int);
            var bufferSize = nameOffset + nameBytes.Length;
            var buffer = Marshal.AllocHGlobal(bufferSize);
            var directoryHandleAdded = false;
            try
            {
                for (var offset = 0; offset < bufferSize; offset++)
                    Marshal.WriteByte(buffer, offset, 0);
                _directoryHandle.DangerousAddRef(ref directoryHandleAdded);
                Marshal.WriteIntPtr(
                    buffer,
                    rootOffset,
                    _directoryHandle.DangerousGetHandle()
                );
                Marshal.WriteInt32(buffer, lengthOffset, nameBytes.Length);
                Marshal.Copy(nameBytes, 0, IntPtr.Add(buffer, nameOffset), nameBytes.Length);
                var status = NtSetInformationFile(
                    stageHandle,
                    out _,
                    buffer,
                    (uint)bufferSize,
                    FileRenameInformation
                );
                ThrowIfNtFailed(status);
            }
            finally
            {
                if (directoryHandleAdded)
                    _directoryHandle.DangerousRelease();
                Marshal.FreeHGlobal(buffer);
            }
        }

        private SafeFileHandle CreateRelativeFile(string fileName, uint desiredAccess)
        {
            var nameBytes = Encoding.Unicode.GetBytes(fileName);
            var nameBuffer = Marshal.AllocHGlobal(nameBytes.Length + sizeof(char));
            var unicodeStringBuffer = Marshal.AllocHGlobal(Marshal.SizeOf<UnicodeString>());
            var directoryHandleAdded = false;
            try
            {
                Marshal.Copy(nameBytes, 0, nameBuffer, nameBytes.Length);
                Marshal.WriteInt16(nameBuffer, nameBytes.Length, 0);
                var unicodeString = new UnicodeString
                {
                    Length = checked((ushort)nameBytes.Length),
                    MaximumLength = checked((ushort)(nameBytes.Length + sizeof(char))),
                    Buffer = nameBuffer,
                };
                Marshal.StructureToPtr(unicodeString, unicodeStringBuffer, false);
                _directoryHandle.DangerousAddRef(ref directoryHandleAdded);
                var attributes = new ObjectAttributes
                {
                    Length = Marshal.SizeOf<ObjectAttributes>(),
                    RootDirectory = _directoryHandle.DangerousGetHandle(),
                    ObjectName = unicodeStringBuffer,
                    Attributes = ObjectCaseInsensitive,
                };
                var status = NtCreateFile(
                    out var rawHandle,
                    desiredAccess,
                    ref attributes,
                    out _,
                    IntPtr.Zero,
                    FileAttributeTemporary,
                    FileShareRead,
                    FileCreate,
                    FileWriteThrough | FileSynchronousIoNonAlert | FileNonDirectoryFile,
                    IntPtr.Zero,
                    0
                );
                ThrowIfNtFailed(status);
                return new SafeFileHandle(rawHandle, ownsHandle: true);
            }
            finally
            {
                if (directoryHandleAdded)
                    _directoryHandle.DangerousRelease();
                Marshal.FreeHGlobal(unicodeStringBuffer);
                Marshal.FreeHGlobal(nameBuffer);
            }
        }

        private static void DeleteByHandle(SafeFileHandle handle)
        {
            var buffer = Marshal.AllocHGlobal(1);
            try
            {
                Marshal.WriteByte(buffer, 0, 1);
                SetFileInformationByHandle(handle, FileDispositionInfo, buffer, 1);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private static void ThrowIfNtFailed(int status)
        {
            if (status < 0)
                throw new Win32Exception(checked((int)RtlNtStatusToDosError(status)));
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _directoryHandle.Dispose();
        }
    }

    private static string FinalPath(SafeFileHandle handle)
    {
        var capacity = 512;
        while (true)
        {
            var buffer = new StringBuilder(capacity);
            var length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Capacity, 0);
            if (length == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error());
            if (length < buffer.Capacity)
            {
                var path = buffer.ToString();
                if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
                    path = @"\\" + path[8..];
                else if (path.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase))
                    path = path[4..];
                return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            }
            capacity = checked((int)length + 1);
        }
    }

    private static bool SamePath(string left, string right) => string.Equals(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal
    );
}
