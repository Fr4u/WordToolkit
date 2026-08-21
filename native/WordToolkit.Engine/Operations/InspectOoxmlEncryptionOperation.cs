using WordToolkit.Engine.Packaging;

namespace WordToolkit.Engine.Operations;

public static class InspectOoxmlEncryptionContract
{
    public const string OperationName = "inspect_ooxml_encryption";
    public const string Contract = "wordtoolkit.inspect_ooxml_encryption/1.0";
    public const int MaximumFileNameCharacters = 512;
    public const int MaximumLocalPathCharacters = 32_767;
}

public sealed record InspectOoxmlEncryptionRequest(string LocalPath);

public sealed record InspectOoxmlEncryptionSecurity(
    bool AcceptsPassword,
    bool DecryptsContent,
    bool ReturnsDocumentContent,
    bool ReturnsStreamNames,
    bool ReturnsPaths,
    bool OpensWord,
    bool UsesNetwork,
    int EncryptionInfoBytesReadMaximum
);

public sealed record InspectOoxmlEncryptionResult(
    string OperationContract,
    string FileName,
    long Bytes,
    string ContainerKind,
    string EncryptionState,
    bool IsEncryptedOoxml,
    bool CompleteEncryptionContainer,
    bool HasEncryptionInfoStream,
    bool HasEncryptedPackageStream,
    bool HasDataSpacesStorage,
    string EncryptionInfoVariant,
    int? EncryptionInfoMajor,
    int? EncryptionInfoMinor,
    int? CompoundFileMajorVersion,
    int? SectorSize,
    int DirectoryEntryCount,
    int RootChildCount,
    IReadOnlyList<string> IssueCodes,
    InspectOoxmlEncryptionSecurity Security
);

/// <summary>
/// Detects an ECMA-376 encrypted OOXML container without opening Word, accepting a
/// password, decrypting the package or returning file paths, stream names or content.
/// </summary>
public sealed class InspectOoxmlEncryptionOperation
{
    private static readonly HashSet<string> SupportedExtensions = new(
        [
            ".docx",
            ".docm",
            ".dotx",
            ".dotm",
            ".xlsx",
            ".xlsm",
            ".xltx",
            ".xltm",
            ".xlam",
            ".pptx",
            ".pptm",
            ".potx",
            ".potm",
            ".ppsx",
            ".ppsm",
            ".ppam",
            ".thmx",
        ],
        StringComparer.OrdinalIgnoreCase
    );

    private readonly OoxmlEncryptionInspector _inspector;

    public InspectOoxmlEncryptionOperation(OoxmlEncryptionInspectionLimits? limits = null)
    {
        _inspector = new OoxmlEncryptionInspector(limits);
    }

