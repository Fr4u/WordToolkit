using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;
using WordToolkit.Engine.Xml;

namespace WordToolkit.Engine.Operations;

public static class InspectWordPackageContract
{
    public const string OperationName = "inspect_ooxml_package";
    public const string Contract = "wordtoolkit.inspect_ooxml_package/1.0";
    public const int DefaultMaxItems = 40;
    public const int MaximumMaxItems = 200;
    public const int MaximumStreamFileNameCharacters = 512;

    private static readonly HashSet<string> SupportedExtensions = new(
        [".docx", ".docm", ".dotx", ".dotm"],
        StringComparer.OrdinalIgnoreCase
    );

    public static bool IsSupportedFileName(string fileName)
    {
        return !string.IsNullOrWhiteSpace(fileName)
            && SupportedExtensions.Contains(Path.GetExtension(fileName));
    }
}

public sealed record InspectWordPackageRequest(
    string LocalPath,
    bool IncludeDetails = false,
    long MaxItems = InspectWordPackageContract.DefaultMaxItems
);

public sealed record InspectWordPackageDiagnostic(
    string Code,
    string Severity,
    string Message,
    string? PartUri,
    string? RelationshipId
);

public sealed record InspectWordPackageDiagnostics(
    int Errors,
    int Warnings,
    int Information,
    IReadOnlyList<InspectWordPackageDiagnostic> Items,
    bool Truncated
);

public sealed record InspectWordPackagePart(
    string Uri,
    string? ContentType,
    long Bytes,
    string Sha256
);

public sealed record InspectWordPackageRelationship(
    string SourcePartUri,
    string Id,
    string Type,
    string TargetMode,
    string? ResolvedTargetPartUri,
    bool ExternalTargetRedacted
);

public sealed record InspectWordPackageDetails(
    IReadOnlyList<InspectWordPackagePart> Parts,
    bool PartsTruncated,
    IReadOnlyList<InspectWordPackageRelationship> Relationships,
    bool RelationshipsTruncated
);

public sealed record InspectWordPackageResult(
    string OperationContract,
    string FileName,
    long Bytes,
    string PackageFingerprint,
    bool StructurallyValid,
    bool WordDocumentDetected,
    bool ValidWordPackage,
    string? OfficeDocumentPart,
    int EntryCount,
    int PartCount,
    int RelationshipCount,
    int ExternalRelationshipCount,
    int OrphanPartCount,
    InspectWordPackageDiagnostics Diagnostics,
    InspectWordPackageDetails? Details
);

public sealed class InspectWordPackageOperation
{
    private readonly OpcPackageReader _reader;

    public InspectWordPackageOperation(OpcPackageLimits? limits = null)
    {
        _reader = new OpcPackageReader(limits);
    }

