using System.Security.Cryptography;
using WordToolkit.Engine.Rendering;
using WordToolkit.Engine.Publishing;

namespace WordToolkit.Engine.Packaging;

public sealed record OpcAtomicWriteOptions
{
    public string? ExpectedDestinationFingerprint { get; init; }

    public string? ExpectedResultFingerprint { get; init; }

    public OpcSerializationMode SerializationMode { get; init; } =
        OpcSerializationMode.Preserve;

    public bool AllowStructuralErrors { get; init; }

    public bool KeepBackup { get; init; }

    public bool RequireNewDestination { get; init; }
}

public sealed record OpcAtomicWriteResult(
    string DestinationPath,
    string Fingerprint,
    string? BackupPath,
    IReadOnlyCollection<string> ChangedEntryNames,
    IReadOnlyList<OpcDiagnostic> Diagnostics
);

public sealed class OpcAtomicPackageWriter
{
    private readonly OpcPackageReader _reader;
    private readonly OpcPackageSerializer _serializer;
    private readonly Action<string>? _beforeAtomicReplace;
    private readonly Action<string>? _beforeCompensatingReplace;

    public OpcAtomicPackageWriter(
        OpcPackageReader? reader = null,
        OpcPackageSerializer? serializer = null
    ) : this(reader, serializer, beforeAtomicReplace: null)
    {
    }

    internal OpcAtomicPackageWriter(
        OpcPackageReader? reader,
        OpcPackageSerializer? serializer,
        Action<string>? beforeAtomicReplace,
        Action<string>? beforeCompensatingReplace = null
    )
    {
        _reader = reader ?? new OpcPackageReader();
        _serializer = serializer ?? new OpcPackageSerializer();
        _beforeAtomicReplace = beforeAtomicReplace;
        _beforeCompensatingReplace = beforeCompensatingReplace;
    }

    public OpcAtomicWriteResult Write(
        string destinationPath,
        OpcPackageMutationBuilder mutation,
        OpcAtomicWriteOptions? options = null
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentNullException.ThrowIfNull(mutation);
        options ??= new OpcAtomicWriteOptions();

        var destination = Path.GetFullPath(destinationPath);
        var directory = Path.GetDirectoryName(destination)
            ?? throw new ArgumentException(
                "Destination path has no parent directory.",
                nameof(destinationPath)
            );
        Directory.CreateDirectory(directory);

        var transactionId = Guid.NewGuid().ToString("N");
        var temporaryPath = Path.Combine(
            directory,
            $".wordtoolkit-{transactionId}.tmp"
        );
        var backupPath = Path.Combine(
            directory,
            $".wordtoolkit-{transactionId}.bak"
        );
        var displacedCandidatePath = Path.Combine(
            directory,
            $".wordtoolkit-{transactionId}.conflict"
        );
        var lockPath = destination + ".wordtoolkit.lock";
        string? retainedBackup = null;
        var retainedRecoveryPaths = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase
        );

        using var lockStream = new FileStream(
            lockPath,
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None,
            bufferSize: 1,
            FileOptions.None
        );

        try
        {
            var expectedFingerprint = options.ExpectedDestinationFingerprint
                ?? mutation.BaseFingerprint;
            AssertDestinationVersion(
                destination,
                expectedFingerprint,
                options.RequireNewDestination
            );

            using (
                var output = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 128 * 1024,
                    FileOptions.WriteThrough
                )
            )
            {
                _serializer.Write(output, mutation, options.SerializationMode);
                output.Flush(flushToDisk: true);
            }

            var candidate = _reader.Read(temporaryPath);
            if (!options.AllowStructuralErrors && !candidate.IsStructurallyValid)
            {
                throw new OpcPackageValidationException(candidate.Diagnostics);
            }

