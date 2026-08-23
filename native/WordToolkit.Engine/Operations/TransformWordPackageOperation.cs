using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;

namespace WordToolkit.Engine.Operations;

public enum WordPackageTransformKind
{
    ReplaceFirstTextOccurrence,
    AcceptAllTrackedChanges,
    RejectAllTrackedChanges,
}

public static class TransformWordPackageContract
{
    public const string OperationName = "transform_ooxml_package";
    public const string Contract = "wordtoolkit.transform_ooxml_package/1.0";
    public const int MaximumTextCharacters = 1_000_000;
    public const int MaximumReviewDecisions = 10_000;

    public static string Name(WordPackageTransformKind kind) => kind switch
    {
        WordPackageTransformKind.ReplaceFirstTextOccurrence =>
            "replace_first_text_occurrence",
        WordPackageTransformKind.AcceptAllTrackedChanges =>
            "accept_all_tracked_changes",
        WordPackageTransformKind.RejectAllTrackedChanges =>
            "reject_all_tracked_changes",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    public static bool TryParse(string? value, out WordPackageTransformKind kind)
    {
        kind = value switch
        {
            "replace_first_text_occurrence" =>
                WordPackageTransformKind.ReplaceFirstTextOccurrence,
            "accept_all_tracked_changes" =>
                WordPackageTransformKind.AcceptAllTrackedChanges,
            "reject_all_tracked_changes" =>
                WordPackageTransformKind.RejectAllTrackedChanges,
            _ => default,
        };
        return value is "replace_first_text_occurrence"
            or "accept_all_tracked_changes"
            or "reject_all_tracked_changes";
    }
}

public sealed record TransformWordPackageRequest(
    string InputPath,
    string OutputPath,
    WordPackageTransformKind Kind,
    string? FindText = null,
    string? ReplaceText = null
);

public sealed record TransformWordPackageResult(
    string OperationContract,
    string Operation,
    string InputFileName,
    string OutputFileName,
    string BasePackageFingerprint,
    string ResultPackageFingerprint,
    bool Changed,
    int ChangedPartCount,
    IReadOnlyCollection<string> ChangedEntryNames,
    int? MatchOffset,
    int? MatchedTextNodeCount,
    int? SubmittedRevisionCount,
    int? ChangedRevisionCount,
    int? RemovedMoveMarkerCount,
    int RemainingRevisionCount,
    bool StructurallyValid,
    bool DigitalSignaturesPresent,
    WordPackageProtectionRiskAssessment Protection,
    bool RawXmlReturned,
    bool WordOpened
);

public sealed record TransformWordPackageProtectionBlockDetails(
    string Operation,
    IReadOnlyList<string> BlockCodes,
    WordPackageProtectionRiskAssessment Protection
);

/// <summary>
/// Applies one bounded, source-preserving WordprocessingML transform to a new
/// package. The operation is independent of MCP and Microsoft Word automation.
/// Unknown ZIP entries and untouched XML bytes are carried through verbatim.
/// </summary>
public sealed class TransformWordPackageOperation
{
    private readonly OpcPackageReader _reader;
    private readonly OpcPackageSerializer _serializer;
    private readonly OpcAtomicPackageWriter _writer;

    public TransformWordPackageOperation(OpcPackageLimits? limits = null)
    {
        _reader = new OpcPackageReader(limits);
        _serializer = new OpcPackageSerializer();
        _writer = new OpcAtomicPackageWriter(_reader, _serializer);
    }

