using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;
using WordToolkit.Engine.Validation;

namespace WordToolkit.Engine.Operations;

/// <summary>
/// Inspects, plans and atomically applies text-only rewrites to the ordinary text
/// slots around direct OfficeMath anchors in selected Word paragraphs. Equation XML,
/// paragraph/run structure, unselected paragraphs and unrelated package entries are
/// invariants. Rich or ambiguous inline structures fail closed.
/// </summary>
public sealed class EquationParagraphRewriteWordPackageOperation
{
    private readonly OpcPackageReader _reader;
    private readonly OpcPackageSerializer _serializer;
    private readonly OpcAtomicPackageWriter _writer;
    private readonly IWordPackageCandidateValidator? _candidateValidator;

    public EquationParagraphRewriteWordPackageOperation(
        IWordPackageCandidateValidator? candidateValidator = null,
        OpcPackageLimits? limits = null
    )
    {
        _reader = new OpcPackageReader(limits);
        _serializer = new OpcPackageSerializer();
        _writer = new OpcAtomicPackageWriter(_reader, _serializer);
        _candidateValidator = candidateValidator;
    }

    public EquationParagraphRewriteInspectResult Inspect(
        EquationParagraphRewriteInspectRequest request,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            if (request is null)
            {
                throw Invalid("Equation paragraph inspection request is required");
            }
            ValidateInspectRequest(request);
            var context = LoadCatalog(
                request.LocalPath,
                request.ExpectedPackageFingerprint,
                cancellationToken
            );
            var candidates = context.Catalog.Candidates.AsEnumerable();
            if (request.ParagraphNodeId is not null)
            {
                candidates = candidates.Where(candidate => string.Equals(
                    candidate.ParagraphNodeId.Value,
                    request.ParagraphNodeId,
                    StringComparison.Ordinal
                ));
            }
            var materialized = candidates.ToArray();
            if (request.ParagraphNodeId is not null && materialized.Length == 0)
            {
                throw new WordToolkitOperationException(
                    "PARAGRAPH_NOT_FOUND",
                    "The selected paragraph is not an equation paragraph in the current package"
                );
            }
            if (request.IncludeText)
            {
                var characters = materialized.Sum(candidate =>
                    (long)candidate.TextCharacterCount
                );
                if (characters
                    > EquationParagraphRewriteWordPackageContract.MaximumInspectTextCharacters)
                {
                    throw new WordToolkitOperationException(
                        "PACKAGE_LIMIT",
                        $"Requested paragraph text exceeds {EquationParagraphRewriteWordPackageContract.MaximumInspectTextCharacters} characters"
                    );
                }
            }
            var page = materialized.Skip(request.Offset)
                .Take(request.MaxItems)
                .ToArray();
            var exposeSlots = request.ParagraphNodeId is not null;
            var projected = page.Select(candidate => ProjectInspectionCandidate(
                candidate,
                exposeSlots,
                request.IncludeText
            )).ToArray();
            var returnedTextCharacters = request.IncludeText
                ? page.Sum(candidate => candidate.TextCharacterCount)
                : 0;
            var nextOffset = request.Offset + page.Length < materialized.Length
                ? request.Offset + page.Length
                : (int?)null;
            return new EquationParagraphRewriteInspectResult(
                EquationParagraphRewriteWordPackageContract.InspectContract,
                Path.GetFileName(context.Path),
                context.Package.Fingerprint,
                materialized.Length,
                materialized.Count(candidate => candidate.CanRewrite),
                request.Offset,
                page.Length,
                nextOffset,
                projected,
                TextIncluded: request.IncludeText,
                ReturnedTextCharacters: returnedTextCharacters,
                RawXmlReturned: false,
                MutationPerformed: false,
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

    public EquationParagraphRewritePlanResult Plan(
        EquationParagraphRewritePlanRequest request,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            if (request is null)
            {
                throw Invalid("Equation paragraph rewrite plan request is required");
            }
            var context = BuildPlanContext(
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

    public EquationParagraphRewriteApplyResult Apply(
        EquationParagraphRewriteApplyRequest request,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            if (request is null)
            {
                throw Invalid("Equation paragraph rewrite apply request is required");
            }
            if (!IsPlanId(request.ExpectedPlanId))
            {
                throw Invalid("expected_plan_id is not a valid equation paragraph plan ID");
            }
            var context = BuildPlanContext(
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
                    "Commands do not reproduce the reviewed equation paragraph plan ID"
                );
            }
            EnsureApplicable(context);

            if (!context.Plan.HasChanges)
            {
                return new EquationParagraphRewriteApplyResult(
                    EquationParagraphRewriteWordPackageContract.ApplyContract,
                    Path.GetFileName(context.Path),
                    context.PlanId,
                    Applied: false,
                    NoOp: true,
                    ParagraphCount: context.Rewrites.Count,
                    EquationAnchorCount: context.Rewrites.Sum(item =>
                        item.Before.EquationAnchorCount
                    ),
                    TextNodeOperationCount: context.Plan.OperationCount,
                    PreviousPackageFingerprint: context.Package.Fingerprint,
                    PackageFingerprint: context.Package.Fingerprint,
                    PredictedPackageFingerprint: context.Plan.ResultPackageFingerprint,
                    BackupPath: null,
                    ChangedEntryNames: Array.Empty<string>(),
                    DiagnosticCount: 0,
                    MicrosoftSchemaValid: context.Validation.CandidateValid,
                    MicrosoftSchemaNoNewErrors: context.Validation.NoNewErrors,
                    ExactEquationBytesPreserved: context.ExactEquationBytesPreserved,
                    ParagraphStructurePreserved: context.ParagraphStructurePreserved,
                    ExactInverseVerified: context.ExactInverseVerified,
                    RawTextReturned: false,
                    RawXmlReturned: false,
                    MutationPerformed: false,
                    WordOpened: false
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
            return new EquationParagraphRewriteApplyResult(
                EquationParagraphRewriteWordPackageContract.ApplyContract,
                Path.GetFileName(context.Path),
                context.PlanId,
                Applied: true,
                NoOp: false,
                ParagraphCount: context.Rewrites.Count,
                EquationAnchorCount: context.Rewrites.Sum(item =>
                    item.Before.EquationAnchorCount
                ),
                TextNodeOperationCount: context.Plan.OperationCount,
                PreviousPackageFingerprint: context.Package.Fingerprint,
                PackageFingerprint: result.Fingerprint,
                PredictedPackageFingerprint: context.Plan.ResultPackageFingerprint,
                BackupPath: result.BackupPath,
                ChangedEntryNames: result.ChangedEntryNames,
                DiagnosticCount: result.Diagnostics.Count,
                MicrosoftSchemaValid: context.Validation.CandidateValid,
                MicrosoftSchemaNoNewErrors: context.Validation.NoNewErrors,
                ExactEquationBytesPreserved: context.ExactEquationBytesPreserved,
                ParagraphStructurePreserved: context.ParagraphStructurePreserved,
                ExactInverseVerified: context.ExactInverseVerified,
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

    private LoadedCatalog LoadCatalog(
        string localPath,
        string expectedPackageFingerprint,
        CancellationToken cancellationToken
    )
    {
        ValidatePathAndFingerprint(localPath, expectedPackageFingerprint);
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
                "Saved package changed before equation paragraph evidence was built"
            );
        }
        var semantic = new WordSemanticProjector().Project(package, cancellationToken);
        if (!package.Parts.TryGetValue(semantic.MainPartUri, out var mainPart)
            || !WordPackageConformance.IsMainContentTypeCompatibleWithFileName(
                path,
                mainPart.ContentType
            ))
        {
            throw new WordToolkitOperationException(
                "INVALID_WORD_PACKAGE",
                "The filename extension does not match the Word main-part content type"
            );
        }
        var catalog = new WordEquationParagraphRewriteCatalogBuilder().Build(
            package,
            semantic,
            cancellationToken
        );
        return new LoadedCatalog(path, package, semantic, catalog);
    }

    private PlanContext BuildPlanContext(
        string localPath,
        string expectedPackageFingerprint,
        IReadOnlyList<RewriteEquationParagraphTextCommand> commands,
        CancellationToken cancellationToken
    )
    {
        ValidatePlanRequest(localPath, expectedPackageFingerprint, commands);
        var commandSnapshot = SnapshotCommands(commands);
        var loaded = LoadCatalog(localPath, expectedPackageFingerprint, cancellationToken);
        var resolved = ResolveCommands(
            loaded.Catalog,
            loaded.Semantic,
            commandSnapshot,
            cancellationToken
        );
        var plan = new WordSemanticTransactionPlanner(
            new WordSemanticTransactionOptions
            {
                MaxCommands = EquationParagraphRewriteWordPackageContract
                    .MaximumTextNodeOperations,
                MaxTotalReplacementCharacters = EquationParagraphRewriteWordPackageContract
                    .MaximumTotalReplacementCharacters,
            }
        ).PlanTextReplacements(
            loaded.Package,
            loaded.Semantic,
            resolved.TextCommands,
            cancellationToken
        );
        if (plan.ChangedPartCount
            > EquationParagraphRewriteWordPackageContract.MaximumChangedParts)
        {
            throw new WordToolkitOperationException(
                "PACKAGE_LIMIT",
                $"Equation paragraph rewrites may change at most {EquationParagraphRewriteWordPackageContract.MaximumChangedParts} package parts"
            );
        }
        var allowedParts = resolved.Rewrites.Select(rewrite => rewrite.Before.SourcePartUri)
            .ToHashSet(StringComparer.Ordinal);
        if (plan.ChangedParts.Any(part => !allowedParts.Contains(part.PartUri)))
        {
            throw new WordToolkitOperationException(
                "UNSAFE_EDIT",
                "The paragraph rewrite plan attempted to change a package part outside the selected paragraphs"
            );
        }
        var outcome = ValidateExactCandidate(
            loaded.Package,
            loaded.Catalog,
            plan,
            resolved.Rewrites,
            cancellationToken
        );
        return new PlanContext(
            loaded.Path,
            loaded.Package,
            plan,
            CreatePlanId(plan.PlanId, resolved.IntentFields),
            commandSnapshot.Count,
            outcome.Rewrites,
            WordPackagePatchRiskAnalyzer.HasDigitalSignatures(loaded.Package),
            outcome.Validation,
            outcome.ExactEquationBytesPreserved,
            outcome.ParagraphStructurePreserved,
            outcome.ExactInverseVerified
        );
    }

    private CandidateOutcome ValidateExactCandidate(
        OpcPackageSnapshot package,
        WordEquationParagraphRewriteCatalog beforeCatalog,
        WordSemanticTransactionPlan plan,
        IReadOnlyList<ResolvedRewrite> rewrites,
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
        var afterCatalog = new WordEquationParagraphRewriteCatalogBuilder().Build(
            candidateSnapshot,
            candidateSemantic,
            cancellationToken
        );
        var verified = VerifyCandidateInvariants(
            beforeCatalog,
            afterCatalog,
            rewrites,
            cancellationToken
        );
        var inverseVerified = VerifyExactInverse(
            package,
            candidateSnapshot,
            plan,
            cancellationToken
        );
        if (!inverseVerified)
        {
            throw new WordToolkitOperationException(
                "RESULT_MISMATCH",
                "The equation paragraph transaction did not reconstruct the exact source package"
            );
        }

        WordPackageCandidateValidationReport validation;
        if (_candidateValidator is null)
        {
            validation = WordPackageCandidateValidationReport.NotPerformed(
                "schema_validator_unavailable"
            );
        }
        else
        {
            baseline.Position = 0;
            candidate.Position = 0;
            try
            {
                validation = BoundValidation(
                    _candidateValidator.Validate(baseline, candidate, cancellationToken)
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
        return new CandidateOutcome(
            validation,
            verified,
            ExactEquationBytesPreserved: true,
            ParagraphStructurePreserved: true,
            ExactInverseVerified: true
        );
    }

    private bool VerifyExactInverse(
        OpcPackageSnapshot baseline,
        OpcPackageSnapshot candidate,
        WordSemanticTransactionPlan plan,
        CancellationToken cancellationToken
    )
    {
        using var inverse = new MemoryStream();
        _serializer.Write(inverse, plan.CreateInverseMutation(candidate));
        inverse.Position = 0;
        var restored = _reader.Read(inverse, cancellationToken);
        if (!string.Equals(
                baseline.Fingerprint,
                restored.Fingerprint,
                StringComparison.Ordinal
            ) || baseline.Entries.Count != restored.Entries.Count)
        {
            return false;
        }
        var restoredByName = restored.Entries.ToDictionary(
            entry => entry.Name,
            StringComparer.Ordinal
        );
        return baseline.Entries.All(entry =>
        {
            return restoredByName.TryGetValue(entry.Name, out var match)
                && entry.Content.Span.SequenceEqual(match.Content.Span);
        });
    }

    private static IReadOnlyList<ResolvedRewrite> VerifyCandidateInvariants(
        WordEquationParagraphRewriteCatalog beforeCatalog,
        WordEquationParagraphRewriteCatalog afterCatalog,
        IReadOnlyList<ResolvedRewrite> rewrites,
        CancellationToken cancellationToken
    )
    {
        if (beforeCatalog.Candidates.Count != afterCatalog.Candidates.Count)
        {
            throw ResultMismatch(
                "The set of equation-containing paragraphs changed during a text-only rewrite"
            );
        }
        var selected = rewrites.ToDictionary(
            rewrite => (rewrite.Before.SourcePartUri, rewrite.Before.SourceElementOrdinal)
        );
        var afterBySource = afterCatalog.Candidates.ToDictionary(
            candidate => (candidate.SourcePartUri, candidate.SourceElementOrdinal)
        );
        var verified = new Dictionary<int, ResolvedRewrite>();
        foreach (var before in beforeCatalog.Candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = (before.SourcePartUri, before.SourceElementOrdinal);
            if (!afterBySource.TryGetValue(key, out var after))
            {
                throw ResultMismatch(
                    "An equation paragraph source identity changed during a text-only rewrite"
                );
            }
            if (selected.TryGetValue(key, out var rewrite))
            {
                VerifySelectedParagraph(before, after, rewrite);
                verified.Add(rewrite.CommandIndex, rewrite with { After = after });
            }
            else if (!string.Equals(
                    before.Fingerprint,
                    after.Fingerprint,
                    StringComparison.Ordinal
                ))
            {
                throw ResultMismatch("An unselected equation paragraph changed");
            }
        }
        return rewrites.OrderBy(rewrite => rewrite.CommandIndex)
            .Select(rewrite => verified[rewrite.CommandIndex])
            .ToArray();
    }

    private static void VerifySelectedParagraph(
        WordEquationParagraphRewriteCandidate before,
        WordEquationParagraphRewriteCandidate after,
        ResolvedRewrite rewrite
    )
    {
        if (!string.Equals(
                before.ParagraphStructuralFingerprint,
                after.ParagraphStructuralFingerprint,
                StringComparison.Ordinal
            )
            || before.TextSlotCount != after.TextSlotCount
            || before.EquationAnchorCount != after.EquationAnchorCount
            || before.BlockedReasons.Count != after.BlockedReasons.Count
            || !before.BlockedReasons.SequenceEqual(after.BlockedReasons, StringComparer.Ordinal))
        {
            throw ResultMismatch(
                "Paragraph or editable-slot structure changed during a text-only rewrite"
            );
        }
        for (var index = 0; index < before.EquationAnchors.Count; index++)
        {
            if (before.EquationAnchors[index] != after.EquationAnchors[index])
            {
                throw ResultMismatch("An OfficeMath anchor changed during paragraph rewrite");
            }
        }
        for (var index = 0; index < before.TextSlots.Count; index++)
        {
            var expected = rewrite.ReplacementTextSlots[index];
            var candidate = after.TextSlots[index];
            if (!string.Equals(candidate.Text, expected, StringComparison.Ordinal)
                || !before.TextSlots[index].TextElementOrdinals.SequenceEqual(
                    candidate.TextElementOrdinals
                ))
            {
                throw ResultMismatch(
                    "A selected paragraph text slot does not match the planned result"
                );
            }
        }
    }

    private static ResolvedCommands ResolveCommands(
        WordEquationParagraphRewriteCatalog catalog,
        WordSemanticDocument semantic,
        IReadOnlyList<RewriteEquationParagraphTextCommand> commands,
        CancellationToken cancellationToken
    )
    {
        var textById = semantic.Nodes
            .Where(node => node.Kind == WordSemanticNodeKind.Text)
            .ToDictionary(node => node.Id, node => node.Text ?? string.Empty);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var textCommands = new List<WordTextReplacementCommand>();
        var rewrites = new List<ResolvedRewrite>(commands.Count);
        var intent = new List<string>();
        long replacementCharacters = 0;
        for (var commandIndex = 0; commandIndex < commands.Count; commandIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var command = commands[commandIndex]
                ?? throw Invalid($"commands[{commandIndex}] cannot be null");
            ValidateCommand(command, commandIndex);
            if (!seen.Add(command.CandidateId))
            {
                throw Invalid(
                    $"Candidate '{Bound(command.CandidateId, 128)}' is targeted more than once"
                );
            }
            if (!catalog.TryGetCandidate(command.CandidateId, out var candidate)
                || candidate is null)
            {
                throw new WordToolkitOperationException(
                    "CANDIDATE_NOT_FOUND",
                    "The selected equation paragraph candidate does not exist in the current package"
                );
            }
            if (!string.Equals(
                    candidate.Fingerprint,
                    command.ExpectedCandidateFingerprint,
                    StringComparison.OrdinalIgnoreCase
                ))
            {
                throw new WordToolkitOperationException(
                    "VERSION_CONFLICT",
                    "The selected equation paragraph no longer matches its reviewed candidate fingerprint"
                );
            }
            if (!candidate.CanRewrite)
            {
                throw new WordToolkitOperationException(
                    "UNSAFE_EDIT",
                    "The selected equation paragraph contains an unsupported or ambiguous inline structure"
                );
            }
            if (command.ReplacementTextSlots.Count != candidate.TextSlotCount)
            {
                throw Invalid(
                    $"commands[{commandIndex}].replacement_text_slots must contain exactly {candidate.TextSlotCount} strings"
                );
            }
            var changedSlots = 0;
            var nodeOperationsBefore = textCommands.Count;
            for (var slotIndex = 0; slotIndex < candidate.TextSlots.Count; slotIndex++)
            {
                var slot = candidate.TextSlots[slotIndex];
                var replacement = command.ReplacementTextSlots[slotIndex];
                checked
                {
                    replacementCharacters += replacement.Length;
                }
                if (replacementCharacters
                    > EquationParagraphRewriteWordPackageContract
                        .MaximumTotalReplacementCharacters)
                {
                    throw new WordToolkitOperationException(
                        "TRANSACTION_LIMIT",
                        "Equation paragraph replacement text exceeds the configured total limit"
                    );
                }
                if (!slot.CanRewrite)
                {
                    if (replacement.Length != 0)
                    {
                        throw new WordToolkitOperationException(
                            "UNSAFE_EDIT",
                            "A text slot with no existing Word text leaf cannot receive new text"
                        );
                    }
                    continue;
                }
                if (!string.Equals(slot.Text, replacement, StringComparison.Ordinal))
                {
                    changedSlots++;
                }
                for (var nodeIndex = 0; nodeIndex < slot.TextNodeIds.Count; nodeIndex++)
                {
                    var beforeNodeText = ResolveNodeText(textById, slot, nodeIndex);
                    textCommands.Add(new WordTextReplacementCommand(
                        slot.TextNodeIds[nodeIndex],
                        nodeIndex == 0 ? replacement : string.Empty,
                        beforeNodeText
                    ));
                }
            }
            if (textCommands.Count
                > EquationParagraphRewriteWordPackageContract.MaximumTextNodeOperations)
            {
                throw new WordToolkitOperationException(
                    "TRANSACTION_LIMIT",
                    $"Equation paragraph rewrites resolve to more than {EquationParagraphRewriteWordPackageContract.MaximumTextNodeOperations} text-node operations"
                );
            }
            var replacements = command.ReplacementTextSlots.ToArray();
            var rewrite = new ResolvedRewrite(
                commandIndex,
                candidate,
                After: null,
                replacements,
                changedSlots,
                textCommands.Count - nodeOperationsBefore,
                candidate.TextCharacterCount,
                replacements.Sum(value => value.Length),
                HashSlots(candidate.TextSlots.Select(slot => slot.Text)),
                HashSlots(replacements)
            );
            rewrites.Add(rewrite);
            intent.Add(command.CandidateId);
            intent.Add(command.ExpectedCandidateFingerprint.ToLowerInvariant());
            intent.Add(command.ReplacementTextSlots.Count.ToString(
                System.Globalization.CultureInfo.InvariantCulture
            ));
            foreach (var replacement in replacements)
            {
                intent.Add(replacement);
            }
        }
        return new ResolvedCommands(textCommands, rewrites, intent);
    }

    private static string ResolveNodeText(
        IReadOnlyDictionary<SemanticNodeId, string> textById,
        WordEquationParagraphTextSlot slot,
        int nodeIndex
    )
    {
        var nodeId = slot.TextNodeIds[nodeIndex];
        return textById.TryGetValue(nodeId, out var value)
            ? value
            : throw new WordSemanticPreconditionException(
                $"Text node '{nodeId}' no longer exists."
            );
    }

    private static EquationParagraphRewritePlanResult ProjectPlan(
        PlanContext context,
        bool includeDetails
    )
    {
        var blocked = ApplyBlockedReasons(context);
        return new EquationParagraphRewritePlanResult(
            EquationParagraphRewriteWordPackageContract.PlanContract,
            Path.GetFileName(context.Path),
            context.PlanId,
            context.Plan.BasePackageFingerprint,
            context.Plan.ResultPackageFingerprint,
            context.SubmittedCommandCount,
            context.Rewrites.Count,
            context.Rewrites.Sum(item => item.Before.EquationAnchorCount),
            context.Rewrites.Sum(item => item.Before.TextSlotCount),
            context.Rewrites.Sum(item => item.ChangedTextSlotCount),
            context.Plan.OperationCount,
            context.Plan.ChangedOperationCount,
            context.Plan.ChangedPartCount,
            context.Plan.TotalXmlByteDelta,
            context.Plan.HasChanges,
            context.ExactEquationBytesPreserved,
            context.ParagraphStructurePreserved,
            context.ExactInverseVerified,
            CanApply: blocked.Count == 0,
            ApplyBlocked: blocked.Count != 0,
            ApplyBlockedReasons: blocked,
            CandidateValidation: ProjectValidation(context.Validation, includeDetails),
            ParagraphRewrites: includeDetails
                ? context.Rewrites.Select(item => new EquationParagraphRewriteDetail(
                    item.CommandIndex,
                    item.Before.Id,
                    item.Before.ParagraphNodeId.Value,
                    StoryKind(item.Before.StoryKind),
                    item.Before.EquationAnchorCount,
                    item.Before.TextSlotCount,
                    item.ChangedTextSlotCount,
                    item.TextNodeOperationCount,
                    item.BeforeCharacters,
                    item.AfterCharacters,
                    item.BeforeTextSlotsSha256,
                    item.AfterTextSlotsSha256
                )).ToArray()
                : null,
            ChangedParts: includeDetails
                ? context.Plan.ChangedParts.Select(part =>
                    new EquationParagraphRewriteChangedPart(
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

    private static IReadOnlyList<string> ApplyBlockedReasons(PlanContext context)
    {
        var blocked = new List<string>();
        if (context.HasDigitalSignatures)
        {
            blocked.Add("digital_signature_present");
        }
        if (!context.Validation.Performed)
        {
            blocked.Add("schema_validator_unavailable");
        }
        else if (!context.Validation.NoNewErrors)
        {
            blocked.Add("microsoft_schema_validation_failed");
        }
        if (!context.ExactEquationBytesPreserved)
        {
            blocked.Add("equation_bytes_changed");
        }
        if (!context.ParagraphStructurePreserved)
        {
            blocked.Add("paragraph_structure_changed");
        }
        if (!context.ExactInverseVerified)
        {
            blocked.Add("exact_inverse_unverified");
        }
        return blocked;
    }

    private static void EnsureApplicable(PlanContext context)
    {
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
                "Applying equation paragraph rewrites requires a candidate package schema validator"
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
        if (!context.ExactEquationBytesPreserved
            || !context.ParagraphStructurePreserved
            || !context.ExactInverseVerified)
        {
            throw new WordToolkitOperationException(
                "RESULT_MISMATCH",
                "Equation paragraph preservation proof is incomplete"
            );
        }
    }

    private static EquationParagraphRewriteCandidateInspection ProjectInspectionCandidate(
        WordEquationParagraphRewriteCandidate candidate,
        bool includeSlots,
        bool includeText
    ) => new(
        candidate.Id,
        candidate.Fingerprint,
        candidate.ParagraphNodeId.Value,
        StoryKind(candidate.StoryKind),
        candidate.EquationAnchorCount,
        candidate.EquationAnchors.Count(anchor => anchor.Kind == "inline_math"),
        candidate.EquationAnchors.Count(anchor => anchor.Kind == "display_math"),
        candidate.TextSlotCount,
        candidate.TextSlots.Count(slot => slot.CanRewrite),
        candidate.TextNodeCount,
        candidate.TextCharacterCount,
        candidate.CanRewrite,
        candidate.BlockedReasons,
        includeSlots
            ? candidate.TextSlots.Select(slot => new EquationParagraphRewriteSlotInspection(
                slot.Index,
                slot.CharacterCount,
                slot.TextNodeIds.Count,
                slot.TextSha256,
                slot.CanRewrite,
                includeText ? slot.Text : null
            )).ToArray()
            : null
    );

    private static string StoryKind(WordStoryKind value) => value switch
    {
        WordStoryKind.Main => "main",
        WordStoryKind.Header => "header",
        WordStoryKind.Footer => "footer",
        WordStoryKind.Footnote => "footnote",
        WordStoryKind.Endnote => "endnote",
        WordStoryKind.Comment => "comment",
        WordStoryKind.GlossaryEntry => "glossary_entry",
        WordStoryKind.TextBox => "text_box",
        _ => "other",
    };

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

    private static WordPackageCandidateValidationReport BoundValidation(
        WordPackageCandidateValidationReport report
    )
    {
        ArgumentNullException.ThrowIfNull(report);
        if (report.ErrorCount < 0
            || report.BaselineErrorCount < 0
            || report.CandidateErrorCount < 0
            || report.Issues is null
            || report.Issues.Count > 200
            || report.ErrorCount < report.Issues.Count
            || report.NoNewErrors && report.ErrorCount != 0
            || report.CandidateValid && report.CandidateErrorCount != 0
            || report.Performed && report.NotPerformedReason is not null
            || !report.Performed && (
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
                && report.ErrorCount != report.Issues.Count)
        {
            throw new InvalidOperationException(
                "Candidate validator returned an invalid or unbounded report."
            );
        }
        return report with
        {
            NotPerformedReason = Bound(report.NotPerformedReason, 128),
            Issues = report.Issues.Select(issue => new WordPackageValidationIssue(
                Bound(issue.Id, 128),
                Bound(issue.ErrorType, 64) ?? "Unknown",
                Bound(issue.PartUri, 512),
                Bound(issue.Path, 512),
                Bound(issue.Node, 128)
            )).ToArray(),
        };
    }

    private static void ValidateInspectRequest(EquationParagraphRewriteInspectRequest request)
    {
        ValidatePathAndFingerprint(request.LocalPath, request.ExpectedPackageFingerprint);
        if (request.ParagraphNodeId is not null
            && !SemanticNodeId.HasValidSyntax(request.ParagraphNodeId))
        {
            throw Invalid("paragraph_node_id is not a valid semantic node ID");
        }
        if (request.Offset < 0)
        {
            throw Invalid("offset must be non-negative");
        }
        if (request.ParagraphNodeId is not null && request.Offset != 0)
        {
            throw Invalid("offset must be zero when paragraph_node_id is supplied");
        }
        if (request.MaxItems is < 1
            or > EquationParagraphRewriteWordPackageContract.MaximumInspectItems)
        {
            throw Invalid(
                $"max_items must be between 1 and {EquationParagraphRewriteWordPackageContract.MaximumInspectItems}"
            );
        }
        if (request.IncludeText && request.ParagraphNodeId is null)
        {
            throw Invalid("include_text requires one exact paragraph_node_id");
        }
    }

    private static void ValidatePlanRequest(
        string localPath,
        string expectedPackageFingerprint,
        IReadOnlyList<RewriteEquationParagraphTextCommand> commands
    )
    {
        ValidatePathAndFingerprint(localPath, expectedPackageFingerprint);
        if (commands is null
            || commands.Count is < 1
                or > EquationParagraphRewriteWordPackageContract.MaximumCommands)
        {
            throw Invalid(
                $"commands must contain between 1 and {EquationParagraphRewriteWordPackageContract.MaximumCommands} paragraph rewrites"
            );
        }
    }

    private static void ValidatePathAndFingerprint(
        string localPath,
        string expectedPackageFingerprint
    )
    {
        if (string.IsNullOrWhiteSpace(localPath))
        {
            throw Invalid("local_path must be a non-empty string");
        }
        if (localPath.Length
            > EquationParagraphRewriteWordPackageContract.MaximumLocalPathCharacters)
        {
            throw Invalid(
                $"local_path cannot exceed {EquationParagraphRewriteWordPackageContract.MaximumLocalPathCharacters} characters"
            );
        }
        if (!InspectWordPackageContract.IsSupportedFileName(localPath))
        {
            throw Invalid(
                "Equation paragraph rewrites accept DOCX, DOCM, DOTX, or DOTM files"
            );
        }
        if (!IsSha256(expectedPackageFingerprint))
        {
            throw Invalid(
                "expected_package_fingerprint must be exactly 64 hexadecimal characters"
            );
        }
    }

    private static void ValidateCommand(
        RewriteEquationParagraphTextCommand command,
        int index
    )
    {
        if (!IsCandidateId(command.CandidateId))
        {
            throw Invalid($"commands[{index}].candidate_id is invalid");
        }
        if (!IsSha256(command.ExpectedCandidateFingerprint))
        {
            throw Invalid(
                $"commands[{index}].expected_candidate_fingerprint must be exactly 64 hexadecimal characters"
            );
        }
        if (command.ReplacementTextSlots is null
            || command.ReplacementTextSlots.Count
                > EquationParagraphRewriteWordPackageContract.MaximumTextSlotsPerCommand)
        {
            throw Invalid(
                $"commands[{index}].replacement_text_slots exceeds the bounded slot limit"
            );
        }
        for (var slot = 0; slot < command.ReplacementTextSlots.Count; slot++)
        {
            var value = command.ReplacementTextSlots[slot]
                ?? throw Invalid(
                    $"commands[{index}].replacement_text_slots[{slot}] cannot be null"
                );
            if (value.Length
                > EquationParagraphRewriteWordPackageContract.MaximumTextCharactersPerSlot)
            {
                throw Invalid(
                    $"commands[{index}].replacement_text_slots[{slot}] exceeds the bounded character limit"
                );
            }
        }
    }

    private static IReadOnlyList<RewriteEquationParagraphTextCommand> SnapshotCommands(
        IReadOnlyList<RewriteEquationParagraphTextCommand> commands
    )
    {
        try
        {
            var snapshot = commands.Select(command => command with
            {
                ReplacementTextSlots = command.ReplacementTextSlots.ToArray(),
            }).ToArray();
            if (snapshot.Length != commands.Count
                || snapshot.Length is < 1
                    or > EquationParagraphRewriteWordPackageContract.MaximumCommands)
            {
                throw Invalid("commands changed while the request was being read");
            }
            return snapshot;
        }
        catch (WordToolkitOperationException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or NullReferenceException
        )
        {
            throw Invalid("commands changed while the request was being read", exception);
        }
    }

    private static string CreatePlanId(
        string enginePlanId,
        IReadOnlyList<string> intentFields
    )
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendHashField(hash, "wordtoolkit-equation-paragraph-rewrite-intent-v1");
        AppendHashField(hash, enginePlanId);
        foreach (var field in intentFields)
        {
            AppendHashField(hash, field);
        }
        var digest = hash.GetHashAndReset();
        return "weprplan_" + Convert.ToBase64String(digest.AsSpan(0, 15))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static string HashSlots(IEnumerable<string> slots)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var slot in slots)
        {
            AppendHashField(hash, slot);
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static void AppendHashField(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
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

    private static bool IsCandidateId(string value) =>
        value is not null
        && value.Length is >= 12 and <= 128
        && value.StartsWith("wepr_", StringComparison.Ordinal)
        && value[5..].All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '_' or '-'
        );

    private static bool IsPlanId(string value) =>
        value is not null
        && value.Length is >= 12 and <= 128
        && value.StartsWith("weprplan_", StringComparison.Ordinal)
        && value[9..].All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '_' or '-'
        );

    private static bool IsSha256(string value) =>
        value is not null && value.Length == 64 && value.All(Uri.IsHexDigit);

    private static WordToolkitOperationException ResultMismatch(string message) =>
        new("RESULT_MISMATCH", message);

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
        WordEquationParagraphRewriteLimitException limit =>
            new WordToolkitOperationException(
                "PACKAGE_LIMIT",
                "Equation paragraph projection exceeds a bounded safety limit",
                SafeReason(limit.Message, localPath),
                innerException: limit
            ),
        WordSemanticTransactionLimitException limit => new WordToolkitOperationException(
            "TRANSACTION_LIMIT",
            SafeReason(limit.Message, localPath)
                ?? "Equation paragraph transaction limit exceeded",
            innerException: limit
        ),
        WordSemanticPreconditionException conflict => new WordToolkitOperationException(
            "VERSION_CONFLICT",
            SafeReason(conflict.Message, localPath) ?? "Semantic precondition failed",
            innerException: conflict
        ),
        WordSemanticEditException edit => new WordToolkitOperationException(
            "UNSAFE_EDIT",
            SafeReason(edit.Message, localPath) ?? "Equation paragraph edit is unsafe",
            innerException: edit
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
        OpcPackageConcurrencyException conflict => new WordToolkitOperationException(
            "VERSION_CONFLICT",
            "Destination package changed during the atomic write",
            SafeReason(conflict.Message, localPath),
            retryable: true,
            innerException: conflict
        ),
        OpcPackageResultMismatchException mismatch => new WordToolkitOperationException(
            "RESULT_MISMATCH",
            "Candidate package does not match the reviewed equation paragraph plan",
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
        ArgumentException invalid => Invalid("Invalid equation paragraph rewrite", invalid),
        IOException io => new WordToolkitOperationException(
            "IO_ERROR",
            "The Word package could not be read or written",
            retryable: true,
            innerException: io
        ),
        _ => new WordToolkitOperationException(
            "INTERNAL_ERROR",
            "The equation paragraph operation failed",
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

    private sealed record LoadedCatalog(
        string Path,
        OpcPackageSnapshot Package,
        WordSemanticDocument Semantic,
        WordEquationParagraphRewriteCatalog Catalog
    );

    private sealed record ResolvedRewrite(
        int CommandIndex,
        WordEquationParagraphRewriteCandidate Before,
        WordEquationParagraphRewriteCandidate? After,
        IReadOnlyList<string> ReplacementTextSlots,
        int ChangedTextSlotCount,
        int TextNodeOperationCount,
        int BeforeCharacters,
        int AfterCharacters,
        string BeforeTextSlotsSha256,
        string AfterTextSlotsSha256
    );

    private sealed record ResolvedCommands(
        IReadOnlyList<WordTextReplacementCommand> TextCommands,
        IReadOnlyList<ResolvedRewrite> Rewrites,
        IReadOnlyList<string> IntentFields
    );

    private sealed record PlanContext(
        string Path,
        OpcPackageSnapshot Package,
        WordSemanticTransactionPlan Plan,
        string PlanId,
        int SubmittedCommandCount,
        IReadOnlyList<ResolvedRewrite> Rewrites,
        bool HasDigitalSignatures,
        WordPackageCandidateValidationReport Validation,
        bool ExactEquationBytesPreserved,
        bool ParagraphStructurePreserved,
        bool ExactInverseVerified
    );

    private sealed record CandidateOutcome(
        WordPackageCandidateValidationReport Validation,
        IReadOnlyList<ResolvedRewrite> Rewrites,
        bool ExactEquationBytesPreserved,
        bool ParagraphStructurePreserved,
        bool ExactInverseVerified
    );

}
