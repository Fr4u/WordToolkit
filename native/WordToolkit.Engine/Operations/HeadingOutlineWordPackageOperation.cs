using System.Text;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;

namespace WordToolkit.Engine.Operations;

public sealed class HeadingOutlineWordPackageOperation
{
    private static readonly IReadOnlyDictionary<string, WordStoryKind> StoryKinds =
        Enum.GetValues<WordStoryKind>().ToDictionary(
            kind => SnakeCase(kind),
            kind => kind,
            StringComparer.Ordinal
        );

    private readonly OpcPackageReader _reader;
    private readonly WordSemanticProjector _semanticProjector;
    private readonly WordStyleGraphBuilder _styleBuilder;
    private readonly WordOutlineGraphBuilder _outlineBuilder;

    public HeadingOutlineWordPackageOperation(
        OpcPackageLimits? packageLimits = null,
        WordSemanticProjectionOptions? semanticOptions = null,
        WordStyleGraphOptions? styleOptions = null,
        WordOutlineGraphOptions? outlineOptions = null
    )
    {
        _reader = new OpcPackageReader(packageLimits);
        _semanticProjector = new WordSemanticProjector(semanticOptions);
        _styleBuilder = new WordStyleGraphBuilder(styleOptions);
        _outlineBuilder = new WordOutlineGraphBuilder(outlineOptions);
    }

    public HeadingOutlineInspectionResult Inspect(
        HeadingOutlineInspectionRequest request,
        CancellationToken cancellationToken = default
    )
    {
        if (request is null)
        {
            throw Invalid("Heading-outline inspection request is required");
        }
        Validate(request);
        var path = ResolvePath(request.LocalPath);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var package = _reader.Read(stream, cancellationToken);
            return InspectPackage(
                package,
                Path.GetFileName(path),
                request,
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
            throw MapFailure(exception, request.LocalPath);
        }
    }