    public TransformWordPackageResult Execute(
        TransformWordPackageRequest request,
        CancellationToken cancellationToken = default
    )
    {
        if (request is null)
        {
            throw InvalidInput("Transform request is required");
        }

        var (inputPath, outputPath) = ValidateAndResolve(request);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var package = _reader.Read(inputPath, cancellationToken);
            if (!package.IsStructurallyValid)
            {
                throw new WordToolkitOperationException(
                    "INVALID_PACKAGE",
                    "The input package has structural OPC errors"
                );
            }
            if (WordPackagePatchRiskAnalyzer.HasDigitalSignatures(package))
            {
                throw new WordToolkitOperationException(
                    "SIGNED_PACKAGE",
                    "The transform is blocked because the package contains digital signatures"
                );
            }

            var prepared = Prepare(package, request, cancellationToken);
            var candidate = ValidateCandidate(
                package,
                prepared,
                request.Kind,
                cancellationToken
            );
            var protection = WordPackagePatchRiskAnalyzer.AssessProtection(
                package,
                prepared.SourceSemantic,
                candidate.Package,
                candidate.Semantic,
                prepared.Mutation.HasChanges,
                cancellationToken
            );
            var protectionBlocks = ProtectionBlockCodes(protection);
            if (protectionBlocks.Count != 0)
            {
                throw new WordToolkitOperationException(
                    "EDIT_POLICY_BLOCKED",
                    "The package transform is blocked by Word editing protection",
                    details: new TransformWordPackageProtectionBlockDetails(
                        TransformWordPackageContract.Name(request.Kind),
                        protectionBlocks,
                        protection
                    )
                );
            }

            var write = _writer.Write(
                outputPath,
                prepared.Mutation,
                new OpcAtomicWriteOptions
                {
                    ExpectedResultFingerprint = candidate.Package.Fingerprint,
                    RequireNewDestination = true,
                    KeepBackup = false,
                }
            );

            return new TransformWordPackageResult(
                TransformWordPackageContract.Contract,
                TransformWordPackageContract.Name(request.Kind),
                Path.GetFileName(inputPath),
                Path.GetFileName(outputPath),
                package.Fingerprint,
                write.Fingerprint,
                prepared.Mutation.HasChanges,
                prepared.ChangedPartCount,
                write.ChangedEntryNames,
                prepared.MatchOffset,
                prepared.MatchedTextNodeCount,
                prepared.SubmittedRevisionCount,
                prepared.ChangedRevisionCount,
                prepared.RemovedMoveMarkerCount,
                prepared.RemainingRevisionCount,
                candidate.Package.IsStructurallyValid,
                DigitalSignaturesPresent: false,
                protection,
                RawXmlReturned: false,
                WordOpened: false
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
            throw MapFailure(exception);
        }
    }

    private PreparedTransform Prepare(
        OpcPackageSnapshot package,
        TransformWordPackageRequest request,
        CancellationToken cancellationToken
    )
    {
        var semantic = new WordSemanticProjector().Project(package, cancellationToken);
        return request.Kind switch
        {
            WordPackageTransformKind.ReplaceFirstTextOccurrence => PrepareReplacement(
                package,
                semantic,
                request.FindText!,
                request.ReplaceText!,
                cancellationToken
            ),
            WordPackageTransformKind.AcceptAllTrackedChanges => PrepareReview(
                package,
                semantic,
                WordReviewDecision.Accept,
                cancellationToken
            ),
            WordPackageTransformKind.RejectAllTrackedChanges => PrepareReview(
                package,
                semantic,
                WordReviewDecision.Reject,
                cancellationToken
            ),
            _ => throw InvalidInput("Unsupported transform kind"),
        };
    }