    public InspectWordPackageResult Execute(
        InspectWordPackageRequest request,
        CancellationToken cancellationToken = default
    )
    {
        if (request is null)
        {
            throw InvalidInput("Inspection request is required");
        }
        var maxItems = Validate(request.LocalPath, request.MaxItems, requireLeafName: false);
        var path = ResolvePath(request.LocalPath);
        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read
            );
            return ExecuteCore(
                stream,
                Path.GetFileName(path),
                stream.Length,
                request.IncludeDetails,
                maxItems,
                cancellationToken
            );
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (WordToolkitOperationException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw MapFailure(exception, path, request.IncludeDetails);
        }
    }

    public InspectWordPackageResult Execute(
        Stream packageStream,
        string fileName,
        bool includeDetails = false,
        long maxItems = InspectWordPackageContract.DefaultMaxItems,
        CancellationToken cancellationToken = default
    )
    {
        if (packageStream is null)
        {
            throw InvalidInput("Package stream is required");
        }

        var validatedMaximum = Validate(fileName, maxItems, requireLeafName: true);
        long? originalPosition = null;
        try
        {
            if (!packageStream.CanRead || !packageStream.CanSeek)
            {
                throw InvalidInput("Package stream must be readable and seekable");
            }

            originalPosition = packageStream.Position;
            packageStream.Position = 0;
            var result = ExecuteCore(
                packageStream,
                fileName,
                packageStream.Length,
                includeDetails,
                validatedMaximum,
                cancellationToken
            );
            packageStream.Position = originalPosition.Value;
            originalPosition = null;
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (WordToolkitOperationException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw MapFailure(exception, localPath: null, includeDetails);
        }
        finally
        {
            if (originalPosition.HasValue)
            {
                try
                {
                    packageStream.Position = originalPosition.Value;
                }
                catch (Exception)
                {
                    // Preserve the operation failure; a hostile stream must not mask it.
                }
            }
        }
    }

    private InspectWordPackageResult ExecuteCore(
        Stream stream,
        string fileName,
        long bytes,
        bool includeDetails,
        int maxItems,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = _reader.Read(stream, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var errors = snapshot.Diagnostics.Count(diagnostic =>
            diagnostic.Severity is OpcDiagnosticSeverity.Error
                or OpcDiagnosticSeverity.Fatal
        );
        var warnings = snapshot.Diagnostics.Count(diagnostic =>
            diagnostic.Severity == OpcDiagnosticSeverity.Warning
        );
        var information = snapshot.Diagnostics.Count(diagnostic =>
            diagnostic.Severity == OpcDiagnosticSeverity.Info
        );
        var externalRelationships = snapshot.Relationships.Count(relationship =>
            relationship.TargetMode == OpcRelationshipTargetMode.External
        );
        var officeRelationships = snapshot.Relationships
            .Where(relationship =>
                relationship.SourcePartUri == "/"
                && relationship.TargetMode == OpcRelationshipTargetMode.Internal
                && WordPackageConformance.IsOfficeDocumentRelationshipType(
                    relationship.Type
                )
            )
            .ToArray();
        var officeDocumentPart = officeRelationships.Length == 1
            ? officeRelationships[0].ResolvedTargetPartUri
            : null;
        OpcPart? mainPart = null;
        var wordDocumentDetected = false;
        if (
            officeDocumentPart is not null
            && snapshot.Parts.TryGetValue(officeDocumentPart, out var resolvedMainPart)
            && WordPackageConformance.IsWordMainContentType(resolvedMainPart.ContentType)
        )
        {
            mainPart = resolvedMainPart;
            wordDocumentDetected = true;
        }
        var hasWordDocumentRoot = false;
        var mainContentTypeMatchesFileName = false;
        if (wordDocumentDetected)
        {
            mainContentTypeMatchesFileName =
                WordPackageConformance.IsMainContentTypeCompatibleWithFileName(
                    fileName,
                    mainPart!.ContentType
                );
            try
            {
                var source = LosslessXmlDocument.Parse(
                    mainPart!.Entry.Content,
                    cancellationToken: cancellationToken
                );
                hasWordDocumentRoot = WordPackageConformance.HasWordDocumentRoot(source);
            }
            catch (LosslessXmlException)
            {
                hasWordDocumentRoot = false;
            }
        }
        var diagnosticItems = snapshot.Diagnostics
            .Take(maxItems)
            .Select(diagnostic =>
                new InspectWordPackageDiagnostic(
                    diagnostic.Code,
                    diagnostic.Severity.ToString().ToLowerInvariant(),
                    includeDetails
                        ? Bound(diagnostic.Message, 512) ?? ""
                        : $"Package diagnostic {diagnostic.Code}; enable include_details for bounded location metadata.",
                    includeDetails ? Bound(diagnostic.PartUri, 512) : null,
                    includeDetails ? Bound(diagnostic.RelationshipId, 128) : null
                )
            )
            .ToArray();

        InspectWordPackageDetails? details = null;
        if (includeDetails)
        {
            var parts = snapshot.Parts.Values
                .OrderBy(part => part.Uri, StringComparer.Ordinal)
                .Take(maxItems)
                .Select(part =>
                    new InspectWordPackagePart(
                        Bound(part.Uri, 512) ?? "",
                        Bound(part.ContentType, 256),
                        part.Entry.UncompressedLength,
                        part.Entry.Sha256
                    )
                )
                .ToArray();
            var relationships = snapshot.Relationships
                .OrderBy(relationship => relationship.SourcePartUri, StringComparer.Ordinal)
                .ThenBy(relationship => relationship.Id, StringComparer.Ordinal)
                .Take(maxItems)
                .Select(relationship =>
                    new InspectWordPackageRelationship(
                        Bound(relationship.SourcePartUri, 512) ?? "",
                        Bound(relationship.Id, 128) ?? "",
                        Bound(relationship.Type, 512) ?? "",
                        relationship.TargetMode.ToString().ToLowerInvariant(),
                        Bound(relationship.ResolvedTargetPartUri, 512),
                        relationship.TargetMode == OpcRelationshipTargetMode.External
                    )
                )
                .ToArray();
            details = new InspectWordPackageDetails(
                parts,
                snapshot.Parts.Count > parts.Length,
                relationships,
                snapshot.Relationships.Count > relationships.Length
            );
        }

        return new InspectWordPackageResult(
            InspectWordPackageContract.Contract,
            fileName,
            bytes,
            snapshot.Fingerprint,
            snapshot.IsStructurallyValid,
            wordDocumentDetected,
            snapshot.IsStructurallyValid
                && wordDocumentDetected
                && hasWordDocumentRoot
                && mainContentTypeMatchesFileName,
            officeDocumentPart,
            snapshot.Entries.Count,
            snapshot.Parts.Count,
            snapshot.Relationships.Count,
            externalRelationships,
            snapshot.Diagnostics.Count(diagnostic => diagnostic.Code == "OPC040"),
            new InspectWordPackageDiagnostics(
                errors,
                warnings,
                information,
                diagnosticItems,
                snapshot.Diagnostics.Count > diagnosticItems.Length
            ),
            details
        );
    }

    private static int Validate(
        string fileName,
        long maxItems,
        bool requireLeafName
    )
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw InvalidInput("local_path must be a non-empty string");
        }
        if (
            requireLeafName
            && (
                fileName.Length > InspectWordPackageContract.MaximumStreamFileNameCharacters
                || fileName.IndexOfAny(['<', '>', ':', '"', '/', '\\', '|', '?', '*']) >= 0
                || fileName.Any(char.IsControl)
            )
        )
        {
            throw InvalidInput(
                $"Stream file_name must be a leaf name of at most {InspectWordPackageContract.MaximumStreamFileNameCharacters} characters"
            );
        }
        if (!InspectWordPackageContract.IsSupportedFileName(fileName))
        {
            throw InvalidInput(
                "Package inspection accepts DOCX, DOCM, DOTX, or DOTM files"
            );
        }
        if (maxItems is < 1 or > InspectWordPackageContract.MaximumMaxItems)
        {
            throw InvalidInput(
                $"max_items must be between 1 and {InspectWordPackageContract.MaximumMaxItems}"
            );
        }
        return checked((int)maxItems);
    }

    private static string ResolvePath(string rawPath)
    {
        string path;
        try
        {
            path = Path.GetFullPath(rawPath);
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or NotSupportedException
                or PathTooLongException
        )
        {
            throw InvalidInput("local_path is not a valid filesystem path", exception);
        }
        if (!File.Exists(path))
        {
            throw new WordToolkitOperationException(
                "NOT_FOUND",
                "The requested Word package does not exist"
            );
        }
        return path;
    }

    private static WordToolkitOperationException MapFailure(
        Exception exception,
        string? localPath,
        bool includeDetails
    )
    {
        return exception switch
        {
            OpcPackageLimitException limit => new WordToolkitOperationException(
                "PACKAGE_LIMIT",
                "The package exceeds a bounded safety limit",
                SafeReason(limit.Message, localPath, includeDetails),
                innerException: limit
            ),
            InvalidDataException invalid => new WordToolkitOperationException(
                "INVALID_PACKAGE",
                "The file is not a readable OPC ZIP package",
                SafeReason(invalid.Message, localPath, includeDetails),
                innerException: invalid
            ),
            UnauthorizedAccessException denied => new WordToolkitOperationException(
                "ACCESS_DENIED",
                "The Word package cannot be read with current permissions",
                innerException: denied
            ),
            FileNotFoundException missing => new WordToolkitOperationException(
                "NOT_FOUND",
                "The requested Word package does not exist",
                innerException: missing
            ),
            DirectoryNotFoundException missing => new WordToolkitOperationException(
                "NOT_FOUND",
                "The requested Word package does not exist",
                innerException: missing
            ),
            ObjectDisposedException disposed => InvalidInput(
                "Package stream must be open",
                disposed
            ),
            NotSupportedException unsupported => new WordToolkitOperationException(
                "INVALID_PACKAGE",
                "The file is not a readable OPC ZIP package",
                SafeReason(unsupported.Message, localPath, includeDetails),
                innerException: unsupported
            ),
            IOException io => new WordToolkitOperationException(
                "IO_ERROR",
                "The Word package could not be read",
                retryable: true,
                innerException: io
            ),
            ArgumentException invalid => InvalidInput(
                "Package stream is not a readable OPC ZIP package",
                invalid
            ),
            _ => new WordToolkitOperationException(
                "INTERNAL_ERROR",
                "The package inspection operation failed",
                innerException: exception
            ),
        };
    }

    private static WordToolkitOperationException InvalidInput(
        string message,
        Exception? innerException = null
    )
    {
        return new WordToolkitOperationException(
            "INVALID_INPUT",
            message,
            innerException: innerException
        );
    }

    private static string? SafeReason(
        string? message,
        string? localPath,
        bool includeDetails
    )
    {
        if (!includeDetails || string.IsNullOrWhiteSpace(message))
        {
            return null;
        }
        var safe = localPath is null
            ? message
            : message.Replace(localPath, "<redacted>", StringComparison.OrdinalIgnoreCase);
        return Bound(safe, 512);
    }

    private static string? Bound(string? value, int maxCharacters)
    {
        if (value is null || value.Length <= maxCharacters)
        {
            return value;
        }
        return value[..maxCharacters] + "…";
    }
}
