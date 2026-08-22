using System.Collections.ObjectModel;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;

namespace WordToolkit.Engine.Operations;

public static class QueryWordPackageContract
{
    public const string OperationName = "query_ooxml_semantics";
    public const string Contract = "wordtoolkit.query_ooxml_semantics/1.0";
    public const int MaximumPropertyValueCharacters = 160;
    public const int MaximumLocalPathCharacters = 32_767;
    public const int MaximumSemanticNodeIdCharacters = SemanticNodeId.MaximumCharacters;
    public const int MaximumSourcePartUriCharacters = 512;
    public const int MaximumSourcePathCharacters = 1_024;

    private static readonly HashSet<string> SensitivePropertyNames = new(
        ["anchor", "author", "date", "guid", "initials", "instruction", "name"],
        StringComparer.Ordinal
    );

    public static bool IsSensitiveProperty(string name) =>
        SensitivePropertyNames.Contains(name);
}

public sealed record QueryWordPackageRequest(
    string LocalPath,
    WordSemanticQuery Query,
    string? ExpectedPackageFingerprint = null,
    bool IncludeSensitiveProperties = false
);

public sealed record QueryWordPackageMatch(
    string NodeId,
    string Kind,
    string ObjectCategory,
    string StoryKind,
    string? ParentId,
    int SourceOrder,
    int ChildCount,
    string IdentityKind,
    string IdentityFingerprint,
    string? TextPreview,
    bool TextPreviewTruncated,
    IReadOnlyDictionary<string, string>? Properties,
    IReadOnlyList<string>? RedactedPropertyNames,
    IReadOnlyList<string>? TruncatedPropertyNames,
    string? SourcePartUri,
    string? SourcePath,
    int? SourceElementOrdinal
);

public sealed record QueryWordPackageDisclosure(
    bool TextPreviewsReturned,
    bool PropertiesReturned,
    bool SensitivePropertiesReturned,
    bool SensitiveTextPreviewsReturned,
    bool SourceLocationsReturned,
    bool RawXmlReturned,
    bool ExternalRelationshipsFollowed,
    bool WordOpened,
    bool DocumentContentIsUntrusted
);

public sealed record QueryWordPackageResult(
    string OperationContract,
    string FileName,
    string PackageFingerprint,
    string MainPartUri,
    int ProjectedPartCount,
    int ProjectionWarningCount,
    bool SemanticIndexUsed,
    string? SemanticIndexId,
    string? SemanticIndexFingerprint,
    string CandidateSeed,
    int TotalNodeCount,
    int ScannedNodeCount,
    int MatchedNodeCount,
    int Offset,
    int ReturnedNodeCount,
    int? NextOffset,
    IReadOnlyList<QueryWordPackageMatch> Matches,
    QueryWordPackageDisclosure Disclosure
);

public sealed class QueryWordPackageOperation
{
    private readonly OpcPackageReader _reader;
    private readonly WordSemanticProjector _projector;

    public QueryWordPackageOperation(
        OpcPackageLimits? packageLimits = null,
        WordSemanticProjectionOptions? projectionOptions = null
    )
    {
        _reader = new OpcPackageReader(packageLimits);
        _projector = new WordSemanticProjector(projectionOptions);
    }

    public QueryWordPackageResult Execute(
        QueryWordPackageRequest request,
        CancellationToken cancellationToken = default
    )
    {
        if (request is null)
        {
            throw InvalidInput("Query request is required");
        }
        ValidateRequest(request, requireLocalPath: true);
        var path = ResolvePath(request.LocalPath);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var package = _reader.Read(path, cancellationToken);
            return ExecutePackage(
                package,
                Path.GetFileName(path),
                request.Query,
                request.ExpectedPackageFingerprint,
                request.IncludeSensitiveProperties,
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
            throw MapFailure(exception, path);
        }
    }

