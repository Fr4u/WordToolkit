using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;
using WordToolkit.Engine.Validation;
using WordToolkit.Engine.Xml;

namespace WordToolkit.Engine.Operations;

/// <summary>
/// Plans and atomically applies bounded text replacements inside selected comment
/// definitions. Comment anchors, authorship, thread metadata, durable identifiers,
/// reactions and every non-comment package part are invariants of this operation.
/// </summary>
public sealed class CommentBodyWordPackageOperation
{
    private readonly OpcPackageReader _reader;
    private readonly OpcPackageSerializer _serializer;
    private readonly OpcAtomicPackageWriter _writer;
    private readonly IWordPackageCandidateValidator? _candidateValidator;

    public CommentBodyWordPackageOperation(
        IWordPackageCandidateValidator? candidateValidator = null,
        OpcPackageLimits? limits = null
    )
    {
        _reader = new OpcPackageReader(limits);
        _serializer = new OpcPackageSerializer();
        _writer = new OpcAtomicPackageWriter(_reader, _serializer);
        _candidateValidator = candidateValidator;
    }

    public CommentBodyEditPlanResult Plan(
        CommentBodyEditPlanRequest request,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            if (request is null)
            {
                throw Invalid("Comment body edit plan request is required");
            }
            var context = BuildContext(
                request.LocalPath,
                request.ExpectedPackageFingerprint,
                request.Commands,
                cancellationToken
            );
            return ProjectPlan(context, request.IncludeDetails);
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
            throw MapFailure(exception, request?.LocalPath);
        }
    }

    public CommentBodyEditApplyResult Apply(
        CommentBodyEditApplyRequest request,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            if (request is null)
            {
                throw Invalid("Comment body edit apply request is required");
            }
            if (!IsPlanId(request.ExpectedPlanId))
            {
                throw Invalid("expected_plan_id is not a valid comment body edit plan ID");
            }

            var context = BuildContext(
                request.LocalPath,
                request.ExpectedPackageFingerprint,
                request.Commands,
                cancellationToken
            );
            if (!string.Equals(
                    context.PlanId,
                    request.ExpectedPlanId,
                    StringComparison.Ordinal
                ))
            {
                throw new WordToolkitOperationException(
                    "PLAN_MISMATCH",
                    "Commands do not reproduce the reviewed comment body edit plan ID"
                );
            }
            if (context.HasDigitalSignatures)
            {
                throw new WordToolkitOperationException(
                    "SIGNED_PACKAGE",
                    "Direct OOXML editing is blocked because the package contains digital signatures"
                );
            }
            if (!context.Validation.Performed)
            {
                throw new WordToolkitOperationException(
                    "VALIDATOR_REQUIRED",
                    "Applying comment body edits requires a candidate package schema validator"
                );
            }
            if (!context.Validation.NoNewErrors)
            {
                var issues = context.Validation.Issues.Take(20).ToArray();
                throw new WordToolkitOperationException(
                    "OOXML_SCHEMA_INVALID",
                    "The exact candidate package introduces Microsoft Open XML schema errors",
                    details: new WordPackageValidationFailureDetails(
                        context.Validation.ErrorCount,
                        context.Validation.BaselineErrorCount,
                        context.Validation.CandidateErrorCount,
                        context.Validation.ErrorsTruncated
                            || context.Validation.Issues.Count > issues.Length,
                        issues
                    )
                );
            }

            cancellationToken.ThrowIfCancellationRequested();
            var result = _writer.Write(
                context.Path,
                context.Plan.CreateMutation(context.Package),
                new OpcAtomicWriteOptions
                {
                    ExpectedDestinationFingerprint = context.Package.Fingerprint,
                    ExpectedResultFingerprint = context.Plan.ResultPackageFingerprint,
                    KeepBackup = request.KeepBackup,
                }
            );
            return new CommentBodyEditApplyResult(
                CommentBodyWordPackageContract.ApplyContract,
                Path.GetFileName(context.Path),
                context.PlanId,
                Applied: true,
                NoOp: false,
                CommentCount: context.Edits.Count,
                TextNodeOperationCount: context.Plan.OperationCount,
                PreviousPackageFingerprint: context.Package.Fingerprint,
                PackageFingerprint: result.Fingerprint,
                PredictedPackageFingerprint: context.Plan.ResultPackageFingerprint,
                BackupPath: result.BackupPath,
                ChangedEntryNames: result.ChangedEntryNames,
                DiagnosticCount: result.Diagnostics.Count,
                MicrosoftSchemaValid: context.Validation.CandidateValid,
                MicrosoftSchemaNoNewErrors: context.Validation.NoNewErrors,
                RawTextReturned: false,
                RawXmlReturned: false,
                MutationPerformed: true,
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
            throw MapFailure(exception, request?.LocalPath);
        }
    }

    private PlanContext BuildContext(
        string localPath,
        string expectedPackageFingerprint,
        IReadOnlyList<ReplaceCommentBodyTextCommand> commands,
        CancellationToken cancellationToken
    )
    {
        ValidateRequest(localPath, expectedPackageFingerprint, commands);
        var commandSnapshot = SnapshotCommands(commands);
        var path = ResolvePath(localPath);
        cancellationToken.ThrowIfCancellationRequested();
        var package = _reader.Read(path, cancellationToken);
        if (!package.IsStructurallyValid)
        {
            throw new WordToolkitOperationException(
                "INVALID_PACKAGE",
                "The input package has structural OPC errors"
            );
        }
        if (!string.Equals(
                package.Fingerprint,
                expectedPackageFingerprint,
                StringComparison.OrdinalIgnoreCase
            ))
        {
            throw new WordToolkitOperationException(
                "VERSION_CONFLICT",
                "Saved package changed before the comment body edit plan was built"
            );
        }

        var semantic = new WordSemanticProjector().Project(package, cancellationToken);
        if (
            !package.Parts.TryGetValue(semantic.MainPartUri, out var mainPart)
            || !WordPackageConformance.IsMainContentTypeCompatibleWithFileName(
                path,
                mainPart.ContentType
            )
        )
        {
            throw new WordToolkitOperationException(
                "INVALID_WORD_PACKAGE",
                "The filename extension does not match the Word main-part content type"
            );
        }
        var review = new WordReviewGraphBuilder().Build(
            package,
            semantic,
            cancellationToken
        );
        var sources = new CommentBodySourceCache(package, cancellationToken);
        var resolved = ResolveCommands(
            commandSnapshot,
            semantic,
            review,
            sources,
            cancellationToken
        );
        var plan = new WordSemanticTransactionPlanner(
            new WordSemanticTransactionOptions
            {
                MaxCommands = CommentBodyWordPackageContract.MaximumTextNodeOperations,
                MaxTotalReplacementCharacters =
                    CommentBodyWordPackageContract.MaximumTotalReplacementCharacters,
            }
        ).PlanTextReplacements(
            package,
            semantic,
            resolved.TextCommands,
            cancellationToken
        );
        if (plan.ChangedPartCount > CommentBodyWordPackageContract.MaximumChangedParts)
        {
            throw new WordToolkitOperationException(
                "PACKAGE_LIMIT",
                $"Comment body edits may change at most {CommentBodyWordPackageContract.MaximumChangedParts} package parts"
            );
        }
        var allowedParts = resolved.Edits.Select(edit => edit.SourcePartUri)
            .ToHashSet(StringComparer.Ordinal);
        if (plan.ChangedParts.Any(part => !allowedParts.Contains(part.PartUri)))
        {
            throw new WordToolkitOperationException(
                "UNSAFE_EDIT",
                "The comment body plan attempted to change a package part outside the selected comment definitions"
            );
        }

        var candidate = ValidateExactCandidate(
            package,
            semantic,
            review,
            plan,
            resolved.Edits,
            cancellationToken
        );
        return new PlanContext(
            path,
            package,
            plan,
            CreatePlanId(plan.PlanId, resolved.IntentFields),
            commandSnapshot.Count,
            candidate.Edits,
            WordPackagePatchRiskAnalyzer.HasDigitalSignatures(package),
            candidate.Validation
        );
    }

    private CandidateOutcome ValidateExactCandidate(
        OpcPackageSnapshot package,
        WordSemanticDocument semantic,
        WordReviewGraph review,
        WordSemanticTransactionPlan plan,
        IReadOnlyList<ResolvedCommentEdit> edits,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var baseline = new MemoryStream();
        _serializer.Write(baseline, new OpcPackageMutationBuilder(package));
        using var candidate = new MemoryStream();
        _serializer.Write(candidate, plan.CreateMutation(package));

        candidate.Position = 0;
        var candidateSnapshot = _reader.Read(candidate, cancellationToken);
        if (!candidateSnapshot.IsStructurallyValid)
        {
            throw new WordToolkitOperationException(
                "VALIDATION_FAILED",
                "The exact candidate package has structural OPC errors"
            );
        }
        if (!string.Equals(
                candidateSnapshot.Fingerprint,
                plan.ResultPackageFingerprint,
                StringComparison.Ordinal
            ))
        {
            throw new WordToolkitOperationException(
                "RESULT_MISMATCH",
                "The exact candidate package does not match the planned result fingerprint"
            );
        }

        var candidateSemantic = new WordSemanticProjector().Project(
            candidateSnapshot,
            cancellationToken
        );
        var candidateReview = new WordReviewGraphBuilder().Build(
            candidateSnapshot,
            candidateSemantic,
            cancellationToken
        );
        var verifiedEdits = VerifyReviewInvariants(
            package,
            semantic,
            review,
            candidateSnapshot,
            candidateSemantic,
            candidateReview,
            edits,
            cancellationToken
        );

        if (_candidateValidator is null)
        {
            return new CandidateOutcome(
                WordPackageCandidateValidationReport.NotPerformed(
                    "schema_validator_unavailable"
                ),
                verifiedEdits
            );
        }
        baseline.Position = 0;
        candidate.Position = 0;
        try
        {
            return new CandidateOutcome(
                BoundValidation(
                    _candidateValidator.Validate(baseline, candidate, cancellationToken)
                ),
                verifiedEdits
            );
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new WordToolkitOperationException(
                "VALIDATION_FAILED",
                "Candidate package schema validation failed",
                innerException: exception
            );
        }
    }

    private static IReadOnlyList<ResolvedCommentEdit> VerifyReviewInvariants(
        OpcPackageSnapshot beforePackage,
        WordSemanticDocument beforeSemantic,
        WordReviewGraph before,
        OpcPackageSnapshot afterPackage,
        WordSemanticDocument afterSemantic,
        WordReviewGraph after,
        IReadOnlyList<ResolvedCommentEdit> edits,
        CancellationToken cancellationToken
    )
    {
        if (before.Comments.Count != after.Comments.Count)
        {
            throw ResultMismatch("Comment definitions changed during a body-only edit");
        }
        var selected = edits.Select(edit => edit.CommentId)
            .ToHashSet(StringComparer.Ordinal);
        var updated = edits.ToDictionary(edit => edit.CommentId, StringComparer.Ordinal);
        var beforeSources = new CommentBodySourceCache(beforePackage, cancellationToken);
        var afterSources = new CommentBodySourceCache(afterPackage, cancellationToken);
        foreach (var original in before.Comments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!after.TryGetComment(original.Id, out var candidate) || candidate is null)
            {
                throw ResultMismatch("A comment identity changed during a body-only edit");
            }
            if (!CommentMetadataEquals(original, candidate))
            {
                throw ResultMismatch("Comment metadata changed during a body-only edit");
            }
            var originalBody = EditableBody(beforeSemantic, original, beforeSources);
            var candidateBody = EditableBody(afterSemantic, candidate, afterSources);
            if (selected.Contains(original.Id))
            {
                var expected = edits.Single(edit => edit.CommentId == original.Id);
                var candidateSegments = candidateBody.Segments
                    .Select(segment => segment.Text)
                    .ToArray();
                if (
                    candidateBody.CharacterCount != expected.AfterCharacters
                    || !candidateSegments.SequenceEqual(
                        expected.ExpectedAfterSegmentTexts,
                        StringComparer.Ordinal
                    )
                )
                {
                    throw ResultMismatch(
                        "A selected comment body does not match the planned result"
                    );
                }
                updated[original.Id] = expected with
                {
                    AfterBodySha256 = candidateBody.BodySha256,
                };
            }
            else if (!string.Equals(
                    originalBody.BodySha256,
                    candidateBody.BodySha256,
                    StringComparison.Ordinal
                ))
            {
                throw ResultMismatch("An unselected comment body changed");
            }
        }
        if (!SequenceMetadataEquals(before.Anchors, after.Anchors)
            || !SequenceMetadataEquals(before.People, after.People)
            || !SequenceMetadataEquals(before.Revisions, after.Revisions)
            || !SequenceMetadataEquals(before.MoveRanges, after.MoveRanges)
            || !SequenceMetadataEquals(before.Moves, after.Moves)
            || !SequenceMetadataEquals(before.Permissions, after.Permissions)
            || !Equals(before.Settings, after.Settings))
        {
            throw ResultMismatch(
                "Review anchors, authors, threads, revisions, reactions or permissions changed during a body-only edit"
            );
        }
        return edits.Select(edit => updated[edit.CommentId]).ToArray();
    }

    private static bool CommentMetadataEquals(
        WordCommentDefinition left,
        WordCommentDefinition right
    ) =>
        left.Id == right.Id
        && left.OoxmlId == right.OoxmlId
        && left.IsEffectiveByOoxmlId == right.IsEffectiveByOoxmlId
        && left.PartUri == right.PartUri
        && left.SourceElementOrdinal == right.SourceElementOrdinal
        && left.SemanticNodeId == right.SemanticNodeId
        && left.Author == right.Author
        && left.Initials == right.Initials
        && left.Date == right.Date
        && left.DateUtc == right.DateUtc
        && left.ParagraphIds.SequenceEqual(right.ParagraphIds, StringComparer.Ordinal)
        && left.LastParagraphId == right.LastParagraphId
        && left.AnchorIds.SequenceEqual(right.AnchorIds, StringComparer.Ordinal)
        && left.ParentCommentId == right.ParentCommentId
        && left.ThreadRootCommentId == right.ThreadRootCommentId
        && left.ThreadDepth == right.ThreadDepth
        && left.IsDone == right.IsDone
        && left.DurableId == right.DurableId
        && left.ExtensibleDateUtc == right.ExtensibleDateUtc
        && left.IsIntelligentPlaceholder == right.IsIntelligentPlaceholder
        && left.HasReactions == right.HasReactions
        && left.ExtensionCount == right.ExtensionCount
        && left.PersonId == right.PersonId;

    private static bool SequenceMetadataEquals<T>(
        IReadOnlyList<T> left,
        IReadOnlyList<T> right
    ) => string.Equals(
        JsonSerializer.Serialize(left),
        JsonSerializer.Serialize(right),
        StringComparison.Ordinal
    );

    private static WordToolkitOperationException ResultMismatch(string message) =>
        new("RESULT_MISMATCH", message);

    private static ResolvedCommands ResolveCommands(
        IReadOnlyList<ReplaceCommentBodyTextCommand> commands,
        WordSemanticDocument semantic,
        WordReviewGraph review,
        CommentBodySourceCache sources,
        CancellationToken cancellationToken
    )
    {
        var seenComments = new HashSet<string>(StringComparer.Ordinal);
        var textCommands = new List<WordTextReplacementCommand>();
        var edits = new List<ResolvedCommentEdit>(commands.Count);
        var intent = new List<string>(commands.Count * 8);
        long totalReplacementCharacters = 0;

        for (var commandIndex = 0; commandIndex < commands.Count; commandIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var command = commands[commandIndex]
                ?? throw Invalid($"commands[{commandIndex}] cannot be null");
            ValidateCommand(command, commandIndex);
            if (!seenComments.Add(command.CommentId))
            {
                throw Invalid(
                    $"Comment '{command.CommentId}' is targeted more than once; use one bounded replacement command per comment"
                );
            }
            if (!review.TryGetComment(command.CommentId, out var comment) || comment is null)
            {
                throw new WordToolkitOperationException(
                    "COMMENT_NOT_FOUND",
                    $"Comment '{Bound(command.CommentId, 128)}' does not exist"
                );
            }
            if (
                !comment.IsEffectiveByOoxmlId
                || comment.SemanticNodeId is null
                || string.IsNullOrWhiteSpace(comment.OoxmlId)
                || review.Comments.Count(candidate => string.Equals(
                    candidate.OoxmlId,
                    comment.OoxmlId,
                    StringComparison.Ordinal
                )) != 1
            )
            {
                throw new WordToolkitOperationException(
                    "UNSAFE_EDIT",
                    "The selected comment has an ambiguous or unbound OOXML identity"
                );
            }

            var body = EditableBody(semantic, comment, sources);
            var beforeHash = body.BodySha256;
            if (
                command.ExpectedBodySha256 is not null
                && !string.Equals(
                    beforeHash,
                    command.ExpectedBodySha256,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                throw new WordToolkitOperationException(
                    "VERSION_CONFLICT",
                    "The selected comment body does not match expected_body_sha256"
                );
            }
            var segmentMatches = body.Segments.Select((segment, segmentIndex) =>
                new SegmentMatches(
                    segmentIndex,
                    FindMatches(
                        segment.Text,
                        command.FindText,
                        command.CaseSensitive
                    )
                )
            ).Where(item => item.Offsets.Count != 0).ToArray();
            var matchCount = segmentMatches.Sum(item => item.Offsets.Count);
            if (matchCount != command.ExpectedMatchCount)
            {
                throw new WordToolkitOperationException(
                    "MATCH_COUNT_MISMATCH",
                    $"Comment body replacement expected {command.ExpectedMatchCount} matches but found {matchCount}"
                );
            }

            var nodeChanges = segmentMatches.SelectMany(item => BuildNodeChanges(
                body.Segments[item.SegmentIndex].Nodes,
                item.Offsets,
                command.FindText.Length,
                command.ReplacementText
            )).ToArray();
            if (nodeChanges.Length == 0)
            {
                throw Invalid("The comment body command does not change any text");
            }
            foreach (var change in nodeChanges)
            {
                textCommands.Add(
                    new WordTextReplacementCommand(
                        change.Node.Id,
                        change.After,
                        change.Before
                    )
                );
                checked
                {
                    totalReplacementCharacters += change.After.Length;
                }
            }
            if (
                textCommands.Count
                > CommentBodyWordPackageContract.MaximumTextNodeOperations
            )
            {
                throw new WordToolkitOperationException(
                    "TRANSACTION_LIMIT",
                    $"Comment edits resolve to more than {CommentBodyWordPackageContract.MaximumTextNodeOperations} text-node operations"
                );
            }
            if (
                totalReplacementCharacters
                > CommentBodyWordPackageContract.MaximumTotalReplacementCharacters
            )
            {
                throw new WordToolkitOperationException(
                    "TRANSACTION_LIMIT",
                    "Resolved replacement text exceeds the transaction character limit"
                );
            }

            var changesByNode = nodeChanges.ToDictionary(change => change.Node.Id);
            var expectedAfterSegments = body.Segments.Select(segment =>
                ApplyNodeChanges(segment.Nodes, changesByNode)
            ).ToArray();
            foreach (var segmentMatchesItem in segmentMatches)
            {
                var expectedAfter = ReplaceMatches(
                    body.Segments[segmentMatchesItem.SegmentIndex].Text,
                    segmentMatchesItem.Offsets,
                    command.FindText.Length,
                    command.ReplacementText
                );
                if (!string.Equals(
                        expectedAfterSegments[segmentMatchesItem.SegmentIndex],
                        expectedAfter,
                        StringComparison.Ordinal
                    ))
                {
                    throw new InvalidOperationException(
                        "Comment replacement could not be bound deterministically to semantic text nodes."
                    );
                }
            }
            var afterCharacters = checked(
                body.CharacterCount
                + nodeChanges.Sum(change => change.After.Length - change.Before.Length)
            );
            edits.Add(
                new ResolvedCommentEdit(
                    commandIndex,
                    comment.Id,
                    matchCount,
                    body.Segments.Sum(segment => segment.Nodes.Count),
                    nodeChanges.Length,
                    body.CharacterCount,
                    afterCharacters,
                    beforeHash,
                    string.Empty,
                    comment.PartUri,
                    expectedAfterSegments
                )
            );
            intent.Add(commandIndex.ToString(System.Globalization.CultureInfo.InvariantCulture));
            intent.Add(command.CommentId);
            intent.Add(command.FindText);
            intent.Add(command.ReplacementText);
            intent.Add(command.ExpectedMatchCount.ToString(
                System.Globalization.CultureInfo.InvariantCulture
            ));
            intent.Add(command.CaseSensitive ? "1" : "0");
            intent.Add(command.ExpectedBodySha256 is null ? "null" : "value");
            if (command.ExpectedBodySha256 is not null)
            {
                intent.Add(command.ExpectedBodySha256.ToLowerInvariant());
            }
        }
        return new ResolvedCommands(textCommands, edits, intent);
    }

    private static EditableCommentBody EditableBody(
        WordSemanticDocument semantic,
        WordCommentDefinition comment,
        CommentBodySourceCache sources
    )
    {
        if (
            comment.SemanticNodeId is not { } commentNodeId
            || !semantic.TryGetNode(commentNodeId, out var commentNode)
            || commentNode is null
            || commentNode.Kind != WordSemanticNodeKind.Comment
            || !string.Equals(
                commentNode.SourcePartUri,
                comment.PartUri,
                StringComparison.Ordinal
            )
            || commentNode.SourceElementOrdinal != comment.SourceElementOrdinal
        )
        {
            throw new WordToolkitOperationException(
                "UNSAFE_EDIT",
                "The selected comment cannot be bound to its semantic source element"
            );
        }
        var nodes = commentNode.DescendantsAndSelf()
            .Where(node => node.Kind == WordSemanticNodeKind.Text)
            .OrderBy(node => node.SourceOrder)
            .ToArray();
        if (nodes.Length == 0)
        {
            throw new WordToolkitOperationException(
                "UNSAFE_EDIT",
                "The selected comment has no editable text nodes"
            );
        }
        if (nodes.Any(node => HasAncestorKind(
                semantic,
                node,
                commentNode.Id,
                WordSemanticNodeKind.Revision
            )))
        {
            throw new WordToolkitOperationException(
                "UNSAFE_EDIT",
                "The selected comment contains tracked revision text; body-only edits do not rewrite revision content"
            );
        }
        var source = sources.Get(comment.PartUri);
        var sourceComment = source.GetElement(comment.SourceElementOrdinal);
        if (
            sourceComment.LocalName != "comment"
            || !IsWordNamespace(sourceComment.NamespaceUri)
        )
        {
            throw new WordToolkitOperationException(
                "UNSAFE_EDIT",
                "The selected comment source element is not a Word comment definition"
            );
        }
        var nodesByOrdinal = nodes.ToDictionary(node => node.SourceElementOrdinal);
        var segments = BuildEditableSegments(
            source,
            sourceComment.Ordinal,
            nodesByOrdinal,
            sources.CancellationToken
        );
        if (segments.Count == 0)
        {
            throw new WordToolkitOperationException(
                "UNSAFE_EDIT",
                "The selected comment has no ordinary paragraph text that can be edited safely"
            );
        }
        var bodyBytes = source.SourceBytes.Slice(
            sourceComment.ContentSpan.ByteOffset,
            sourceComment.ContentSpan.ByteLength
        );
        return new EditableCommentBody(
            segments,
            nodes.Sum(node => (node.Text ?? string.Empty).Length),
            Convert.ToHexString(SHA256.HashData(bodyBytes.Span)).ToLowerInvariant()
        );
    }

    private static IReadOnlyList<EditableTextSegment> BuildEditableSegments(
        LosslessXmlDocument source,
        int commentOrdinal,
        IReadOnlyDictionary<int, WordSemanticNode> nodesByOrdinal,
        CancellationToken cancellationToken
    )
    {
        var comment = source.GetParsedElement(commentOrdinal);
        var result = new List<EditableTextSegment>();
        foreach (var paragraph in comment.Elements().Where(element =>
            element.Name.LocalName == "p" && IsWordNamespace(element.Name.NamespaceName)
        ))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = new List<WordSemanticNode>();
            void Flush()
            {
                if (current.Count != 0)
                {
                    result.Add(new EditableTextSegment(
                        current.ToArray(),
                        string.Concat(current.Select(node => node.Text ?? string.Empty))
                    ));
                    current.Clear();
                }
            }

            foreach (var child in paragraph.Elements())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (IsWordElement(child, "pPr"))
                {
                    continue;
                }
                if (!IsWordElement(child, "r"))
                {
                    Flush();
                    continue;
                }
                foreach (var runChild in child.Elements())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (IsWordElement(runChild, "rPr"))
                    {
                        continue;
                    }
                    if (
                        IsWordElement(runChild, "t")
                        || IsWordElement(runChild, "delText")
                    )
                    {
                        var ordinal = source.GetElementOrdinal(runChild);
                        if (!nodesByOrdinal.TryGetValue(ordinal, out var textNode))
                        {
                            throw new WordToolkitOperationException(
                                "UNSAFE_EDIT",
                                "The selected comment text cannot be bound to its exact XML source"
                            );
                        }
                        current.Add(textNode);
                        continue;
                    }
                    Flush();
                }
            }
            Flush();
        }
        return result;
    }

    private static bool IsWordElement(System.Xml.Linq.XElement element, string localName) =>
        element.Name.LocalName == localName && IsWordNamespace(element.Name.NamespaceName);

    private static bool IsWordNamespace(string namespaceUri) =>
        namespaceUri is "http://schemas.openxmlformats.org/wordprocessingml/2006/main"
            or "http://purl.oclc.org/ooxml/wordprocessingml/main";

    private static bool HasAncestorKind(
        WordSemanticDocument semantic,
        WordSemanticNode node,
        SemanticNodeId boundary,
        WordSemanticNodeKind kind
    )
    {
        var current = node;
        while (current.ParentId is { } parentId)
        {
            if (!semantic.TryGetNode(parentId, out var parent) || parent is null)
            {
                throw new WordToolkitOperationException(
                    "UNSAFE_EDIT",
                    "The selected comment has an incomplete semantic ancestry"
                );
            }
            if (parent.Kind == kind)
            {
                return true;
            }
            if (parent.Id == boundary)
            {
                return false;
            }
            current = parent;
        }
        throw new WordToolkitOperationException(
            "UNSAFE_EDIT",
            "The selected text node is not contained by the selected comment"
        );
    }

    private static IReadOnlyList<int> FindMatches(
        string body,
        string find,
        bool caseSensitive
    )
    {
        var comparison = caseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;
        var result = new List<int>();
        var offset = 0;
        while (offset <= body.Length - find.Length)
        {
            var match = body.IndexOf(find, offset, comparison);
            if (match < 0)
            {
                break;
            }
            result.Add(match);
            offset = checked(match + find.Length);
        }
        return result;
    }

    private static IReadOnlyList<NodeChange> BuildNodeChanges(
        IReadOnlyList<WordSemanticNode> nodes,
        IReadOnlyList<int> matches,
        int findLength,
        string replacement
    )
    {
        var result = new List<NodeChange>();
        var nodeStart = 0;
        foreach (var node in nodes)
        {
            var before = node.Text ?? string.Empty;
            var nodeEnd = checked(nodeStart + before.Length);
            var edits = new List<LocalEdit>();
            foreach (var matchStart in matches)
            {
                var matchEnd = checked(matchStart + findLength);
                var overlapStart = Math.Max(matchStart, nodeStart);
                var overlapEnd = Math.Min(matchEnd, nodeEnd);
                if (overlapStart >= overlapEnd)
                {
                    continue;
                }
                edits.Add(
                    new LocalEdit(
                        overlapStart - nodeStart,
                        overlapEnd - nodeStart,
                        matchStart >= nodeStart && matchStart < nodeEnd
                            ? replacement
                            : string.Empty
                    )
                );
            }
            var after = before;
            foreach (
                var edit in edits.OrderByDescending(item => item.Start)
                    .ThenByDescending(item => item.End)
            )
            {
                after = after[..edit.Start] + edit.Replacement + after[edit.End..];
            }
            if (!string.Equals(before, after, StringComparison.Ordinal))
            {
                result.Add(new NodeChange(node, before, after));
            }
            nodeStart = nodeEnd;
        }
        return result;
    }

    private static string ApplyNodeChanges(
        IReadOnlyList<WordSemanticNode> nodes,
        IReadOnlyDictionary<SemanticNodeId, NodeChange> changes
    )
    {
        return string.Concat(nodes.Select(node =>
            changes.TryGetValue(node.Id, out var changed)
                ? changed.After
                : node.Text ?? string.Empty
        ));
    }

    private static string ReplaceMatches(
        string body,
        IReadOnlyList<int> matches,
        int findLength,
        string replacement
    )
    {
        var builder = new StringBuilder(
            checked(body.Length + matches.Count * (replacement.Length - findLength))
        );
        var offset = 0;
        foreach (var match in matches)
        {
            builder.Append(body, offset, match - offset);
            builder.Append(replacement);
            offset = match + findLength;
        }
        builder.Append(body, offset, body.Length - offset);
        return builder.ToString();
    }

    private static CommentBodyEditPlanResult ProjectPlan(
        PlanContext context,
        bool includeDetails
    )
    {
        var blockedReasons = new List<string>();
        if (context.HasDigitalSignatures)
        {
            blockedReasons.Add("digital_signature_present");
        }
        if (!context.Validation.Performed)
        {
            blockedReasons.Add("schema_validator_unavailable");
        }
        else if (!context.Validation.NoNewErrors)
        {
            blockedReasons.Add("microsoft_schema_validation_failed");
        }
        return new CommentBodyEditPlanResult(
            CommentBodyWordPackageContract.PlanContract,
            Path.GetFileName(context.Path),
            context.PlanId,
            context.Plan.BasePackageFingerprint,
            context.Plan.ResultPackageFingerprint,
            context.SubmittedCommandCount,
            context.Edits.Count,
            context.Edits.Sum(edit => edit.MatchedOccurrenceCount),
            context.Plan.OperationCount,
            context.Plan.ChangedOperationCount,
            context.Plan.ChangedPartCount,
            context.Plan.TotalXmlByteDelta,
            context.Plan.HasChanges,
            CanApply: blockedReasons.Count == 0,
            ApplyBlocked: blockedReasons.Count != 0,
            ApplyBlockedReasons: blockedReasons,
            CandidateValidation: ProjectValidation(context.Validation, includeDetails),
            CommentEdits: includeDetails
                ? context.Edits.Select(edit => new CommentBodyEditDetail(
                    edit.CommandIndex,
                    edit.CommentId,
                    edit.MatchedOccurrenceCount,
                    edit.TextNodeCount,
                    edit.ChangedTextNodeCount,
                    edit.BeforeCharacters,
                    edit.AfterCharacters,
                    edit.BeforeBodySha256,
                    edit.AfterBodySha256,
                    Bound(edit.SourcePartUri, 512)!
                )).ToArray()
                : null,
            ChangedParts: includeDetails
                ? context.Plan.ChangedParts.Select(part =>
                    new CommentBodyEditChangedPart(
                        Bound(part.PartUri, 512)!,
                        part.BeforeBytes,
                        part.AfterBytes,
                        (long)part.AfterBytes - part.BeforeBytes
                    )
                ).ToArray()
                : null,
            RawTextReturned: false,
            RawXmlReturned: false,
            MutationPerformed: false,
            WordOpened: false
        );
    }

    private static WordPackageCandidateValidationReport ProjectValidation(
        WordPackageCandidateValidationReport report,
        bool includeDetails
    ) => includeDetails
        ? report
        : report with
        {
            ErrorsTruncated = report.ErrorsTruncated || report.Issues.Count > 0,
            Issues = Array.Empty<WordPackageValidationIssue>(),
        };

    private static void ValidateCommand(
        ReplaceCommentBodyTextCommand command,
        int index
    )
    {
        if (!IsCommentId(command.CommentId))
        {
            throw Invalid($"commands[{index}].comment_id is not a valid semantic comment ID");
        }
        if (
            command.FindText.Length is < 1
                or > CommentBodyWordPackageContract.MaximumTextCharactersPerField
            || command.ReplacementText.Length
                > CommentBodyWordPackageContract.MaximumTextCharactersPerField
        )
        {
            throw Invalid(
                $"commands[{index}] text fields exceed the bounded comment edit limits"
            );
        }
        if (command.ExpectedMatchCount is < 1 or > 10_000)
        {
            throw Invalid(
                $"commands[{index}].expected_match_count must be between 1 and 10000"
            );
        }
        if (
            command.ExpectedBodySha256 is not null
            && !IsSha256(command.ExpectedBodySha256)
        )
        {
            throw Invalid(
                $"commands[{index}].expected_body_sha256 must be exactly 64 hexadecimal characters"
            );
        }
    }

    private static void ValidateRequest(
        string localPath,
        string expectedPackageFingerprint,
        IReadOnlyList<ReplaceCommentBodyTextCommand> commands
    )
    {
        if (string.IsNullOrWhiteSpace(localPath))
        {
            throw Invalid("local_path must be a non-empty string");
        }
        if (localPath.Length > CommentBodyWordPackageContract.MaximumLocalPathCharacters)
        {
            throw Invalid(
                $"local_path cannot exceed {CommentBodyWordPackageContract.MaximumLocalPathCharacters} characters"
            );
        }
        if (!InspectWordPackageContract.IsSupportedFileName(localPath))
        {
            throw Invalid("Comment body edits accept DOCX, DOCM, DOTX, or DOTM files");
        }
        if (!IsSha256(expectedPackageFingerprint))
        {
            throw Invalid(
                "expected_package_fingerprint must be exactly 64 hexadecimal characters"
            );
        }
        if (
            commands is null
            || commands.Count is < 1
                or > CommentBodyWordPackageContract.MaximumCommands
        )
        {
            throw Invalid(
                $"commands must contain between 1 and {CommentBodyWordPackageContract.MaximumCommands} comment body edits"
            );
        }
    }

    private static IReadOnlyList<ReplaceCommentBodyTextCommand> SnapshotCommands(
        IReadOnlyList<ReplaceCommentBodyTextCommand> commands
    )
    {
        try
        {
            var snapshot = commands.ToArray();
            if (
                snapshot.Length != commands.Count
                || snapshot.Length is < 1
                    or > CommentBodyWordPackageContract.MaximumCommands
            )
            {
                throw Invalid("commands changed while the request was being read");
            }
            return snapshot;
        }
        catch (WordToolkitOperationException)
        {
            throw;
        }
        catch (InvalidOperationException exception)
        {
            throw Invalid("commands changed while the request was being read", exception);
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
            exception is ArgumentException
                or NotSupportedException
                or PathTooLongException
        )
        {
            throw Invalid("local_path is not a valid filesystem path", exception);
        }
    }

    private static WordPackageCandidateValidationReport BoundValidation(
        WordPackageCandidateValidationReport report
    )
    {
        ArgumentNullException.ThrowIfNull(report);
        if (
            report.ErrorCount < 0
            || report.BaselineErrorCount < 0
            || report.CandidateErrorCount < 0
            || report.Issues is null
            || report.Issues.Count > 200
            || report.ErrorCount < report.Issues.Count
            || report.NoNewErrors && report.ErrorCount != 0
            || report.CandidateValid && report.CandidateErrorCount != 0
            || report.Performed && report.NotPerformedReason is not null
            || !report.Performed
                && (
                    report.CandidateValid
                    || report.NoNewErrors
                    || report.ErrorCount != 0
                    || report.BaselineErrorCount != 0
                    || report.CandidateErrorCount != 0
                    || report.ErrorsTruncated
                    || report.Issues.Count != 0
                    || string.IsNullOrWhiteSpace(report.NotPerformedReason)
                )
            || report.Performed
                && !report.ErrorsTruncated
                && report.ErrorCount != report.Issues.Count
        )
        {
            throw new InvalidOperationException(
                "Candidate validator returned an invalid or unbounded report."
            );
        }
        return report with
        {
            NotPerformedReason = Bound(report.NotPerformedReason, 128),
            Issues = report.Issues.Select(issue =>
                new WordPackageValidationIssue(
                    Bound(issue.Id, 128),
                    Bound(issue.ErrorType, 64) ?? "Unknown",
                    Bound(issue.PartUri, 512),
                    Bound(issue.Path, 512),
                    Bound(issue.Node, 128)
                )
            ).ToArray(),
        };
    }

    private static string CreatePlanId(
        string enginePlanId,
        IReadOnlyList<string> intentFields
    )
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendIntentHashField(hash, "wordtoolkit-comment-body-edit-intent-v1");
        AppendIntentHashField(hash, enginePlanId);
        foreach (var field in intentFields)
        {
            AppendIntentHashField(hash, field);
        }
        var digest = hash.GetHashAndReset();
        return "wcbplan_" + Convert.ToBase64String(digest.AsSpan(0, 15))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static void AppendIntentHashField(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }

    private static bool IsPlanId(string value) =>
        value is not null
        && value.Length is >= 12 and <= 128
        && value.StartsWith("wcbplan_", StringComparison.Ordinal)
        && value[8..].All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '_' or '-'
        );

    private static bool IsCommentId(string value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length is >= 5 and <= 128
        && value.StartsWith("wdc_", StringComparison.Ordinal)
        && value[4..].All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '_' or '-'
        );

    private static bool IsSha256(string value) =>
        value is not null && value.Length == 64 && value.All(Uri.IsHexDigit);

    private static string? Bound(string? value, int maximum)
    {
        if (value is null || value.Length <= maximum)
        {
            return value;
        }
        return value[..maximum] + "…";
    }

    private static WordToolkitOperationException MapFailure(
        Exception exception,
        string? localPath
    ) => exception switch
    {
        WordSemanticTransactionLimitException limit => new WordToolkitOperationException(
            "TRANSACTION_LIMIT",
            SafeReason(limit.Message, localPath) ?? "Comment body transaction limit exceeded",
            innerException: limit
        ),
        WordSemanticPreconditionException conflict => new WordToolkitOperationException(
            "VERSION_CONFLICT",
            SafeReason(conflict.Message, localPath) ?? "Semantic precondition failed",
            innerException: conflict
        ),
        WordSemanticEditException edit => new WordToolkitOperationException(
            "UNSAFE_EDIT",
            SafeReason(edit.Message, localPath) ?? "Comment body edit is unsafe",
            innerException: edit
        ),
        WordSemanticLimitException limit => new WordToolkitOperationException(
            "PACKAGE_LIMIT",
            "Semantic projection exceeds a bounded safety limit",
            SafeReason(limit.Message, localPath),
            innerException: limit
        ),
        WordReviewLimitException limit => new WordToolkitOperationException(
            "PACKAGE_LIMIT",
            "Review projection exceeds a bounded safety limit",
            SafeReason(limit.Message, localPath),
            innerException: limit
        ),
        WordSemanticProjectionException projection => new WordToolkitOperationException(
            "INVALID_WORD_PACKAGE",
            "The package cannot be projected as a Word semantic document",
            SafeReason(projection.Message, localPath),
            innerException: projection
        ),
        WordReviewProjectionException projection => new WordToolkitOperationException(
            "INVALID_WORD_PACKAGE",
            "The package review graph is invalid",
            SafeReason(projection.Message, localPath),
            innerException: projection
        ),
        OpcPackageConcurrencyException conflict => new WordToolkitOperationException(
            "VERSION_CONFLICT",
            "Destination package changed during the atomic write",
            SafeReason(conflict.Message, localPath),
            retryable: true,
            innerException: conflict
        ),
        OpcPackageResultMismatchException mismatch => new WordToolkitOperationException(
            "RESULT_MISMATCH",
            "Candidate package does not match the reviewed comment body plan",
            SafeReason(mismatch.Message, localPath),
            innerException: mismatch
        ),
        OpcPackageValidationException validation => new WordToolkitOperationException(
            "VALIDATION_FAILED",
            "Candidate package failed structural validation",
            SafeReason(validation.Message, localPath),
            innerException: validation
        ),
        OpcPackageRecoveryException recovery => new WordToolkitOperationException(
            "RECOVERY_REQUIRED",
            "Atomic commit detected a concurrent change and automatic recovery did not finish",
            retryable: false,
            innerException: recovery,
            details: StyleWordPackageOperation.BuildRecoveryDetails(recovery)
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
            innerException: invalid
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
        UnauthorizedAccessException denied => new WordToolkitOperationException(
            "ACCESS_DENIED",
            "The Word package cannot be read or written with current permissions",
            innerException: denied
        ),
        ArgumentException invalid => Invalid("Invalid comment body edit", invalid),
        IOException io => new WordToolkitOperationException(
            "IO_ERROR",
            "The Word package could not be read or written",
            retryable: true,
            innerException: io
        ),
        _ => new WordToolkitOperationException(
            "INTERNAL_ERROR",
            "The comment body operation failed",
            innerException: exception
        ),
    };

    private static string? SafeReason(string? message, string? localPath)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }
        var safe = message;
        if (localPath is not null)
        {
            try
            {
                safe = safe.Replace(
                    Path.GetFullPath(localPath),
                    "<redacted>",
                    StringComparison.OrdinalIgnoreCase
                );
            }
            catch (Exception exception) when (
                exception is ArgumentException
                    or NotSupportedException
                    or PathTooLongException
            )
            {
            }
            safe = safe.Replace(localPath, "<redacted>", StringComparison.OrdinalIgnoreCase);
        }
        return Bound(safe, 512);
    }

    private static WordToolkitOperationException Invalid(
        string message,
        Exception? innerException = null
    ) => new("INVALID_INPUT", message, innerException: innerException);

    private sealed record EditableCommentBody(
        IReadOnlyList<EditableTextSegment> Segments,
        int CharacterCount,
        string BodySha256
    );

    private sealed record EditableTextSegment(
        IReadOnlyList<WordSemanticNode> Nodes,
        string Text
    );

    private sealed record SegmentMatches(
        int SegmentIndex,
        IReadOnlyList<int> Offsets
    );

    private sealed record LocalEdit(int Start, int End, string Replacement);

    private sealed record NodeChange(
        WordSemanticNode Node,
        string Before,
        string After
    );

    private sealed record ResolvedCommentEdit(
        int CommandIndex,
        string CommentId,
        int MatchedOccurrenceCount,
        int TextNodeCount,
        int ChangedTextNodeCount,
        int BeforeCharacters,
        int AfterCharacters,
        string BeforeBodySha256,
        string AfterBodySha256,
        string SourcePartUri,
        IReadOnlyList<string> ExpectedAfterSegmentTexts
    );

    private sealed record ResolvedCommands(
        IReadOnlyList<WordTextReplacementCommand> TextCommands,
        IReadOnlyList<ResolvedCommentEdit> Edits,
        IReadOnlyList<string> IntentFields
    );

    private sealed record PlanContext(
        string Path,
        OpcPackageSnapshot Package,
        WordSemanticTransactionPlan Plan,
        string PlanId,
        int SubmittedCommandCount,
        IReadOnlyList<ResolvedCommentEdit> Edits,
        bool HasDigitalSignatures,
        WordPackageCandidateValidationReport Validation
    );

    private sealed record CandidateOutcome(
        WordPackageCandidateValidationReport Validation,
        IReadOnlyList<ResolvedCommentEdit> Edits
    );

    private sealed class CommentBodySourceCache
    {
        private readonly OpcPackageSnapshot _package;
        private readonly CancellationToken _cancellationToken;
        private readonly Dictionary<string, LosslessXmlDocument> _sources =
            new(StringComparer.Ordinal);

        public CommentBodySourceCache(
            OpcPackageSnapshot package,
            CancellationToken cancellationToken
        )
        {
            _package = package;
            _cancellationToken = cancellationToken;
        }

        public LosslessXmlDocument Get(string partUri)
        {
            if (_sources.TryGetValue(partUri, out var source))
            {
                return source;
            }
            if (!_package.Parts.TryGetValue(partUri, out var part))
            {
                throw new WordToolkitOperationException(
                    "UNSAFE_EDIT",
                    "The selected comment source part is missing"
                );
            }
            try
            {
                source = LosslessXmlDocument.Parse(
                    part.Entry.Content,
                    cancellationToken: _cancellationToken
                );
            }
            catch (Exception exception) when (
                exception is LosslessXmlParseException or LosslessXmlLimitException
            )
            {
                throw new WordToolkitOperationException(
                    "UNSAFE_EDIT",
                    "The selected comment source part cannot be parsed safely",
                    innerException: exception
                );
            }
            _sources.Add(partUri, source);
            return source;
        }

        public CancellationToken CancellationToken => _cancellationToken;
    }
}