    public InspectOoxmlEncryptionResult Execute(
        InspectOoxmlEncryptionRequest request,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        var path = ResolvePath(request.LocalPath);
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return ExecuteCore(stream, Path.GetFileName(path), stream.Length, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (WordToolkitOperationException)
        {
            throw;
        }
        catch (OoxmlEncryptionInspectionLimitException exception)
        {
            throw new WordToolkitOperationException(
                "ENCRYPTION_INSPECTION_LIMIT",
                "The file exceeds a bounded encryption-inspection limit",
                innerException: exception
            );
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new WordToolkitOperationException(
                "ACCESS_DENIED",
                "The file cannot be read with current permissions",
                innerException: exception
            );
        }
        catch (FileNotFoundException exception)
        {
            throw new WordToolkitOperationException(
                "NOT_FOUND",
                "The requested OOXML file does not exist",
                innerException: exception
            );
        }
        catch (DirectoryNotFoundException exception)
        {
            throw new WordToolkitOperationException(
                "NOT_FOUND",
                "The requested OOXML file does not exist",
                innerException: exception
            );
        }
        catch (IOException exception)
        {
            throw new WordToolkitOperationException(
                "IO_ERROR",
                "The file could not be inspected for OOXML encryption",
                retryable: true,
                innerException: exception
            );
        }
    }

    public InspectOoxmlEncryptionResult Execute(
        Stream stream,
        string fileName,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(stream);
        ValidateFileName(fileName, requireLeafName: true);
        if (!stream.CanRead || !stream.CanSeek)
        {
            throw InvalidInput("File stream must be readable and seekable");
        }
        var originalPosition = stream.Position;
        try
        {
            stream.Position = 0;
            return ExecuteCore(stream, fileName, stream.Length, cancellationToken);
        }
        catch (OoxmlEncryptionInspectionLimitException exception)
        {
            throw new WordToolkitOperationException(
                "ENCRYPTION_INSPECTION_LIMIT",
                "The file exceeds a bounded encryption-inspection limit",
                innerException: exception
            );
        }
        finally
        {
            try
            {
                stream.Position = originalPosition;
            }
            catch (Exception)
            {
                // A hostile stream must not replace the inspection result or primary failure.
            }
        }
    }

    private InspectOoxmlEncryptionResult ExecuteCore(
        Stream stream,
        string fileName,
        long bytes,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var probe = _inspector.Inspect(stream, cancellationToken);
        return new InspectOoxmlEncryptionResult(
            InspectOoxmlEncryptionContract.Contract,
            fileName,
            bytes,
            probe.ContainerKind,
            probe.EncryptionState,
            probe.IsEncryptedOoxml,
            probe.CompleteEncryptionContainer,
            probe.HasEncryptionInfoStream,
            probe.HasEncryptedPackageStream,
            probe.HasDataSpacesStorage,
            probe.EncryptionInfoVariant,
            probe.EncryptionInfoMajor,
            probe.EncryptionInfoMinor,
            probe.CompoundFileMajorVersion,
            probe.SectorSize,
            probe.DirectoryEntryCount,
            probe.RootChildCount,
            probe.IssueCodes,
            new InspectOoxmlEncryptionSecurity(
                AcceptsPassword: false,
                DecryptsContent: false,
                ReturnsDocumentContent: false,
                ReturnsStreamNames: false,
                ReturnsPaths: false,
                OpensWord: false,
                UsesNetwork: false,
                EncryptionInfoBytesReadMaximum: 8
            )
        );
    }

    private static string ResolvePath(string localPath)
    {
        if (
            string.IsNullOrWhiteSpace(localPath)
            || localPath.Length > InspectOoxmlEncryptionContract.MaximumLocalPathCharacters
        )
        {
            throw InvalidInput("local_path must be a non-empty string");
        }
        string path;
        try
        {
            path = Path.GetFullPath(localPath);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException
        )
        {
            throw InvalidInput("local_path is not a valid filesystem path", exception);
        }
        if (!File.Exists(path))
        {
            throw new WordToolkitOperationException(
                "NOT_FOUND",
                "The requested OOXML file does not exist"
            );
        }
        ValidateFileName(path, requireLeafName: false);
        return path;
    }

    private static void ValidateFileName(string fileName, bool requireLeafName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw InvalidInput("A supported OOXML file name is required");
        }
        var leafName = Path.GetFileName(fileName);
        if (
            leafName.Length > InspectOoxmlEncryptionContract.MaximumFileNameCharacters
            || !SupportedExtensions.Contains(Path.GetExtension(leafName))
        )
        {
            throw InvalidInput("A supported OOXML file name is required");
        }
        if (
            requireLeafName
            && (
                leafName != fileName
                || fileName.Contains('/')
                || fileName.Contains('\\')
                || fileName.Contains(':')
            )
        )
        {
            throw InvalidInput("Stream file_name must be a bounded leaf name");
        }
    }

    private static WordToolkitOperationException InvalidInput(
        string message,
        Exception? innerException = null
    ) => new("INVALID_INPUT", message, innerException: innerException);
}