    public QueryWordPackageResult Execute(
        Stream packageStream,
        string fileName,
        WordSemanticQuery query,
        string? expectedPackageFingerprint = null,
        bool includeSensitiveProperties = false,
        CancellationToken cancellationToken = default
    )
    {
        if (packageStream is null)
        {
            throw InvalidInput("Package stream is required");
        }
        if (query is null)
        {
            throw InvalidInput("query is required");
        }
        ValidateFileName(fileName);
        if (
            expectedPackageFingerprint is not null
            && !IsSha256(expectedPackageFingerprint)
        )
        {
            throw InvalidInput(
                "expected_package_fingerprint must be exactly 64 hexadecimal characters"
            );
        }
        ValidateResponseOptions(query, includeSensitiveProperties);

        long? originalPosition = null;
        try
        {
            if (!packageStream.CanRead || !packageStream.CanSeek)
            {
                throw InvalidInput("Package stream must be readable and seekable");
            }
            originalPosition = packageStream.Position;
            packageStream.Position = 0;
            var package = _reader.Read(packageStream, cancellationToken);
            var result = ExecutePackage(
                package,
                fileName,
                query,
                expectedPackageFingerprint,
                includeSensitiveProperties,
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
            throw MapFailure(exception, localPath: null);
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

    public QueryWordPackageResult ExecuteProjected(
        WordSemanticDocument document,
        string fileName,
        WordSemanticQuery query,
        bool includeSensitiveProperties = false,
        WordSemanticIndex? semanticIndex = null,
        string? semanticIndexId = null,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            ArgumentNullException.ThrowIfNull(document);
            ArgumentNullException.ThrowIfNull(query);
            ValidateFileName(fileName);
            ValidateResponseOptions(query, includeSensitiveProperties);
            if (
                semanticIndex is not null
                && !string.Equals(
                    semanticIndex.PackageFingerprint,
                    document.PackageFingerprint,
                    StringComparison.Ordinal
                )
            )
            {
                throw new ArgumentException(
                    "The semantic index and semantic document have different package fingerprints.",
                    nameof(semanticIndex)
                );
            }
            if (semanticIndexId is not null && semanticIndex is null)
            {
                throw new ArgumentException(
                    "semantic_index_id requires a semantic index.",
                    nameof(semanticIndexId)
                );
            }
            if (semanticIndexId is not null && !IsSemanticIndexId(semanticIndexId))
            {
                throw new ArgumentException(
                    "semantic_index_id must use the wsi_ prefix followed by 32 lowercase hexadecimal characters.",
                    nameof(semanticIndexId)
                );
            }

            cancellationToken.ThrowIfCancellationRequested();
            var queryResult = semanticIndex is null
                ? new WordSemanticQueryEngine().Query(document, query, cancellationToken)
                : new WordSemanticQueryEngine().Query(semanticIndex, query, cancellationToken);
            return ProjectResult(
                document,
                fileName,
                query,
                queryResult,
                includeSensitiveProperties,
                semanticIndexId
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
        catch (KeyNotFoundException exception)
        {
            throw new WordToolkitOperationException(
                "TARGET_NOT_FOUND",
                Bound(exception.Message, 512) ?? "Semantic scope was not found",
                innerException: exception
            );
        }
        catch (ArgumentException exception)
        {
            throw InvalidInput(
                Bound(exception.Message, 512) ?? "Invalid semantic query",
                exception
            );
        }
        catch (Exception exception)
        {
            throw new WordToolkitOperationException(
                "INTERNAL_ERROR",
                "The semantic query operation failed",
                innerException: exception
            );
        }
    }

    private static QueryWordPackageResult ProjectResult(
        WordSemanticDocument document,
        string fileName,
        WordSemanticQuery query,
        WordSemanticQueryResult result,
        bool includeSensitiveProperties,
        string? semanticIndexId
    )
    {
        var matches = new List<QueryWordPackageMatch>(result.Matches.Count);
        var sensitivePropertiesReturned = false;
        var sensitiveTextPreviewsReturned = false;
        var mainPartUri = RequireWithinResponseLimit(
            document.MainPartUri,
            QueryWordPackageContract.MaximumSourcePartUriCharacters,
            "Main-part URI"
        );
        foreach (var match in result.Matches)
        {
            if (!document.TryGetNode(match.NodeId, out var node) || node is null)
            {
                throw new InvalidOperationException(
                    $"Semantic query returned missing node '{match.NodeId}'."
                );
            }

            IReadOnlyDictionary<string, string>? properties = null;
            IReadOnlyList<string>? redactedPropertyNames = null;
            IReadOnlyList<string>? truncatedPropertyNames = null;
            var textPreview = ProjectTextPreview(
                node,
                query.TextPreviewCharacters,
                includeSensitiveProperties
            );
            sensitiveTextPreviewsReturned |= textPreview.SensitiveTextReturned;
            if (match.Properties is not null)
            {
                sensitivePropertiesReturned |=
                    includeSensitiveProperties
                    && match.Properties.Keys.Any(
                        QueryWordPackageContract.IsSensitiveProperty
                    );
                var visible = new Dictionary<string, string>(StringComparer.Ordinal);
                var redacted = new List<string>();
                var truncated = new List<string>();
                foreach (
                    var property in match.Properties.OrderBy(
                        item => item.Key,
                        StringComparer.Ordinal
                    )
                )
                {
                    if (
                        !includeSensitiveProperties
                        && QueryWordPackageContract.IsSensitiveProperty(property.Key)
                    )
                    {
                        redacted.Add(property.Key);
                        continue;
                    }
                    if (property.Key.Length > 128)
                    {
                        throw new WordToolkitOperationException(
                            "PACKAGE_LIMIT",
                            "A semantic property name exceeds the public response limit"
                        );
                    }
                    if (
                        property.Value.Length
                        > QueryWordPackageContract.MaximumPropertyValueCharacters
                    )
                    {
                        truncated.Add(property.Key);
                    }
                    visible.Add(
                        property.Key,
                        Bound(
                            property.Value,
                            QueryWordPackageContract.MaximumPropertyValueCharacters
                        )!
                    );
                }
                if (visible.Count != 0)
                {
                    properties = new ReadOnlyDictionary<string, string>(visible);
                }
                if (redacted.Count != 0)
                {
                    redactedPropertyNames = new ReadOnlyCollection<string>(redacted);
                }
                if (truncated.Count != 0)
                {
                    truncatedPropertyNames = new ReadOnlyCollection<string>(truncated);
                }
            }

            matches.Add(
                new QueryWordPackageMatch(
                    match.NodeId.Value,
                    SnakeCase(node.Kind),
                    ObjectCategory(node.Kind),
                    ResolveStoryKind(document, node),
                    match.ParentId?.Value,
                    match.SourceOrder,
                    node.Children.Count,
                    SnakeCase(node.IdentityKind),
                    node.IdentityFingerprint,
                    textPreview.Text,
                    textPreview.Truncated,
                    properties,
                    redactedPropertyNames,
                    truncatedPropertyNames,
                    match.SourcePartUri is null
                        ? null
                        : RequireWithinResponseLimit(
                            match.SourcePartUri,
                            QueryWordPackageContract.MaximumSourcePartUriCharacters,
                            "Source-part URI"
                        ),
                    match.SourcePath is null
                        ? null
                        : RequireWithinResponseLimit(
                            match.SourcePath,
                            QueryWordPackageContract.MaximumSourcePathCharacters,
                            "Source path"
                        ),
                    match.SourceElementOrdinal
                )
            );
        }

        return new QueryWordPackageResult(
            QueryWordPackageContract.Contract,
            fileName,
            result.PackageFingerprint,
            mainPartUri,
            document.ProjectedPartCount,
            document.Warnings.Count,
            result.SemanticIndexUsed,
            semanticIndexId,
            result.SemanticIndexFingerprint,
            result.CandidateSeed,
            result.TotalNodeCount,
            result.ScannedNodeCount,
            result.MatchedNodeCount,
            result.Offset,
            result.ReturnedNodeCount,
            result.NextOffset,
            new ReadOnlyCollection<QueryWordPackageMatch>(matches),
            new QueryWordPackageDisclosure(
                matches.Any(match => match.TextPreview is not null),
                matches.Any(match => match.Properties is not null),
                sensitivePropertiesReturned,
                sensitiveTextPreviewsReturned,
                matches.Any(match => match.SourcePartUri is not null),
                RawXmlReturned: false,
                ExternalRelationshipsFollowed: false,
                WordOpened: false,
                DocumentContentIsUntrusted: true
            )
        );
    }

    private QueryWordPackageResult ExecutePackage(
        OpcPackageSnapshot package,
        string fileName,
        WordSemanticQuery query,
        string? expectedPackageFingerprint,
        bool includeSensitiveProperties,
        CancellationToken cancellationToken
    )
    {
        if (!package.IsStructurallyValid)
        {
            throw new WordToolkitOperationException(
                "INVALID_PACKAGE",
                "The OPC package failed structural validation"
            );
        }
        if (
            expectedPackageFingerprint is not null
            && !string.Equals(
                expectedPackageFingerprint,
                package.Fingerprint,
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            throw new WordToolkitOperationException(
                "VERSION_CONFLICT",
                "The package does not match expected_package_fingerprint"
            );
        }

        var document = _projector.Project(package, cancellationToken);
        if (
            !package.Parts.TryGetValue(document.MainPartUri, out var mainPart)
            || !WordPackageConformance.IsMainContentTypeCompatibleWithFileName(
                fileName,
                mainPart.ContentType
            )
        )
        {
            throw new WordToolkitOperationException(
                "INVALID_WORD_PACKAGE",
                "The file extension does not match the Word main-part content type"
            );
        }

        return ExecuteProjected(
            document,
            fileName,
            query,
            includeSensitiveProperties,
            semanticIndex: null,
            semanticIndexId: null,
            cancellationToken
        );
    }

    private static string ResolveStoryKind(
        WordSemanticDocument document,
        WordSemanticNode node
    )
    {
        WordSemanticNode? current = node;
        while (current is not null)
        {
            if (current.Properties.TryGetValue("story_kind", out var storyKind))
            {
                return Bound(storyKind, 64)!;
            }
            if (
                current.ParentId is null
                || !document.TryGetNode(current.ParentId.Value, out current)
            )
            {
                break;
            }
        }
        return "main_document";
    }

    private static (
        string? Text,
        bool Truncated,
        bool SensitiveTextReturned
    ) ProjectTextPreview(
        WordSemanticNode node,
        int maxCharacters,
        bool includeSensitiveText
    )
    {
        if (maxCharacters == 0)
        {
            return (null, false, false);
        }

        var rawLimit = checked(maxCharacters + 1);
        var builder = new System.Text.StringBuilder(Math.Min(rawLimit, 256));
        var sensitiveTextReturned = false;
        foreach (var candidate in node.DescendantsAndSelf())
        {
            if (candidate.Kind == WordSemanticNodeKind.Paragraph && builder.Length != 0)
            {
                if (builder.Length == rawLimit)
                {
                    break;
                }
                builder.Append('\n');
            }

            var isSensitiveFieldText = candidate.Kind == WordSemanticNodeKind.Field;
            var value = candidate.Kind switch
            {
                WordSemanticNodeKind.Text => candidate.Text,
                WordSemanticNodeKind.Field when includeSensitiveText => candidate.Text,
                WordSemanticNodeKind.Tab => "\t",
                WordSemanticNodeKind.Break => "\n",
                _ => null,
            };
            if (string.IsNullOrEmpty(value))
            {
                continue;
            }

            var remaining = rawLimit - builder.Length;
            if (remaining <= 0)
            {
                break;
            }
            var appendLength = Math.Min(value.Length, remaining);
            if (
                isSensitiveFieldText
                && builder.Length < maxCharacters
                && appendLength > 0
            )
            {
                sensitiveTextReturned = true;
            }
            builder.Append(value.AsSpan(0, appendLength));
        }

        var raw = builder.ToString();
        var truncated = raw.Length > maxCharacters;
        return (
            truncated ? raw[..maxCharacters] : raw,
            truncated,
            sensitiveTextReturned
        );
    }

    private static string ObjectCategory(WordSemanticNodeKind kind) =>
        kind switch
        {
            WordSemanticNodeKind.Document => "document",
            WordSemanticNodeKind.Header
                or WordSemanticNodeKind.Footer
                or WordSemanticNodeKind.Footnotes
                or WordSemanticNodeKind.Endnotes
                or WordSemanticNodeKind.Comments
                or WordSemanticNodeKind.GlossaryDocument => "story",
            WordSemanticNodeKind.Body
                or WordSemanticNodeKind.Section
                or WordSemanticNodeKind.ContentControl
                or WordSemanticNodeKind.TextBox => "container",
            WordSemanticNodeKind.Paragraph => "block",
            WordSemanticNodeKind.Table
                or WordSemanticNodeKind.TableRow
                or WordSemanticNodeKind.TableCell => "table",
            WordSemanticNodeKind.Hyperlink
                or WordSemanticNodeKind.Field
                or WordSemanticNodeKind.Bookmark
                or WordSemanticNodeKind.BookmarkEnd
                or WordSemanticNodeKind.HeaderReference
                or WordSemanticNodeKind.FooterReference
                or WordSemanticNodeKind.FootnoteReference
                or WordSemanticNodeKind.EndnoteReference => "reference",
            WordSemanticNodeKind.CommentAnchor
                or WordSemanticNodeKind.Comment
                or WordSemanticNodeKind.Revision => "review",
            WordSemanticNodeKind.Drawing => "drawing",
            WordSemanticNodeKind.Equation
                or WordSemanticNodeKind.EquationComponent => "math",
            WordSemanticNodeKind.AlternateContent
                or WordSemanticNodeKind.ExtensionIsland => "extension",
            _ => "inline",
        };

    private static void ValidateRequest(
        QueryWordPackageRequest request,
        bool requireLocalPath
    )
    {
        if (request.Query is null)
        {
            throw InvalidInput("query is required");
        }
        if (requireLocalPath && string.IsNullOrWhiteSpace(request.LocalPath))
        {
            throw InvalidInput("local_path must be a non-empty string");
        }
        if (!InspectWordPackageContract.IsSupportedFileName(request.LocalPath))
        {
            throw InvalidInput(
                "Semantic query accepts DOCX, DOCM, DOTX, or DOTM files"
            );
        }
        if (
            request.ExpectedPackageFingerprint is not null
            && !IsSha256(request.ExpectedPackageFingerprint)
        )
        {
            throw InvalidInput(
                "expected_package_fingerprint must be exactly 64 hexadecimal characters"
            );
        }
        ValidateResponseOptions(request.Query, request.IncludeSensitiveProperties);
    }

    private static void ValidateResponseOptions(
        WordSemanticQuery query,
        bool includeSensitiveProperties
    )
    {
        try
        {
            query.Validate();
        }
        catch (ArgumentException exception)
        {
            throw InvalidInput(
                Bound(exception.Message, 512) ?? "Invalid semantic query",
                exception
            );
        }
        RejectDuplicateKinds(query.Kinds, "kinds");
        RejectDuplicateKinds(query.Ancestor?.Kinds, "ancestor.kinds");
        RejectDuplicateKinds(query.Descendant?.Kinds, "descendant.kinds");
        if (
            query.WithinNodeId is { } withinNodeId
            && !SemanticNodeId.HasValidSyntax(withinNodeId.Value)
        )
        {
            throw InvalidInput(
                "within_node_id must use the wdn_ prefix and contain only URL-safe identifier characters"
            );
        }
        if (includeSensitiveProperties && !query.IncludeProperties)
        {
            throw InvalidInput(
                "include_sensitive_properties requires include_properties"
            );
        }
    }

    private static void RejectDuplicateKinds(
        IReadOnlyCollection<WordSemanticNodeKind>? kinds,
        string field
    )
    {
        if (kinds is not null && kinds.Distinct().Count() != kinds.Count)
        {
            throw InvalidInput($"{field} cannot contain duplicates");
        }
    }

    private static void ValidateFileName(string fileName)
    {
        if (
            string.IsNullOrWhiteSpace(fileName)
            || fileName.Length > InspectWordPackageContract.MaximumStreamFileNameCharacters
            || fileName.IndexOfAny(['<', '>', ':', '"', '/', '\\', '|', '?', '*']) >= 0
            || fileName.Any(char.IsControl)
        )
        {
            throw InvalidInput(
                $"file_name must be a leaf name of at most {InspectWordPackageContract.MaximumStreamFileNameCharacters} characters"
            );
        }
        if (!InspectWordPackageContract.IsSupportedFileName(fileName))
        {
            throw InvalidInput(
                "Semantic query accepts DOCX, DOCM, DOTX, or DOTM files"
            );
        }
    }

    private static string ResolvePath(string rawPath)
    {
        if (rawPath.Length > QueryWordPackageContract.MaximumLocalPathCharacters)
        {
            throw InvalidInput(
                $"local_path cannot exceed {QueryWordPackageContract.MaximumLocalPathCharacters} characters"
            );
        }
        try
        {
            var path = Path.GetFullPath(rawPath);
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
            exception is ArgumentException
                or NotSupportedException
                or PathTooLongException
        )
        {
            throw InvalidInput("local_path is not a valid filesystem path", exception);
        }
    }

    private static WordToolkitOperationException MapFailure(
        Exception exception,
        string? localPath
    ) =>
        exception switch
        {
            OpcPackageSourceChangedException => new WordToolkitOperationException(
                "SOURCE_CHANGED",
                "The Word package changed while a stable snapshot was being captured",
                "Retry after Microsoft Word finishes saving the document",
                retryable: true,
                innerException: exception
            ),
            WordSemanticLimitException limit => new WordToolkitOperationException(
                "PACKAGE_LIMIT",
                "Semantic projection exceeds a bounded safety limit",
                SafeReason(limit.Message, localPath),
                innerException: limit
            ),
            WordSemanticProjectionException projection => new WordToolkitOperationException(
                "INVALID_WORD_PACKAGE",
                "The package cannot be projected as a Word semantic document",
                SafeReason(projection.Message, localPath),
                innerException: projection
            ),
            OpcPackageLimitException limit => new WordToolkitOperationException(
                "PACKAGE_LIMIT",
                "The package exceeds a bounded safety limit",
                SafeReason(limit.Message, localPath),
                innerException: limit
            ),
            InvalidDataException invalid => new WordToolkitOperationException(
                "INVALID_PACKAGE",
                "The file is not a readable OPC ZIP package",
                SafeReason(invalid.Message, localPath),
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
            NotSupportedException unsupported => InvalidInput(
                "Package stream must be readable and seekable",
                unsupported
            ),
            IOException io => new WordToolkitOperationException(
                "IO_ERROR",
                "The Word package could not be read",
                retryable: true,
                innerException: io
            ),
            ArgumentException invalid => InvalidInput(
                Bound(invalid.Message, 512) ?? "Invalid semantic query",
                invalid
            ),
            _ => new WordToolkitOperationException(
                "INTERNAL_ERROR",
                "The semantic query operation failed",
                innerException: exception
            ),
        };

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(Uri.IsHexDigit);

    private static bool IsSemanticIndexId(string value) =>
        value.Length == 36
        && value.StartsWith("wsi_", StringComparison.Ordinal)
        && value.Skip(4).All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f'
        );

    private static WordToolkitOperationException InvalidInput(
        string message,
        Exception? innerException = null
    ) =>
        new("INVALID_INPUT", message, innerException: innerException);

    private static string? SafeReason(string? message, string? localPath)
    {
        if (string.IsNullOrWhiteSpace(message))
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

    private static string RequireWithinResponseLimit(
        string value,
        int maxCharacters,
        string field
    )
    {
        if (value.Length <= maxCharacters)
        {
            return value;
        }
        throw new WordToolkitOperationException(
            "PACKAGE_LIMIT",
            $"{field} exceeds the public response limit"
        );
    }

    private static string SnakeCase<TEnum>(TEnum value)
        where TEnum : struct, Enum
    {
        var source = value.ToString();
        var builder = new System.Text.StringBuilder(source.Length + 8);
        for (var index = 0; index < source.Length; index++)
        {
            var character = source[index];
            if (
                index > 0
                && char.IsUpper(character)
                && (
                    char.IsLower(source[index - 1])
                    || (
                        index + 1 < source.Length
                        && char.IsLower(source[index + 1])
                    )
                )
            )
            {
                builder.Append('_');
            }
            builder.Append(char.ToLowerInvariant(character));
        }
        return builder.ToString();
    }
}