    private static PreparedTransform PrepareReplacement(
        OpcPackageSnapshot package,
        WordSemanticDocument semantic,
        string findText,
        string replaceText,
        CancellationToken cancellationToken
    )
    {
        foreach (
            var paragraph in semantic.Nodes.Where(node =>
                node.Kind == WordSemanticNodeKind.Paragraph
                && string.Equals(
                    node.SourcePartUri,
                    semantic.MainPartUri,
                    StringComparison.Ordinal
                )
            )
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            var descendants = ParagraphDescendants(paragraph);
            var revisionPresent = descendants.Any(node =>
                node.Kind == WordSemanticNodeKind.Revision
            );
            var alternateContentPresent = descendants.Any(node =>
                node.Kind == WordSemanticNodeKind.AlternateContent
            );
            var textNodes = descendants.Where(IsWordTextNode)
                .OrderBy(node => node.SourceOrder)
                .ToArray();
            if (textNodes.Length == 0)
            {
                continue;
            }

            var paragraphText = string.Concat(textNodes.Select(node => node.Text));
            var matchOffset = paragraphText.IndexOf(findText, StringComparison.Ordinal);
            if (matchOffset < 0)
            {
                continue;
            }
            if (revisionPresent)
            {
                throw Unsupported(
                    "The first matching paragraph contains tracked revision markup; plain replacement would have ambiguous visible-text semantics"
                );
            }
            if (alternateContentPresent)
            {
                throw Unsupported(
                    "The first matching paragraph contains Markup Compatibility alternatives; branch-visible text cannot be selected without an application profile"
                );
            }

            var commands = BuildReplacementCommands(
                textNodes,
                matchOffset,
                findText.Length,
                replaceText
            );
            var plan = new WordSemanticTransactionPlanner(
                new WordSemanticTransactionOptions
                {
                    MaxCommands = Math.Max(commands.Count, 1),
                    MaxTotalReplacementCharacters =
                        TransformWordPackageContract.MaximumTextCharacters,
                }
            ).PlanTextReplacements(package, semantic, commands, cancellationToken);
            return new PreparedTransform(
                plan.CreateMutation(package),
                plan.ChangedPartCount,
                matchOffset,
                commands.Count,
                SubmittedRevisionCount: null,
                ChangedRevisionCount: null,
                RemovedMoveMarkerCount: null,
                RemainingRevisionCount: 0,
                ExpectedParagraphPartUri: paragraph.SourcePartUri,
                ExpectedParagraphElementOrdinal: paragraph.SourceElementOrdinal,
                ExpectedParagraphText: paragraphText.Remove(matchOffset, findText.Length)
                    .Insert(matchOffset, replaceText),
                SourceSemantic: semantic
            );
        }

        throw new WordToolkitOperationException(
            "TEXT_NOT_FOUND",
            "The requested text does not occur in a main-document paragraph"
        );
    }

    private static IReadOnlyList<WordTextReplacementCommand> BuildReplacementCommands(
        IReadOnlyList<WordSemanticNode> textNodes,
        int matchOffset,
        int matchLength,
        string replacement
    )
    {
        var matchEnd = checked(matchOffset + matchLength);
        var commands = new List<WordTextReplacementCommand>();
        var nodeStart = 0;
        var replacementWritten = false;
        foreach (var node in textNodes)
        {
            var value = node.Text ?? string.Empty;
            var nodeEnd = checked(nodeStart + value.Length);
            var overlapStart = Math.Max(matchOffset, nodeStart);
            var overlapEnd = Math.Min(matchEnd, nodeEnd);
            if (overlapStart < overlapEnd)
            {
                var localStart = overlapStart - nodeStart;
                var localEnd = overlapEnd - nodeStart;
                var changed = value[..localStart]
                    + (replacementWritten ? string.Empty : replacement)
                    + value[localEnd..];
                replacementWritten = true;
                commands.Add(new WordTextReplacementCommand(node.Id, changed, value));
            }
            nodeStart = nodeEnd;
        }

        if (!replacementWritten)
        {
            throw new WordToolkitOperationException(
                "INTERNAL_ERROR",
                "The paragraph match could not be bound to source text nodes"
            );
        }
        return commands;
    }