    public HeadingOutlineInspectionResult Inspect(
        Stream packageStream,
        string fileName,
        HeadingOutlineInspectionRequest request,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(packageStream);
        ArgumentNullException.ThrowIfNull(request);
        Validate(request with { LocalPath = fileName }, requireLeafName: true);
        long? originalPosition = null;
        try
        {
            if (!packageStream.CanRead || !packageStream.CanSeek)
            {
                throw Invalid("Package stream must be readable and seekable");
            }
            originalPosition = packageStream.Position;
            packageStream.Position = 0;
            var package = _reader.Read(packageStream, cancellationToken);
            var result = InspectPackage(package, fileName, request, cancellationToken);
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
                    // Preserve the operation failure.
                }
            }
        }
    }

    private HeadingOutlineInspectionResult InspectPackage(
        OpcPackageSnapshot package,
        string fileName,
        HeadingOutlineInspectionRequest request,
        CancellationToken cancellationToken
    )
    {
        if (
            request.ExpectedPackageFingerprint is not null
            && !string.Equals(
                request.ExpectedPackageFingerprint,
                package.Fingerprint,
                StringComparison.Ordinal
            )
        )
        {
            throw new WordToolkitOperationException(
                "VERSION_CONFLICT",
                "The Word package changed after the inspected fingerprint was issued"
            );
        }
        var semantic = _semanticProjector.Project(package, cancellationToken);
        var styles = _styleBuilder.Build(package, semantic, cancellationToken);
        var outline = _outlineBuilder.Build(package, semantic, styles, cancellationToken);
        WordStoryKind? storyFilter = request.StoryKind == "all"
            ? null
            : StoryKinds[request.StoryKind];
        var matched = outline.Headings.Where(heading =>
            (storyFilter is null || heading.StoryKind == storyFilter)
            && (!request.HierarchyOnly || heading.HierarchyEligible)
            && (request.MinimumLevel is null || heading.Level >= request.MinimumLevel)
            && (request.MaximumLevel is null || heading.Level <= request.MaximumLevel)
            && (request.ParagraphNodeId is null
                || string.Equals(
                    heading.ParagraphNodeId.Value,
                    request.ParagraphNodeId,
                    StringComparison.Ordinal
                ))
        ).OrderBy(heading => heading.SourceOrder).ToArray();
        var page = request.View == "headings"
            ? matched.Skip(request.Offset).Take(request.MaxItems).ToArray()
            : Array.Empty<WordOutlineHeading>();
        var items = new List<HeadingOutlineInspectionItem>(page.Length);
        for (var index = 0; index < page.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var heading = page[index];
            string? preview = null;
            var previewTruncated = false;
            if (request.TextPreviewCharacters > 0)
            {
                if (!semantic.TryGetNode(heading.ParagraphNodeId, out var paragraph)
                    || paragraph is null)
                {
                    throw new WordOutlineProjectionException(
                        "Outline heading lost its source semantic paragraph."
                    );
                }
                (preview, previewTruncated) = HeadingTextPreview(
                    paragraph,
                    request.TextPreviewCharacters
                );
            }
            items.Add(new HeadingOutlineInspectionItem(
                heading.ParagraphNodeId.Value,
                heading.ParentHeadingParagraphNodeId?.Value,
                heading.PreviousHeadingParagraphNodeId?.Value,
                heading.NextHeadingParagraphNodeId?.Value,
                request.Offset + index,
                heading.Level,
                SnakeCase(heading.LevelSourceKind),
                SnakeCase(heading.StoryKind),
                heading.SourceOrder,
                heading.ChildHeadingCount,
                heading.DescendantHeadingCount,
                request.IncludeSensitive ? heading.TitleCharacterCount : null,
                heading.TitleIsEmpty,
                heading.HierarchyEligible,
                heading.ViewAmbiguous,
                request.IncludeStyles ? Bound(heading.ParagraphStyleId, 253) : null,
                request.IncludeStyles ? Bound(heading.LevelSourceStyleId, 253) : null,
                preview,
                previewTruncated,
                request.IncludeSource ? Bound(heading.SourcePartUri, 512) : null,
                request.IncludeSource ? heading.SourceElementOrdinal : null
            ));
        }

        var filteredIssues = outline.Issues.Where(issue =>
        {
            if (request.View != "issues")
            {
                return false;
            }
            if (issue.ParagraphNodeId is not { } paragraphId)
            {
                return storyFilter is null;
            }
            return outline.TryGetParagraph(paragraphId, out var paragraph)
                && paragraph is not null
                && (storyFilter is null || paragraph.StoryKind == storyFilter);
        }).ToArray();
        var issuePage = filteredIssues
            .Skip(request.Offset)
            .Take(Math.Min(request.MaxItems, HeadingOutlineWordPackageContract.MaximumReturnedIssues))
            .Select(issue => Issue(issue, outline, request.IncludeSource))
            .ToArray();
        var nextOffset = request.View switch
        {
            "headings" when request.Offset + page.Length < matched.Length => request.Offset + page.Length,
            "issues" when request.Offset + issuePage.Length < filteredIssues.Length => request.Offset + issuePage.Length,
            _ => (int?)null,
        };
        var omissions = new List<string>();
        if (outline.SkippedHeadingCount > 0)
        {
            omissions.Add("revision_or_mce_view_selection");
        }
        if (styles.StylesWithEffectsPartUri is not null)
        {
            omissions.Add("styles_with_effects_word_execution");
        }

        return new HeadingOutlineInspectionResult(
            HeadingOutlineWordPackageContract.Contract,
            fileName,
            package.Fingerprint,
            semantic.MainPartUri,
            request.View,
            request.StoryKind,
            outline.ExaminedParagraphCount,
            outline.ExaminedParagraphCount - outline.UnresolvedParagraphCount,
            outline.BodyTextParagraphCount,
            outline.UnresolvedParagraphCount,
            outline.HeadingCount,
            outline.HierarchyHeadingCount,
            outline.RootHeadingCount,
            outline.SkippedHeadingCount,
            outline.StoryCount,
            outline.Issues.Count,
            outline.AnalysisExecutionComplete,
            outline.OutlineCoverageComplete && styles.StylesWithEffectsPartUri is null,
            styles.StylesWithEffectsPartUri is not null,
            omissions,
            matched.Length,
            request.Offset,
            items.Count,
            nextOffset,
            items,
            filteredIssues.Length,
            issuePage.Length,
            request.View == "issues" && request.Offset + issuePage.Length < filteredIssues.Length,
            issuePage,
            new HeadingOutlineInspectionDisclosure(
                items.Any(item => item.TextPreview is not null),
                items.Any(item => item.ParagraphStyleId is not null || item.LevelSourceStyleId is not null),
                items.Any(item => item.SourcePartUri is not null)
                    || issuePage.Any(issue => issue.SourcePartUri is not null),
                RawXmlReturned: false,
                ExternalRelationshipsFollowed: false,
                MutationPerformed: false,
                WordOpened: false,
                DocumentContentIsUntrusted: true
            )
        );
    }

    private static HeadingOutlineInspectionIssue Issue(
        WordOutlineIssue issue,
        WordOutlineGraph graph,
        bool includeSource
    )
    {
        WordOutlineParagraph? paragraph = null;
        if (issue.ParagraphNodeId is { } paragraphId)
        {
            graph.TryGetParagraph(paragraphId, out paragraph);
        }
        return new HeadingOutlineInspectionIssue(
            Bound(issue.Code, 128) ?? "OUTLINE_ISSUE",
            SnakeCase(issue.Severity),
            Bound(PublicIssueMessage(issue), 512) ?? "Outline issue",
            issue.ParagraphNodeId?.Value,
            paragraph is null ? null : SnakeCase(paragraph.StoryKind),
            issue.Level,
            issue.PreviousLevel,
            includeSource ? Bound(paragraph?.SourcePartUri, 512) : null,
            includeSource ? paragraph?.SourceElementOrdinal : null
        );
    }

    private static string PublicIssueMessage(WordOutlineIssue issue) => issue.Code switch
    {
        "OUTLINE_LEVEL_UNRESOLVED" =>
            "A paragraph outline level could not be resolved from valid direct, style, or document-default evidence.",
        _ => issue.Message,
    };

    private static (string Text, bool Truncated) HeadingTextPreview(
        WordSemanticNode paragraph,
        int maximumCharacters
    )
    {
        var builder = new StringBuilder(Math.Min(maximumCharacters + 1, 256));
        foreach (var node in paragraph.DescendantsAndSelf())
        {
            var value = node.Kind switch
            {
                WordSemanticNodeKind.Text => node.Text,
                WordSemanticNodeKind.Tab => "\t",
                WordSemanticNodeKind.Break => "\n",
                _ => null,
            };
            if (string.IsNullOrEmpty(value))
            {
                continue;
            }
            var remaining = maximumCharacters + 1 - builder.Length;
            if (remaining <= 0)
            {
                break;
            }
            builder.Append(value.AsSpan(0, Math.Min(remaining, value.Length)));
        }
        var truncated = builder.Length > maximumCharacters;
        if (truncated)
        {
            builder.Length = maximumCharacters;
        }
        return (builder.ToString(), truncated);
    }

    private static void Validate(
        HeadingOutlineInspectionRequest request,
        bool requireLeafName = false
    )
    {
        if (string.IsNullOrWhiteSpace(request.LocalPath)
            || request.LocalPath.Length > HeadingOutlineWordPackageContract.MaximumLocalPathCharacters)
        {
            throw Invalid("local_path must be a non-empty bounded path");
        }
        if (requireLeafName && !string.Equals(Path.GetFileName(request.LocalPath), request.LocalPath, StringComparison.Ordinal))
        {
            throw Invalid("Stream file name must be a leaf name");
        }
        if (!InspectWordPackageContract.IsSupportedFileName(request.LocalPath))
        {
            throw Invalid("local_path must use .docx, .docm, .dotx, or .dotm");
        }
        if (request.View is not "summary" and not "headings" and not "issues")
        {
            throw Invalid("view must be summary, headings, or issues");
        }
        if (request.StoryKind != "all" && !StoryKinds.ContainsKey(request.StoryKind))
        {
            throw Invalid("story_kind must be all or a supported Word story kind");
        }
        if (request.ExpectedPackageFingerprint is not null
            && !IsSha256(request.ExpectedPackageFingerprint))
        {
            throw Invalid("expected_package_fingerprint must be exactly 64 hexadecimal characters");
        }
        if (request.Offset < 0)
        {
            throw Invalid("offset must be non-negative");
        }
        if (request.Offset > 0 && request.ExpectedPackageFingerprint is null)
        {
            throw Invalid("expected_package_fingerprint is required when offset is positive");
        }
        if (request.MaxItems is < 1 or > HeadingOutlineWordPackageContract.MaximumMaxItems)
        {
            throw Invalid($"max_items must be between 1 and {HeadingOutlineWordPackageContract.MaximumMaxItems}");
        }
        if (request.MinimumLevel is < 1 or > 9 || request.MaximumLevel is < 1 or > 9)
        {
            throw Invalid("minimum_level and maximum_level must be between 1 and 9");
        }
        if (request.MinimumLevel > request.MaximumLevel)
        {
            throw Invalid("minimum_level cannot exceed maximum_level");
        }
        if (request.ParagraphNodeId is not null
            && !SemanticNodeId.HasValidSyntax(request.ParagraphNodeId))
        {
            throw Invalid("paragraph_node_id is not a valid semantic node ID");
        }
        if (request.TextPreviewCharacters is < 0 or > HeadingOutlineWordPackageContract.MaximumPreviewCharacters)
        {
            throw Invalid($"text_preview_chars must be between 0 and {HeadingOutlineWordPackageContract.MaximumPreviewCharacters}");
        }
        if (request.TextPreviewCharacters > 0 && !request.IncludeSensitive)
        {
            throw Invalid("include_sensitive=true is required when text_preview_chars is positive");
        }
        if (request.View == "summary" && request.Offset != 0)
        {
            throw Invalid("summary view does not support a positive offset");
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

    private static WordToolkitOperationException MapFailure(Exception exception, string? localPath) =>
        exception switch
        {
            WordOutlineLimitException or WordStyleLimitException or WordSemanticLimitException =>
                new WordToolkitOperationException(
                    "PACKAGE_LIMIT",
                    "The Word heading outline exceeds a bounded safety limit",
                    SafeReason(exception.Message, localPath),
                    innerException: exception
                ),
            WordOutlineProjectionException or WordStyleProjectionException or WordSemanticProjectionException =>
                new WordToolkitOperationException(
                    "INVALID_WORD_PACKAGE",
                    "The package cannot be projected safely as a Word heading outline",
                    SafeReason(exception.Message, localPath),
                    innerException: exception
                ),
            OpcPackageLimitException => new WordToolkitOperationException(
                "PACKAGE_LIMIT",
                "The package exceeds a bounded OPC safety limit",
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
                "The Word heading outline package could not be read",
                SafeReason(exception.Message, localPath),
                retryable: true,
                innerException: exception
            ),
            ArgumentException => Invalid(
                SafeReason(exception.Message, localPath) ?? "Invalid heading-outline request",
                exception
            ),
            _ => new WordToolkitOperationException(
                "INTERNAL_ERROR",
                "The Word heading-outline inspection failed",
                innerException: exception
            ),
        };

    private static bool IsSha256(string value) => value.Length == 64
        && value.All(character => char.IsAsciiHexDigit(character));

    private static string SnakeCase<T>(T value) where T : struct, Enum
    {
        var source = value.ToString();
        var result = new StringBuilder(source.Length + 8);
        for (var index = 0; index < source.Length; index++)
        {
            var character = source[index];
            if (char.IsUpper(character) && index != 0)
            {
                result.Append('_');
            }
            result.Append(char.ToLowerInvariant(character));
        }
        return result.ToString();
    }

    private static string? Bound(string? value, int maximum) =>
        value is null || value.Length <= maximum ? value : value[..maximum] + "…";

    private static string? SafeReason(string? message, string? localPath)
    {
        if (message is null)
        {
            return null;
        }
        var safe = message;
        if (!string.IsNullOrWhiteSpace(localPath))
        {
            safe = safe.Replace(localPath, "<redacted>", StringComparison.OrdinalIgnoreCase);
        }
        return Bound(safe, 512);
    }

    private static WordToolkitOperationException Invalid(
        string message,
        Exception? innerException = null
    ) => new("INVALID_INPUT", message, innerException: innerException);
}
