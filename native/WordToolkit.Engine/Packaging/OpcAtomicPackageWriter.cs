namespace WordToolkit.Engine.Packaging;

public sealed record OpcAtomicWriteOptions
{
    public string? ExpectedDestinationFingerprint { get; init; }

    public string? ExpectedResultFingerprint { get; init; }

    public OpcSerializationMode SerializationMode { get; init; } =
        OpcSerializationMode.Preserve;

    public bool AllowStructuralErrors { get; init; }

    public bool KeepBackup { get; init; }
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

    public OpcAtomicPackageWriter(
        OpcPackageReader? reader = null,
        OpcPackageSerializer? serializer = null
    )
    {
        _reader = reader ?? new OpcPackageReader();
        _serializer = serializer ?? new OpcPackageSerializer();
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

        var fileName = Path.GetFileName(destination);
        var transactionId = Guid.NewGuid().ToString("N");
        var temporaryPath = Path.Combine(
            directory,
            $".{fileName}.wordtoolkit-{transactionId}.tmp"
        );
        var backupPath = Path.Combine(
            directory,
            $".{fileName}.wordtoolkit-{transactionId}.bak"
        );
        var lockPath = destination + ".wordtoolkit.lock";
        string? retainedBackup = null;

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
            AssertDestinationVersion(destination, expectedFingerprint);

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

            AssertDestinationVersion(destination, expectedFingerprint);
            if (File.Exists(destination))
            {
                File.Replace(
                    temporaryPath,
                    destination,
                    backupPath,
                    ignoreMetadataErrors: false
                );
                retainedBackup = backupPath;
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
            else
            {
                File.Move(temporaryPath, destination);
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
            if (!options.KeepBackup && retainedBackup is null)
            {
                TryDelete(backupPath);
            }

            lockStream.Dispose();
            TryDelete(lockPath);
        }
    }

    private void AssertDestinationVersion(string destination, string expectedFingerprint)
    {
        if (!File.Exists(destination))
        {
            return;
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