    private static PreparedTransform PrepareReview(
        OpcPackageSnapshot package,
        WordSemanticDocument semantic,
        WordReviewDecision decision,
        CancellationToken cancellationToken
    )
    {
        var graph = new WordReviewGraphBuilder().Build(
            package,
            semantic,
            cancellationToken
        );
        if (graph.Revisions.Count == 0)
        {
            if (graph.MoveRanges.Count != 0 || graph.Moves.Count != 0)
            {
                throw Unsupported(
                    "The package contains move-range markup without a transformable tracked revision"
                );
            }
            return new PreparedTransform(
                new OpcPackageMutationBuilder(package),
                ChangedPartCount: 0,
                MatchOffset: null,
                MatchedTextNodeCount: null,
                SubmittedRevisionCount: 0,
                ChangedRevisionCount: 0,
                RemovedMoveMarkerCount: 0,
                RemainingRevisionCount: 0,
                ExpectedParagraphPartUri: null,
                ExpectedParagraphElementOrdinal: null,
                ExpectedParagraphText: null,
                SourceSemantic: semantic
            );
        }
        if (graph.Revisions.Count > TransformWordPackageContract.MaximumReviewDecisions)
        {
            throw new WordToolkitOperationException(
                "PACKAGE_LIMIT",
                $"The package contains more than {TransformWordPackageContract.MaximumReviewDecisions} tracked revisions"
            );
        }

        var commands = graph.Revisions.Select(revision =>
            new WordReviewDecisionCommand(revision.Id, decision)
        ).ToArray();
        var plan = new WordReviewMutationPlanner(
            new WordReviewTransactionOptions
            {
                MaxCommands = TransformWordPackageContract.MaximumReviewDecisions,
                AllowCascadingRevisions = true,
            }
        ).Plan(package, graph, commands, cancellationToken);
        if (!plan.CanApply)
        {
            var codes = string.Join(
                ",",
                plan.Blocks.Select(block => block.Code)
                    .Distinct(StringComparer.Ordinal)
                    .Take(12)
            );
            throw Unsupported(
                "The tracked-revision set cannot be transformed losslessly"
                    + (codes.Length == 0 ? string.Empty : $" ({codes})")
            );
        }

        return new PreparedTransform(
            plan.CreateMutation(package),
            plan.ChangedPartCount,
            MatchOffset: null,
            MatchedTextNodeCount: null,
            plan.ExplicitCommandCount,
            plan.ChangedOperationCount,
            plan.RemovedMoveMarkerCount,
            RemainingRevisionCount: 0,
            ExpectedParagraphPartUri: null,
            ExpectedParagraphElementOrdinal: null,
            ExpectedParagraphText: null,
            SourceSemantic: semantic
        );
    }

    private ValidatedTransformCandidate ValidateCandidate(
        OpcPackageSnapshot package,
        PreparedTransform prepared,
        WordPackageTransformKind kind,
        CancellationToken cancellationToken
    )
    {
        using var stream = new MemoryStream();
        _serializer.Write(stream, prepared.Mutation, OpcSerializationMode.Preserve);
        stream.Position = 0;
        var candidate = _reader.Read(stream, cancellationToken);
        if (!candidate.IsStructurallyValid)
        {
            throw new WordToolkitOperationException(
                "VALIDATION_FAILED",
                "The candidate package has structural OPC errors"
            );
        }

        var semantic = new WordSemanticProjector().Project(candidate, cancellationToken);
        if (kind == WordPackageTransformKind.ReplaceFirstTextOccurrence)
        {
            var paragraph = semantic.Nodes.SingleOrDefault(node =>
                node.Kind == WordSemanticNodeKind.Paragraph
                && node.SourcePartUri == prepared.ExpectedParagraphPartUri
                && node.SourceElementOrdinal == prepared.ExpectedParagraphElementOrdinal
            );
            var actual = paragraph is null
                ? null
                : string.Concat(
                    ParagraphDescendants(paragraph)
                        .Where(IsWordTextNode)
                        .OrderBy(node => node.SourceOrder)
                        .Select(node => node.Text)
                );
            if (!string.Equals(actual, prepared.ExpectedParagraphText, StringComparison.Ordinal))
            {
                throw new WordToolkitOperationException(
                    "RESULT_MISMATCH",
                    "The candidate paragraph does not match the planned replacement"
                );
            }
            return new ValidatedTransformCandidate(candidate, semantic);
        }

        var review = new WordReviewGraphBuilder().Build(
            candidate,
            semantic,
            cancellationToken
        );
        if (
            review.Revisions.Count != prepared.RemainingRevisionCount
            || review.MoveRanges.Count != 0
            || review.Moves.Count != 0
        )
        {
            throw new WordToolkitOperationException(
                "RESULT_MISMATCH",
                "The candidate package still contains tracked revisions"
            );
        }
        return new ValidatedTransformCandidate(candidate, semantic);
    }

    private static IReadOnlyList<WordSemanticNode> ParagraphDescendants(
        WordSemanticNode paragraph
    )
    {
        var result = new List<WordSemanticNode>();
        var stack = new Stack<WordSemanticNode>();
        for (var index = paragraph.Children.Count - 1; index >= 0; index--)
        {
            stack.Push(paragraph.Children[index]);
        }
        while (stack.TryPop(out var node))
        {
            if (node.Kind == WordSemanticNodeKind.Paragraph)
            {
                continue;
            }
            result.Add(node);
            for (var index = node.Children.Count - 1; index >= 0; index--)
            {
                stack.Push(node.Children[index]);
            }
        }
        return result;
    }

