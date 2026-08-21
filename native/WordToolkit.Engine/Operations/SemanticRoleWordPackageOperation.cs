using System.Text;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;

namespace WordToolkit.Engine.Operations;

public sealed class SemanticRoleWordPackageOperation
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
    private readonly WordContentControlBindingGraphBuilder _contentControlBuilder;
    private readonly WordSemanticRoleGraphBuilder _roleBuilder;

    public SemanticRoleWordPackageOperation(
        OpcPackageLimits? packageLimits = null,
        WordSemanticProjectionOptions? semanticOptions = null,
        WordStyleGraphOptions? styleOptions = null,
        WordContentControlBindingGraphOptions? contentControlOptions = null,
        WordSemanticRoleGraphOptions? roleOptions = null
    )
    {
        _reader = new OpcPackageReader(packageLimits);
        _semanticProjector = new WordSemanticProjector(semanticOptions);
        _styleBuilder = new WordStyleGraphBuilder(styleOptions);
        _contentControlBuilder = new WordContentControlBindingGraphBuilder(
            contentControlOptions
        );
        _roleBuilder = new WordSemanticRoleGraphBuilder(roleOptions);
    }

    public SemanticRoleInspectionResult Inspect(
        SemanticRoleInspectionRequest request,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(request);
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

    public SemanticRoleInspectionResult Inspect(
        Stream packageStream,
        string fileName,
        SemanticRoleInspectionRequest request,
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

    private SemanticRoleInspectionResult InspectPackage(
        OpcPackageSnapshot package,
        string fileName,
        SemanticRoleInspectionRequest request,
        CancellationToken cancellationToken
    )
    {
        if (request.ExpectedPackageFingerprint is not null
            && !string.Equals(
                request.ExpectedPackageFingerprint,
                package.Fingerprint,
                StringComparison.OrdinalIgnoreCase
            ))
        {
            throw new WordToolkitOperationException(
                "VERSION_CONFLICT",
                "The Word package changed after the inspected fingerprint was issued"
            );
        }

        var semantic = _semanticProjector.Project(package, cancellationToken);
        var styles = _styleBuilder.Build(package, semantic, cancellationToken);
        var contentControls = _contentControlBuilder.Build(
            package,
            semantic,
            cancellationToken
        );
        var graph = _roleBuilder.Build(
            package,
            semantic,
            styles,
            contentControls,
            cancellationToken
        );
        var nodes = semantic.Nodes.ToDictionary(node => node.Id);
        WordStoryKind? storyFilter = request.StoryKind == "all"
            ? null
            : StoryKinds[request.StoryKind];
        var requestedRoles = request.Roles.ToHashSet();
        var matched = graph.Candidates.Where(candidate =>
            (storyFilter is null || candidate.StoryKind == storyFilter)
            && candidate.Evidence.Any(evidence => requestedRoles.Contains(evidence.Role))
            && (!request.UsableOnly || candidate.UsableAsSemanticRole)
            && MeetsMinimumEvidence(candidate, request.MinimumEvidence)
            && (request.CandidateId is null
                || string.Equals(candidate.Id, request.CandidateId, StringComparison.Ordinal))
            && (request.ParagraphNodeId is null
                || string.Equals(
                    candidate.ParagraphNodeId.Value,
                    request.ParagraphNodeId,
                    StringComparison.Ordinal
                ))
            && (request.Classification is null
                || string.Equals(
                    SnakeCase(candidate.Classification),
                    request.Classification,
                    StringComparison.Ordinal
                ))
        ).OrderBy(candidate => candidate.SourceOrder).ToArray();
        var page = request.View == "candidates"
            ? matched.Skip(request.Offset).Take(request.MaxItems).ToArray()
            : Array.Empty<WordSemanticRoleCandidate>();
        var items = page.Select(candidate => Item(
            candidate,
            nodes,
            request
        )).ToArray();

        var filteredIssues = graph.Issues.Where(issue =>
            request.View == "issues"
            && (storyFilter is null || issue.StoryKind == storyFilter)
        ).ToArray();
        var issuePage = filteredIssues.Skip(request.Offset)
            .Take(Math.Min(
                request.MaxItems,
                SemanticRoleWordPackageContract.MaximumReturnedIssues
            ))
            .Select(Issue)
            .ToArray();
        var nextOffset = request.View switch
        {
            "candidates" when request.Offset + page.Length < matched.Length =>
                request.Offset + page.Length,
            "issues" when request.Offset + issuePage.Length < filteredIssues.Length =>
                request.Offset + issuePage.Length,
            _ => (int?)null,
        };

        var omissions = new List<string> { "unstated_author_conventions" };
        if (graph.AmbiguousParagraphCount > 0)
        {
            omissions.Add("revision_or_mce_view_selection");
        }
        if (styles.StylesWithEffectsPartUri is not null)
        {
            omissions.Add("styles_with_effects_word_execution");
        }
        if (contentControls.Issues.Any(issue =>
            issue.Severity == WordContentControlIssueSeverity.Error
        ))
        {
            omissions.Add("content_control_binding_diagnostics");
        }
        if (!graph.ModeledEvidenceCoverageComplete)
        {
            omissions.Add("modeled_evidence_incomplete");
        }

        var summaries = request.Roles.Select(role =>
        {
            var roleCandidates = graph.Candidates.Where(candidate =>
                (storyFilter is null || candidate.StoryKind == storyFilter)
                && candidate.Evidence.Any(evidence => evidence.Role == role)
            ).ToArray();
            return new SemanticRoleInspectionSummary(
                WordSemanticRoleGraphBuilder.RoleToken(role),
                roleCandidates.Length,
                roleCandidates.Count(candidate =>
                    candidate.UsableAsSemanticRole && candidate.Role == role
                ),
                roleCandidates.Count(candidate =>
                    candidate.Role == role
                    && candidate.Classification == WordSemanticRoleClassification.Declared
                ),
                roleCandidates.Count(candidate =>
                    candidate.Role == role
                    && candidate.Classification
                        == WordSemanticRoleClassification.StyleConvention
                ),
                roleCandidates.Count(candidate =>
                    candidate.Role == role
                    && candidate.Classification
                        == WordSemanticRoleClassification.LexicalCandidate
                ),
                roleCandidates.Count(candidate =>
                    candidate.Classification == WordSemanticRoleClassification.Conflicting
                )
            );
        }).ToArray();

        return new SemanticRoleInspectionResult(
            SemanticRoleWordPackageContract.Contract,
            fileName,
            package.Fingerprint,
            semantic.MainPartUri,
            graph.Profile,
            request.View,
            request.StoryKind,
            request.Roles.Select(WordSemanticRoleGraphBuilder.RoleToken).ToArray(),
            request.MinimumEvidence,
            request.UsableOnly,
            graph.ExaminedParagraphCount,
            graph.EligibleParagraphCount,
            graph.AmbiguousParagraphCount,
            graph.Candidates.Count,
            graph.Candidates.Count(candidate => candidate.UsableAsSemanticRole),
            graph.Candidates.Count(candidate =>
                candidate.Classification == WordSemanticRoleClassification.Conflicting
            ),
            graph.Issues.Count,
            graph.AnalysisExecutionComplete,
            SemanticRoleCoverageComplete: false,
            SemanticCompletenessClaimed: false,
            StylesWithEffectsPresent: styles.StylesWithEffectsPartUri is not null,
            CoverageOmissions: omissions.Distinct(StringComparer.Ordinal).ToArray(),
            RoleSummaries: summaries,
            MatchedCandidateCount: matched.Length,
            Offset: request.Offset,
            ReturnedItemCount: items.Length,
            NextOffset: nextOffset,
            Items: items,
            MatchedIssueCount: filteredIssues.Length,
            ReturnedIssueCount: issuePage.Length,
            IssuePageTruncated: request.View == "issues"
                && request.Offset + issuePage.Length < filteredIssues.Length,
            Issues: issuePage,
            Disclosure: new SemanticRoleInspectionDisclosure(
                TextReturned: items.Any(item => item.TextPreview is not null),
                EvidenceReturned: request.IncludeEvidence && items.Length > 0,
                StylesReturned: request.IncludeEvidence
                    && request.IncludeStyles
                    && items.Any(item => item.Evidence?.Any(evidence =>
                        evidence.StyleId is not null
                    ) == true),
                DeclarationsReturned: request.IncludeEvidence
                    && request.IncludeDeclarations
                    && items.Any(item => item.Evidence?.Any(evidence =>
                        evidence.ContentControlId is not null
                    ) == true),
                HashesReturned: request.IncludeHashes && items.Length > 0,
                SourceReturned: items.Any(item => item.SourcePartUri is not null),
                RawXmlReturned: false,
                CustomXmlValuesReturned: false,
                ExternalRelationshipsFollowed: false,
                MutationPerformed: false,
                WordOpened: false,
                DocumentContentIsUntrusted: true
            )
        );
    }

    private static SemanticRoleInspectionItem Item(
        WordSemanticRoleCandidate candidate,
        IReadOnlyDictionary<SemanticNodeId, WordSemanticNode> nodes,
        SemanticRoleInspectionRequest request
    )
    {
        if (!nodes.TryGetValue(candidate.ParagraphNodeId, out var paragraph))
        {
            throw new WordSemanticRoleProjectionException(
                "A semantic-role candidate lost its source paragraph."
            );
        }
        string? preview = null;
        var previewTruncated = false;
        if (request.TextPreviewCharacters > 0)
        {
            (preview, previewTruncated) = TextPreview(
                paragraph,
                request.TextPreviewCharacters
            );
        }
        var evidence = request.IncludeEvidence
            ? candidate.Evidence.Select(item => new SemanticRoleInspectionEvidence(
                item.Id,
                SnakeCase(item.Kind),
                WordSemanticRoleGraphBuilder.RoleToken(item.Role),
                item.AuthorDeclared,
                request.IncludeDeclarations ? item.ContentControlId : null,
                request.IncludeStyles ? Bound(item.StyleId, 253) : null,
                request.IncludeHashes ? item.ValueFingerprint : null
            )).ToArray()
            : null;
        return new SemanticRoleInspectionItem(
            candidate.Id,
            candidate.Fingerprint,
            candidate.ParagraphNodeId.Value,
            candidate.Role is null
                ? null
                : WordSemanticRoleGraphBuilder.RoleToken(candidate.Role.Value),
            SnakeCase(candidate.Classification),
            SnakeCase(candidate.StoryKind),
            candidate.SourceOrder,
            request.IncludeSensitive ? candidate.ParagraphCharacterCount : null,
            request.IncludeSensitive ? candidate.LabelCharacterCount : null,
            request.IncludeHashes ? candidate.ParagraphTextFingerprint : null,
            candidate.ViewAmbiguous,
            candidate.UsableAsSemanticRole,
            candidate.Evidence.Count,
            evidence,
            preview,
            previewTruncated,
            request.IncludeSource ? Bound(paragraph.SourcePartUri, 512) : null,
            request.IncludeSource ? paragraph.SourceElementOrdinal : null
        );
    }

    private static SemanticRoleInspectionIssue Issue(WordSemanticRoleIssue issue) => new(
        Bound(issue.Code, 128) ?? "SEMANTIC_ROLE_ISSUE",
        SnakeCase(issue.Severity),
        Bound(issue.Message, 512) ?? "Semantic-role issue",
        issue.ParagraphNodeId?.Value,
        issue.StoryKind is null ? null : SnakeCase(issue.StoryKind.Value),
        issue.SourceOrder,
        issue.CandidateId
    );

    private static bool MeetsMinimumEvidence(
        WordSemanticRoleCandidate candidate,
        string minimumEvidence
    ) => minimumEvidence switch
    {
        "declared_only" => candidate.Classification
            == WordSemanticRoleClassification.Declared,
        "declared_or_style" => candidate.Classification
            is WordSemanticRoleClassification.Declared
                or WordSemanticRoleClassification.StyleConvention,
        _ => true,
    };

    private static (string Text, bool Truncated) TextPreview(
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
        SemanticRoleInspectionRequest request,
        bool requireLeafName = false
    )
    {
        if (string.IsNullOrWhiteSpace(request.LocalPath)
            || request.LocalPath.Length > SemanticRoleWordPackageContract.MaximumLocalPathCharacters)
        {
            throw Invalid("local_path must be a non-empty bounded path");
        }
        if (requireLeafName
            && !string.Equals(
                Path.GetFileName(request.LocalPath),
                request.LocalPath,
                StringComparison.Ordinal
            ))
        {
            throw Invalid("Stream file name must be a leaf name");
        }
        if (!InspectWordPackageContract.IsSupportedFileName(request.LocalPath))
        {
            throw Invalid("local_path must use .docx, .docm, .dotx, or .dotm");
        }
        if (request.View is not "summary" and not "candidates" and not "issues")
        {
            throw Invalid("view must be summary, candidates, or issues");
        }
        if (request.StoryKind != "all" && !StoryKinds.ContainsKey(request.StoryKind))
        {
            throw Invalid("story_kind must be all or a supported Word story kind");
        }
        if (request.ExpectedPackageFingerprint is not null
            && !IsSha256(request.ExpectedPackageFingerprint))
        {
            throw Invalid(
                "expected_package_fingerprint must be exactly 64 hexadecimal characters"
            );
        }
        if (request.Roles.Count is < 1 or > SemanticRoleWordPackageContract.MaximumRoles
            || request.Roles.Distinct().Count() != request.Roles.Count)
        {
            throw Invalid(
                $"roles must contain between 1 and {SemanticRoleWordPackageContract.MaximumRoles} unique values"
            );
        }
        if (request.MinimumEvidence is not "any"
            and not "declared_or_style"
            and not "declared_only")
        {
            throw Invalid(
                "minimum_evidence must be any, declared_or_style, or declared_only"
            );
        }
        if (request.Classification is not null
            && request.Classification is not "declared"
                and not "style_convention"
                and not "lexical_candidate"
                and not "conflicting")
        {
            throw Invalid("classification is unsupported");
        }
        if (request.CandidateId is not null
            && (request.CandidateId.Length > 128
                || !request.CandidateId.StartsWith("wdsr_", StringComparison.Ordinal)))
        {
            throw Invalid("candidate_id is not a valid semantic-role candidate ID");
        }
        if (request.ParagraphNodeId is not null
            && !SemanticNodeId.HasValidSyntax(request.ParagraphNodeId))
        {
            throw Invalid("paragraph_node_id is not a valid semantic node ID");
        }
        if (request.Offset < 0)
        {
            throw Invalid("offset must be non-negative");
        }
        if (request.Offset > 0 && request.ExpectedPackageFingerprint is null)
        {
            throw Invalid(
                "expected_package_fingerprint is required when offset is positive"
            );
        }
        if (request.MaxItems is < 1 or > SemanticRoleWordPackageContract.MaximumMaxItems)
        {
            throw Invalid(
                $"max_items must be between 1 and {SemanticRoleWordPackageContract.MaximumMaxItems}"
            );
        }
        if (request.TextPreviewCharacters is < 0
            or > SemanticRoleWordPackageContract.MaximumPreviewCharacters)
        {
            throw Invalid(
                $"text_preview_chars must be between 0 and {SemanticRoleWordPackageContract.MaximumPreviewCharacters}"
            );
        }
        if (request.TextPreviewCharacters > 0 && !request.IncludeSensitive)
        {
            throw Invalid(
                "include_sensitive=true is required when text_preview_chars is positive"
            );
        }
        if ((request.IncludeStyles || request.IncludeDeclarations)
            && !request.IncludeEvidence)
        {
            throw Invalid(
                "include_evidence=true is required for style or declaration evidence"
            );
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

    private static WordToolkitOperationException MapFailure(
        Exception exception,
        string? localPath
    ) => exception switch
    {
        WordSemanticRoleLimitException
            or WordContentControlLimitException
            or WordStyleLimitException
            or WordSemanticLimitException => new WordToolkitOperationException(
                "PACKAGE_LIMIT",
                "The Word semantic-role graph exceeds a bounded safety limit",
                SafeReason(exception.Message, localPath),
                innerException: exception
            ),
        WordSemanticRoleProjectionException
            or WordContentControlProjectionException
            or WordStyleProjectionException
            or WordSemanticProjectionException => new WordToolkitOperationException(
                "INVALID_WORD_PACKAGE",
                "The package cannot be projected safely as semantic-role evidence",
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
            "The Word semantic-role package could not be read",
            SafeReason(exception.Message, localPath),
            retryable: true,
            innerException: exception
        ),
        ArgumentException => Invalid(
            SafeReason(exception.Message, localPath) ?? "Invalid semantic-role request",
            exception
        ),
        _ => new WordToolkitOperationException(
            "INTERNAL_ERROR",
            "The Word semantic-role inspection failed",
            innerException: exception
        ),
    };

    private static bool IsSha256(string value) => value.Length == 64
        && value.All(character => char.IsAsciiHexDigit(character));

    private static string SnakeCase<T>(T value)
        where T : struct, Enum
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