            if (
                options.ExpectedResultFingerprint is { } expectedResultFingerprint
                && !string.Equals(
                    candidate.Fingerprint,
                    expectedResultFingerprint,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                throw new OpcPackageResultMismatchException(
                    $"Candidate package fingerprint differs from the planned result: "
                        + $"expected '{expectedResultFingerprint}', actual "
                        + $"'{candidate.Fingerprint}'."
                );
            }
            var candidateFileHash = HashFile(temporaryPath);

            AssertDestinationVersion(
                destination,
                expectedFingerprint,
                options.RequireNewDestination
            );
            if (options.RequireNewDestination)
            {
                _beforeAtomicReplace?.Invoke(destination);
                try
                {
                    AtomicFilePublisher.PublishCreateNew(
                        temporaryPath,
                        destination
                    );
                }
                catch (IOException exception) when (
                    AtomicFilePublisher.IsAlreadyExists(exception)
                )
                {
                    throw new OpcPackageConcurrencyException(
                        "The destination was created while the package was being written."
                    );
                }
            }
            else
            {
                _beforeAtomicReplace?.Invoke(destination);
                try
                {
                    File.Replace(
                        temporaryPath,
                        destination,
                        backupPath,
                        ignoreMetadataErrors: false
                    );
                }
                catch (IOException) when (!File.Exists(destination))
                {
                    throw new OpcPackageConcurrencyException(
                        "The destination was removed immediately before atomic replacement."
                    );
                }
                retainedBackup = backupPath;
                if (!FingerprintMatches(backupPath, expectedFingerprint))
                {
                    try
                    {
                        RestoreConcurrentDestination(
                            destination,
                            backupPath,
                            displacedCandidatePath,
                            candidateFileHash
                        );
                    }
                    catch (OpcPackageRecoveryException recovery)
                    {
                        foreach (var recoveryPath in recovery.RecoveryPaths)
                        {
                            retainedRecoveryPaths.Add(recoveryPath);
                        }
                        retainedBackup = recovery.RecoveryPath;
                        throw;
                    }
                    retainedBackup = null;
                    throw new OpcPackageConcurrencyException(
                        "The destination changed immediately before atomic replacement; the external version was restored."
                    );
                }
                if (!options.KeepBackup)
                {
                    try
                    {
                        File.Delete(backupPath);
                        retainedBackup = null;
                    }
                    catch (IOException)
                    {
                        // The transaction succeeded. Keeping a recovery file is safer
                        // than reporting a false write failure after replacement.
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // Same reasoning as above: report the retained recovery path.
                    }
                }
            }
            return new OpcAtomicWriteResult(
                destination,
                candidate.Fingerprint,
                retainedBackup,
                mutation.ChangedEntryNames,
                candidate.Diagnostics
            );
        }
        finally
        {
            TryDelete(temporaryPath);
            if (!retainedRecoveryPaths.Contains(displacedCandidatePath))
            {
                TryDelete(displacedCandidatePath);
            }
            if (
                !options.KeepBackup
                && retainedBackup is null
                && !retainedRecoveryPaths.Contains(backupPath)
            )
            {
                TryDelete(backupPath);
            }

            lockStream.Dispose();
            TryDelete(lockPath);
        }
    }

    private bool FingerprintMatches(string path, string expectedFingerprint)
    {
        try
        {
            return string.Equals(
                _reader.Read(path).Fingerprint,
                expectedFingerprint,
                StringComparison.OrdinalIgnoreCase
            );
        }
        catch (Exception exception) when (
            exception is IOException
                or InvalidDataException
                or UnauthorizedAccessException
        )
        {
            return false;
        }
    }

    private void RestoreConcurrentDestination(
        string destination,
        string backupPath,
        string displacedCandidatePath,
        byte[] expectedCandidateFileHash
    )
    {
        try
        {
            var externalHash = HashFile(backupPath);
            _beforeCompensatingReplace?.Invoke(destination);
            File.Replace(
                backupPath,
                destination,
                displacedCandidatePath,
                ignoreMetadataErrors: false
            );
            var displacedHash = HashFile(displacedCandidatePath);
            var restoredHash = HashFile(destination);
            if (!displacedHash.AsSpan().SequenceEqual(expectedCandidateFileHash))
            {
                throw new OpcPackageRecoveryException(
                    "A newer concurrent destination was displaced during compensation and was retained as a sibling recovery artifact.",
                    [displacedCandidatePath],
                    new IOException(
                        "The displaced file is not the WordToolkit candidate."
                    )
                );
            }
            if (!restoredHash.AsSpan().SequenceEqual(externalHash))
            {
                throw new OpcPackageRecoveryException(
                    "Concurrent destination recovery did not restore the displaced bytes; the displaced candidate was retained as a sibling recovery artifact.",
                    [displacedCandidatePath],
                    new IOException(
                        "The restored destination does not match the displaced package."
                    )
                );
            }
        }
        catch (OpcPackageRecoveryException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException
        )
        {
            var recoveryPaths = ExistingRecoveryPaths(
                backupPath,
                displacedCandidatePath
            );
            throw new OpcPackageRecoveryException(
                recoveryPaths.Count > 0
                    ? "The destination changed during atomic commit and automatic recovery failed; one or more sibling recovery artifacts were retained."
                    : "The destination changed during atomic commit and automatic recovery failed; no recovery artifact was available.",
                recoveryPaths,
                exception
            );
        }
    }

    private static IReadOnlyList<string> ExistingRecoveryPaths(
        string backupPath,
        string displacedCandidatePath
    )
    {
        var paths = new List<string>(capacity: 2);
        if (File.Exists(backupPath))
        {
            paths.Add(backupPath);
        }
        if (File.Exists(displacedCandidatePath))
        {
            paths.Add(displacedCandidatePath);
        }
        return paths;
    }

    private static byte[] HashFile(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 128 * 1024,
            FileOptions.SequentialScan
        );
        return SHA256.HashData(stream);
    }

    private void AssertDestinationVersion(
        string destination,
        string expectedFingerprint,
        bool requireNewDestination
    )
    {
        if (!File.Exists(destination))
        {
            if (requireNewDestination)
            {
                return;
            }
            throw new OpcPackageConcurrencyException(
                "The destination no longer exists."
            );
        }

        if (requireNewDestination)
        {
            throw new OpcPackageConcurrencyException(
                "The destination already exists and overwrite is forbidden."
            );
        }

        var current = _reader.Read(destination);
        if (
            !string.Equals(
                current.Fingerprint,
                expectedFingerprint,
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            throw new OpcPackageConcurrencyException(
                $"Destination changed: expected package fingerprint "
                    + $"'{expectedFingerprint}', actual '{current.Fingerprint}'."
            );
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

public sealed class OpcPackageConcurrencyException : IOException
{
    public OpcPackageConcurrencyException(string message)
        : base(message)
    {
    }
}

public sealed class OpcPackageRecoveryException : IOException
{
    public OpcPackageRecoveryException(
        string message,
        IReadOnlyList<string> recoveryPaths,
        Exception innerException
    ) : base(message, innerException)
    {
        ArgumentNullException.ThrowIfNull(recoveryPaths);
        RecoveryPaths = recoveryPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToArray();
    }

    public IReadOnlyList<string> RecoveryPaths { get; }

    public string? RecoveryPath => RecoveryPaths.FirstOrDefault();
}

public sealed class OpcPackageValidationException : IOException
{
    public OpcPackageValidationException(IReadOnlyList<OpcDiagnostic> diagnostics)
        : base(BuildMessage(diagnostics))
    {
        Diagnostics = diagnostics;
    }

    public IReadOnlyList<OpcDiagnostic> Diagnostics { get; }

    private static string BuildMessage(IReadOnlyList<OpcDiagnostic> diagnostics)
    {
        var errors = diagnostics
            .Where(diagnostic => diagnostic.Severity is OpcDiagnosticSeverity.Error
                or OpcDiagnosticSeverity.Fatal)
            .Take(5)
            .Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}")
            .ToArray();
        return errors.Length == 0
            ? "Candidate package failed structural validation."
            : "Candidate package failed structural validation: " + string.Join(" | ", errors);
    }
}

public sealed class OpcPackageResultMismatchException : IOException
{
    public OpcPackageResultMismatchException(string message)
        : base(message)
    {
    }
}
