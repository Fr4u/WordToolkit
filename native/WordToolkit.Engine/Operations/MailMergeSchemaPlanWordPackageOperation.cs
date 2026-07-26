using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Resources;
using WordToolkit.Engine.Semantics;

namespace WordToolkit.Engine.Operations;

public sealed class MailMergeSchemaPlanWordPackageOperation
{
    private readonly OpcPackageLimits _packageLimits;
    private readonly WordSemanticProjectionOptions? _semanticOptions;
    private readonly WordSettingsGraphOptions? _settingsOptions;
    private readonly WordReferenceGraphOptions? _referenceOptions;
    private readonly WordMailMergeGraphOptions? _mailMergeOptions;
    private readonly WordMailMergeSchemaPlannerOptions? _plannerOptions;
    private readonly Func<WordOperationResourceLease> _resourceLeaseFactory;

    public MailMergeSchemaPlanWordPackageOperation(
        OpcPackageLimits? packageLimits = null,
        WordSemanticProjectionOptions? semanticOptions = null,
        WordSettingsGraphOptions? settingsOptions = null,
        WordReferenceGraphOptions? referenceOptions = null,
        WordMailMergeGraphOptions? mailMergeOptions = null,
        WordMailMergeSchemaPlannerOptions? plannerOptions = null,
        Func<WordOperationResourceLease>? resourceLeaseFactory = null
    )
    {
        _packageLimits = packageLimits ?? OpcPackageLimits.Default;
        _semanticOptions = semanticOptions;
        _settingsOptions = settingsOptions;
        _referenceOptions = referenceOptions;
        _mailMergeOptions = mailMergeOptions;
        _plannerOptions = plannerOptions;
        _resourceLeaseFactory = resourceLeaseFactory ?? (() => new WordOperationResourceLease());
    }

    public MailMergeSchemaPlanResult Plan(
        MailMergeSchemaPlanRequest request,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(request);
        var path = ResolvePath(request.LocalPath);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var resourceLease = _resourceLeaseFactory()
                ?? throw new InvalidOperationException(
                    "The operation resource-lease factory returned null."
                );
            var package = new OpcPackageReader(_packageLimits, resourceLease).Read(
                path,
                cancellationToken
            );
            if (!package.IsStructurallyValid)
            {
                throw new WordToolkitOperationException(
                    "INVALID_PACKAGE",
                    "The input package has structural OPC errors"
                );
            }
            if (!string.Equals(
                package.Fingerprint,
                request.ExpectedPackageFingerprint,
                StringComparison.OrdinalIgnoreCase
            ))
            {
                throw new WordToolkitOperationException(
                    "VERSION_CONFLICT",
                    "The package does not match expected_package_fingerprint"
                );
            }
            var semantic = new WordSemanticProjector(
                _semanticOptions,
                resourceLease
            ).Project(package, cancellationToken);
            var settings = new WordSettingsGraphBuilder(
                _settingsOptions,
                resourceLease
            ).Build(package, semantic, cancellationToken);
            var references = new WordReferenceGraphBuilder(
                _referenceOptions,
                resourceLease
            ).Build(package, semantic, cancellationToken);
            var graph = new WordMailMergeGraphBuilder(
                _mailMergeOptions,
                resourceLease
            ).Build(package, semantic, settings, references, cancellationToken);
            var plan = new WordMailMergeSchemaPlanner(_plannerOptions).Plan(
                graph,
                request.SourceColumns,
                cancellationToken
            );
            return new MailMergeSchemaPlanResult(
                MailMergeSchemaPlanWordPackageContract.Contract,
                Path.GetFileName(path),
                plan.PackageFingerprint,
                plan.SourceSchemaFingerprint,
                plan.PlanId,
                plan.ConfigurationId,
                plan.MainDocumentType,
                plan.Destination,
                plan.SourceColumns,
                plan.Bindings,
                plan.Issues,
                plan.SchemaBlockedReasons,
                plan.ExecutionBlockedReasons,
                plan.UnusedSourceColumnCount,
                plan.CanBindSchema,
                plan.ExecutionSupported,
                plan.ExternalSourceIgnored,
                plan.SensitiveConnectionMetadataIgnored,
                plan.ContainsRecordValues,
                new MailMergeSchemaPlanDisclosure(
                    RecordValuesAccepted: false,
                    RecordValuesReturned: false,
                    WordOpened: false,
                    MailMergeExecuted: false,
                    DataSourcesOpened: false,
                    QueriesExecuted: false,
                    ExternalTargetsFollowed: false,
                    MutationPerformed: false,
                    DocumentContentIsUntrusted: true
                ),
                resourceLease.Snapshot()
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
            throw MapFailure(exception, request.LocalPath);
        }
    }

    private static void Validate(MailMergeSchemaPlanRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.LocalPath))
        {
            throw Invalid("local_path is required");
        }
        if (request.LocalPath.Length > MailMergeSchemaPlanWordPackageContract.MaximumLocalPathCharacters)
        {
            throw Invalid(
                $"local_path cannot exceed {MailMergeSchemaPlanWordPackageContract.MaximumLocalPathCharacters} characters"
            );
        }
        var extension = Path.GetExtension(request.LocalPath);
        if (extension is not ".docx" and not ".docm" and not ".dotx" and not ".dotm")
        {
            throw Invalid("local_path must use .docx, .docm, .dotx, or .dotm");
        }
        if (!IsSha256(request.ExpectedPackageFingerprint))
        {
            throw Invalid(
                "expected_package_fingerprint must be exactly 64 hexadecimal characters"
            );
        }
        if (request.SourceColumns is null)
        {
            throw Invalid("source_columns is required");
        }
    }

    private static string ResolvePath(string localPath)
    {
        try
        {
            var path = Path.GetFullPath(localPath);
            if (!File.Exists(path))
            {
                throw new WordToolkitOperationException(
                    "NOT_FOUND",
                    "The requested Word package does not exist"
                );
            }
            return path;
        }
        catch (WordToolkitOperationException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException
        )
        {
            throw Invalid("local_path is not a valid filesystem path", exception);
        }
    }

    private static WordToolkitOperationException MapFailure(
        Exception exception,
        string localPath
    ) => exception switch
    {
        WordMailMergeSchemaPlanLimitException
            or WordMailMergeLimitException
            or WordReferenceLimitException
            or WordSettingsLimitException
            or WordSemanticLimitException
            or WordOperationResourceLimitException
            or OpcPackageLimitException => new WordToolkitOperationException(
                "PACKAGE_LIMIT",
                "The mail-merge schema plan exceeds a bounded safety limit",
                SafeReason(exception.Message, localPath),
                innerException: exception
            ),
        WordMailMergeSchemaPlanException => new WordToolkitOperationException(
            "INVALID_INPUT",
            "The supplied mail-merge source schema is invalid",
            SafeReason(exception.Message, localPath),
            innerException: exception
        ),
        WordMailMergeProjectionException
            or WordReferenceProjectionException
            or WordSettingsProjectionException
            or WordSemanticProjectionException => new WordToolkitOperationException(
                "INVALID_WORD_PACKAGE",
                "The package cannot be projected safely as a mail-merge graph",
                SafeReason(exception.Message, localPath),
                innerException: exception
            ),
        InvalidDataException => new WordToolkitOperationException(
            "INVALID_PACKAGE",
            "The file is not a readable OPC ZIP package",
            innerException: exception
        ),
        FileNotFoundException or DirectoryNotFoundException => new WordToolkitOperationException(
            "NOT_FOUND",
            "The requested Word package does not exist",
            innerException: exception
        ),
        UnauthorizedAccessException => new WordToolkitOperationException(
            "ACCESS_DENIED",
            "The Word package cannot be read with current permissions",
            innerException: exception
        ),
        IOException => new WordToolkitOperationException(
            "IO_ERROR",
            "The mail-merge schema package could not be read",
            SafeReason(exception.Message, localPath),
            retryable: true,
            innerException: exception
        ),
        ArgumentException => Invalid(
            SafeReason(exception.Message, localPath) ?? "Invalid mail-merge schema request",
            exception
        ),
        _ => new WordToolkitOperationException(
            "INTERNAL_ERROR",
            "The mail-merge schema planning operation failed",
            innerException: exception
        ),
    };

    private static bool IsSha256(string value) => value.Length == 64
        && value.All(character => char.IsAsciiHexDigit(character));

    private static string? SafeReason(string? message, string localPath)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }
        var reason = message.Replace(localPath, "<document>", StringComparison.OrdinalIgnoreCase);
        return reason.Length <= 512 ? reason : reason[..512];
    }

    private static WordToolkitOperationException Invalid(
        string message,
        Exception? innerException = null
    ) => new("INVALID_INPUT", message, innerException: innerException);
}