    private static bool IsWordTextNode(WordSemanticNode node) =>
        node.Kind == WordSemanticNodeKind.Text
        && (
            node.SourcePath.Contains("/w:t[", StringComparison.Ordinal)
            || node.SourcePath.Contains("/w:delText[", StringComparison.Ordinal)
        );

    private static IReadOnlyList<string> ProtectionBlockCodes(
        WordPackageProtectionRiskAssessment protection
    )
    {
        if (protection.HasMalformedProtectionMetadata)
        {
            return ["protection_metadata_malformed"];
        }
        if (protection.AuthorizationRequired)
        {
            return ["protected_document_edit_requires_plan"];
        }
        return Array.Empty<string>();
    }

    private static (string InputPath, string OutputPath) ValidateAndResolve(
        TransformWordPackageRequest request
    )
    {
        if (!Enum.IsDefined(request.Kind))
        {
            throw InvalidInput("kind is not a supported transform");
        }
        if (string.IsNullOrWhiteSpace(request.InputPath))
        {
            throw InvalidInput("input_path must be a non-empty string");
        }
        if (string.IsNullOrWhiteSpace(request.OutputPath))
        {
            throw InvalidInput("output_path must be a non-empty string");
        }

        string inputPath;
        string outputPath;
        try
        {
            inputPath = Path.GetFullPath(request.InputPath);
            outputPath = Path.GetFullPath(request.OutputPath);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException
        )
        {
            throw InvalidInput("input_path or output_path is not a valid filesystem path");
        }
        if (!File.Exists(inputPath))
        {
            throw new WordToolkitOperationException(
                "NOT_FOUND",
                "The input Word package does not exist"
            );
        }
        if (!InspectWordPackageContract.IsSupportedFileName(inputPath))
        {
            throw InvalidInput("Input must be a DOCX, DOCM, DOTX, or DOTM package");
        }
        if (!InspectWordPackageContract.IsSupportedFileName(outputPath))
        {
            throw InvalidInput("Output must be a DOCX, DOCM, DOTX, or DOTM package");
        }
        if (!string.Equals(
            Path.GetExtension(inputPath),
            Path.GetExtension(outputPath),
            StringComparison.OrdinalIgnoreCase
        ))
        {
            throw InvalidInput("Input and output package extensions must match");
        }
        if (string.Equals(inputPath, outputPath, StringComparison.OrdinalIgnoreCase))
        {
            throw InvalidInput("output_path must differ from input_path");
        }
        if (File.Exists(outputPath))
        {
            throw new WordToolkitOperationException(
                "VERSION_CONFLICT",
                "The output path already exists; transforms never overwrite a file"
            );
        }

        if (request.Kind == WordPackageTransformKind.ReplaceFirstTextOccurrence)
        {
            if (string.IsNullOrEmpty(request.FindText))
            {
                throw InvalidInput("find_text must be non-empty for text replacement");
            }
            if (request.ReplaceText is null)
            {
                throw InvalidInput("replace_text is required for text replacement");
            }
            if (
                request.FindText.Length > TransformWordPackageContract.MaximumTextCharacters
                || request.ReplaceText.Length
                    > TransformWordPackageContract.MaximumTextCharacters
            )
            {
                throw InvalidInput(
                    $"find_text and replace_text cannot exceed {TransformWordPackageContract.MaximumTextCharacters} characters"
                );
            }
        }
        else if (request.FindText is not null || request.ReplaceText is not null)
        {
            throw InvalidInput(
                "find_text and replace_text are only valid for text replacement"
            );
        }

        return (inputPath, outputPath);
    }

    private static WordToolkitOperationException MapFailure(Exception exception) =>
        exception switch
        {
            OpcPackageLimitException limit => new WordToolkitOperationException(
                "PACKAGE_LIMIT",
                "The package exceeds a bounded safety limit",
                Bound(limit.Message),
                innerException: limit
            ),
            WordSemanticLimitException limit => new WordToolkitOperationException(
                "PACKAGE_LIMIT",
                "Semantic projection exceeds a bounded safety limit",
                Bound(limit.Message),
                innerException: limit
            ),
            WordReviewLimitException limit => new WordToolkitOperationException(
                "PACKAGE_LIMIT",
                "Review projection exceeds a bounded safety limit",
                Bound(limit.Message),
                innerException: limit
            ),
            WordSemanticProjectionException invalid => new WordToolkitOperationException(
                "INVALID_WORD_PACKAGE",
                "The package cannot be projected as a Word semantic document",
                Bound(invalid.Message),
                innerException: invalid
            ),
            WordReviewProjectionException invalid => new WordToolkitOperationException(
                "INVALID_WORD_PACKAGE",
                "The package review graph is invalid",
                Bound(invalid.Message),
                innerException: invalid
            ),
            WordSemanticTransactionLimitException limit =>
                new WordToolkitOperationException(
                    "PACKAGE_LIMIT",
                    "The semantic transaction exceeds a bounded safety limit",
                    Bound(limit.Message),
                    innerException: limit
                ),
            WordReviewTransactionLimitException limit =>
                new WordToolkitOperationException(
                    "PACKAGE_LIMIT",
                    "The review transaction exceeds a bounded safety limit",
                    Bound(limit.Message),
                    innerException: limit
                ),
            WordSemanticPreconditionException conflict =>
                new WordToolkitOperationException(
                    "VERSION_CONFLICT",
                    "The source package changed during planning",
                    Bound(conflict.Message),
                    innerException: conflict
                ),
            WordSemanticEditException unsafeEdit => Unsupported(
                "The requested transform cannot be applied losslessly",
                unsafeEdit
            ),
            OpcPackageConcurrencyException conflict =>
                new WordToolkitOperationException(
                    "VERSION_CONFLICT",
                    "The output path changed during the atomic write",
                    Bound(conflict.Message),
                    innerException: conflict
                ),
            OpcPackageResultMismatchException mismatch =>
                new WordToolkitOperationException(
                    "RESULT_MISMATCH",
                    "The written package differs from the validated candidate",
                    Bound(mismatch.Message),
                    innerException: mismatch
                ),
            OpcPackageValidationException invalid =>
                new WordToolkitOperationException(
                    "VALIDATION_FAILED",
                    "The candidate package failed structural validation",
                    Bound(invalid.Message),
                    innerException: invalid
                ),
            InvalidDataException invalid => new WordToolkitOperationException(
                "INVALID_PACKAGE",
                "The input is not a readable OPC ZIP package",
                Bound(invalid.Message),
                innerException: invalid
            ),
            UnauthorizedAccessException denied => new WordToolkitOperationException(
                "ACCESS_DENIED",
                "The Word package cannot be read or written with current permissions",
                innerException: denied
            ),
            IOException io => new WordToolkitOperationException(
                "IO_ERROR",
                "The Word package could not be read or written",
                Bound(io.Message),
                retryable: true,
                innerException: io
            ),
            ArgumentException invalid => InvalidInput(
                Bound(invalid.Message) ?? "Invalid transform input",
                invalid
            ),
            _ => new WordToolkitOperationException(
                "INTERNAL_ERROR",
                "The Word package transform failed",
                innerException: exception
            ),
        };

    private static WordToolkitOperationException InvalidInput(
        string message,
        Exception? exception = null
    ) => new("INVALID_INPUT", message, innerException: exception);

    private static WordToolkitOperationException Unsupported(
        string reason,
        Exception? exception = null
    ) => new(
        "UNSUPPORTED_DOCUMENT",
        "The requested transform is not safe for this package",
        Bound(reason),
        innerException: exception
    );

    private static string? Bound(string? value, int maximum = 512) =>
        value is null || value.Length <= maximum ? value : value[..maximum] + "…";

    private sealed record PreparedTransform(
        OpcPackageMutationBuilder Mutation,
        int ChangedPartCount,
        int? MatchOffset,
        int? MatchedTextNodeCount,
        int? SubmittedRevisionCount,
        int? ChangedRevisionCount,
        int? RemovedMoveMarkerCount,
        int RemainingRevisionCount,
        string? ExpectedParagraphPartUri,
        int? ExpectedParagraphElementOrdinal,
        string? ExpectedParagraphText,
        WordSemanticDocument SourceSemantic
    );

    private sealed record ValidatedTransformCandidate(
        OpcPackageSnapshot Package,
        WordSemanticDocument Semantic
    );
}
