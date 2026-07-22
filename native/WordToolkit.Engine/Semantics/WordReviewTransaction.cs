using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Xml;

namespace WordToolkit.Engine.Semantics;

public enum WordReviewDecision
{
    Accept,
    Reject,
}

public sealed record WordReviewDecisionCommand(
    string RevisionId,
    WordReviewDecision Decision
);

public sealed record WordReviewTransactionOptions
{
    public static WordReviewTransactionOptions Default { get; } = new();

    public int MaxCommands { get; init; } = 1_000;

    public bool AllowCascadingRevisions { get; init; }

    internal void Validate()
    {
        if (MaxCommands <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxCommands));
        }
    }
}

public sealed record WordReviewDecisionBlock(
    string Code,
    string Message,
    string? RevisionId,
    string? PartUri,
    int? SourceElementOrdinal,
    IReadOnlyList<string> RelatedRevisionIds
);

public sealed record WordReviewDecisionOperationPlan(
    int Index,
    string RevisionId,
    WordReviewDecision Decision,
    WordRevisionKind RevisionKind,
    string Transformation,
    string SourcePartUri,
    int SourceElementOrdinal,
    bool IsImplicit,
    bool IsAbsorbed,
    string? AbsorbedByRevisionId,
    bool IsBlocked,
    int XmlByteDelta,
    int AffectedElementCount
);

public sealed record WordReviewPartChange(
    string PartUri,
    string EntryName,
    string BeforeSha256,
    string AfterSha256,
    int BeforeBytes,
    int AfterBytes
);

public sealed class WordReviewMutationPlan
{
    private readonly WordPackageTransactionCore _transaction;

    internal WordReviewMutationPlan(
        string planId,
        string basePackageFingerprint,
        string resultPackageFingerprint,
        IReadOnlyList<WordReviewDecisionOperationPlan> operations,
        IReadOnlyList<WordReviewDecisionBlock> blocks,
        IReadOnlyDictionary<string, WordPackagePartPayload> parts,
        int explicitCommandCount,
        int removedMoveMarkerCount
    )
    {
        PlanId = planId;
        BasePackageFingerprint = basePackageFingerprint;
        ResultPackageFingerprint = resultPackageFingerprint;
        Operations = new ReadOnlyCollection<WordReviewDecisionOperationPlan>(
            operations.ToArray()
        );
        Blocks = new ReadOnlyCollection<WordReviewDecisionBlock>(blocks.ToArray());
        ExplicitCommandCount = explicitCommandCount;
        RemovedMoveMarkerCount = removedMoveMarkerCount;
        _transaction = new WordPackageTransactionCore(
            basePackageFingerprint,
            resultPackageFingerprint,
            parts
        );
        ChangedParts = new ReadOnlyCollection<WordReviewPartChange>(
            _transaction.Parts
                .OrderBy(part => part.PartUri, StringComparer.Ordinal)
                .Select(part => new WordReviewPartChange(
                    part.PartUri,
                    part.EntryName,
                    part.BeforeSha256,
                    part.AfterSha256,
                    part.BeforeContent.Length,
                    part.AfterContent.Length
                ))
                .ToArray()
        );
    }

    public string PlanId { get; }

    public string BasePackageFingerprint { get; }

    public string ResultPackageFingerprint { get; }

    public IReadOnlyList<WordReviewDecisionOperationPlan> Operations { get; }

    public IReadOnlyList<WordReviewDecisionBlock> Blocks { get; }

    public IReadOnlyList<WordReviewPartChange> ChangedParts { get; }

    public int ExplicitCommandCount { get; }

    public int RemovedMoveMarkerCount { get; }

    public int OperationCount => Operations.Count;

    public int CascadeCount => Operations.Count(operation => operation.IsImplicit);

    public int BlockCount => Blocks.Count;

    public int ChangedPartCount => ChangedParts.Count;

    public int ChangedOperationCount => Operations.Count(operation =>
        !operation.IsBlocked && !operation.IsAbsorbed && operation.AffectedElementCount > 0
    );

    public bool CanApply => Blocks.Count == 0;

    public bool HasChanges => CanApply && _transaction.HasChanges;

    public long TotalXmlByteDelta => ChangedParts.Sum(part =>
        (long)part.AfterBytes - part.BeforeBytes
    );

    public OpcPackageMutationBuilder CreateMutation(OpcPackageSnapshot currentSnapshot)
    {
        EnsureApplicable();
        return _transaction.CreateMutation(currentSnapshot);
    }

    public OpcPackageMutationBuilder CreateInverseMutation(
        OpcPackageSnapshot appliedSnapshot
    )
    {
        EnsureApplicable();
        return _transaction.CreateInverseMutation(appliedSnapshot);
    }

    private void EnsureApplicable()
    {
        if (!CanApply)
        {
            throw new WordReviewDecisionBlockedException(Blocks);
        }
    }
}

public sealed class WordReviewMutationPlanner
{
    private const string WordTransitionalNamespace =
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string WordStrictNamespace =
        "http://purl.oclc.org/ooxml/wordprocessingml/main";
    private const string Word2010Namespace =
        "http://schemas.microsoft.com/office/word/2010/wordml";
    private readonly WordReviewTransactionOptions _options;
    private readonly LosslessXmlOptions _xmlOptions;

    public WordReviewMutationPlanner(
        WordReviewTransactionOptions? options = null,
        LosslessXmlOptions? xmlOptions = null
    )
    {
        _options = options ?? WordReviewTransactionOptions.Default;
        _xmlOptions = xmlOptions ?? LosslessXmlOptions.Default;
        _options.Validate();
        _xmlOptions.Validate();
    }

    public WordReviewMutationPlan Plan(
        OpcPackageSnapshot package,
        WordReviewGraph reviewGraph,
        IEnumerable<WordReviewDecisionCommand> commands,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(reviewGraph);
        ArgumentNullException.ThrowIfNull(commands);
        cancellationToken.ThrowIfCancellationRequested();
        if (
            !string.Equals(
                package.Fingerprint,
                reviewGraph.PackageFingerprint,
                StringComparison.Ordinal
            )
        )
        {
            throw new WordSemanticPreconditionException(
                "Review graph and package snapshot have different fingerprints."
            );
        }

        var materialized = commands.ToArray();
        if (materialized.Length == 0)
        {
            throw new ArgumentException(
                "A review transaction requires at least one decision command.",
                nameof(commands)
            );
        }
        if (materialized.Length > _options.MaxCommands)
        {
            throw new WordReviewTransactionLimitException(
                $"Review transaction exceeds {_options.MaxCommands} commands."
            );
        }

        var selections = ResolveExplicitSelections(reviewGraph, materialized);
        var blocks = new List<WordReviewDecisionBlock>();
        var blockKeys = new HashSet<string>(StringComparer.Ordinal);
        var moveBundles = new Dictionary<string, MoveBundle>(StringComparer.Ordinal);
        var sources = new Dictionary<string, PartSource>(StringComparer.Ordinal);
        var drafts = new Dictionary<string, OperationDraft>(StringComparer.Ordinal);
        var expanded = true;
        while (expanded)
        {
            cancellationToken.ThrowIfCancellationRequested();
            expanded = false;
            var countBeforeMoveExpansion = selections.Count;
            foreach (var (id, bundle) in ExpandMoveDependencies(
                reviewGraph,
                selections,
                blocks,
                blockKeys,
                cancellationToken
            ))
            {
                moveBundles.TryAdd(id, bundle);
            }
            expanded = selections.Count != countBeforeMoveExpansion;
            foreach (var selection in selections.Values.ToArray())
            {
                if (!drafts.ContainsKey(selection.Revision.Id))
                {
                    var binding = BindRevision(package, selection.Revision, sources, cancellationToken);
                    drafts.Add(
                        selection.Revision.Id,
                        BuildDraft(
                            binding,
                            selection,
                            blocks,
                            blockKeys,
                            cancellationToken
                        )
                    );
                }
            }

            foreach (var draft in drafts.Values.ToArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (draft.ForbiddenNestedSpan is { } forbidden)
                {
                    var nested = RevisionsInside(
                        reviewGraph,
                        package,
                        sources,
                        draft.Selection.Revision,
                        forbidden,
                        cancellationToken
                    );
                    if (nested.Count != 0)
                    {
                        AddBlock(
                            blocks,
                            blockKeys,
                            "nested_revision_not_supported",
                            "This decision rewrites deleted or historical markup that contains another tracked revision.",
                            draft.Selection.Revision,
                            nested.Select(revision => revision.Id)
                        );
                    }
                }

                if (draft.DestructiveSpan is not { } destructive)
                {
                    continue;
                }
                var affected = RevisionsInside(
                    reviewGraph,
                    package,
                    sources,
                    draft.Selection.Revision,
                    destructive,
                    cancellationToken
                );
                if (affected.Count == 0)
                {
                    continue;
                }
                if (draft.DisallowDestructiveDependencies)
                {
                    AddBlock(
                        blocks,
                        blockKeys,
                        "property_revision_overlap",
                        "Rejecting this property revision would replace a property container that also owns other revisions.",
                        draft.Selection.Revision,
                        affected.Select(revision => revision.Id)
                    );
                    continue;
                }

                foreach (var affectedRevision in affected)
                {
                    if (selections.TryGetValue(affectedRevision.Id, out var selected))
                    {
                        if (selected.Decision != draft.Selection.Decision)
                        {
                            AddBlock(
                                blocks,
                                blockKeys,
                                "conflicting_nested_decision",
                                "A destructive parent decision conflicts with a nested revision decision.",
                                draft.Selection.Revision,
                                [affectedRevision.Id]
                            );
                        }
                        else
                        {
                            selected.AbsorbedByRevisionId ??= draft.Selection.Revision.Id;
                        }
                        continue;
                    }

                    if (!_options.AllowCascadingRevisions)
                    {
                        AddBlock(
                            blocks,
                            blockKeys,
                            "unselected_nested_revision",
                            "The decision would silently consume an unselected nested revision.",
                            draft.Selection.Revision,
                            [affectedRevision.Id]
                        );
                        continue;
                    }
                    if (selections.Count >= _options.MaxCommands)
                    {
                        throw new WordReviewTransactionLimitException(
                            $"Cascading review transaction exceeds {_options.MaxCommands} revisions."
                        );
                    }
                    selections.Add(
                        affectedRevision.Id,
                        new Selection(
                            affectedRevision,
                            draft.Selection.Decision,
                            explicitIndex: null,
                            isImplicit: true
                        )
                        {
                            AbsorbedByRevisionId = draft.Selection.Revision.Id,
                        }
                    );
                    expanded = true;
                }
            }
        }

        var operations = BuildOperationPlans(selections, drafts, blocks);
        var payloads = new Dictionary<string, WordPackagePartPayload>(StringComparer.Ordinal);
        var removedMoveMarkerCount = 0;
        var resultFingerprint = package.Fingerprint;
        if (blocks.Count == 0)
        {
            var patchesByPart = new Dictionary<string, List<XmlSourcePatch>>(
                StringComparer.Ordinal
            );
            foreach (var draft in drafts.Values)
            {
                if (draft.Selection.AbsorbedByRevisionId is not null)
                {
                    continue;
                }
                if (!patchesByPart.TryGetValue(draft.Binding.Part.Uri, out var partPatches))
                {
                    partPatches = [];
                    patchesByPart.Add(draft.Binding.Part.Uri, partPatches);
                }
                partPatches.AddRange(draft.Patches);
            }
            removedMoveMarkerCount = AddMoveMarkerPatches(
                moveBundles,
                sources,
                patchesByPart,
                drafts.Values
            );

            var projectedEntries = new Dictionary<string, ReadOnlyMemory<byte>>(
                StringComparer.Ordinal
            );
            foreach (var (partUri, partPatches) in patchesByPart)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (partPatches.Count == 0)
                {
                    continue;
                }
                var source = sources[partUri];
                byte[] changed;
                try
                {
                    changed = source.Xml.ApplyPatches(
                        partPatches,
                        source.Part.Entry.Sha256,
                        cancellationToken
                    );
                }
                catch (LosslessXmlException exception)
                {
                    AddBlock(
                        blocks,
                        blockKeys,
                        "candidate_xml_invalid",
                        "The combined review decisions do not produce safe, well-formed XML: "
                            + exception.Message,
                        revision: null,
                        Array.Empty<string>(),
                        partUri
                    );
                    payloads.Clear();
                    break;
                }

                if (changed.AsSpan().SequenceEqual(source.Part.Entry.Content.Span))
                {
                    continue;
                }
                var payload = new WordPackagePartPayload(
                    source.Part.Uri,
                    source.Part.Entry.Name,
                    source.Part.Entry.Content.ToArray(),
                    changed
                );
                payloads.Add(partUri, payload);
                projectedEntries.Add(source.Part.Entry.Name, changed);
            }
            if (blocks.Count == 0 && payloads.Count != 0)
            {
                resultFingerprint = OpcPackageFingerprint.ComputeProjected(
                    package,
                    projectedEntries
                );
            }
        }

        if (blocks.Count != 0)
        {
            payloads.Clear();
            resultFingerprint = package.Fingerprint;
            removedMoveMarkerCount = 0;
        }
        var planId = CreatePlanId(
            package.Fingerprint,
            resultFingerprint,
            materialized,
            _options
        );
        return new WordReviewMutationPlan(
            planId,
            package.Fingerprint,
            resultFingerprint,
            operations,
            blocks,
            payloads,
            materialized.Length,
            removedMoveMarkerCount
        );
    }

    private Dictionary<string, Selection> ResolveExplicitSelections(
        WordReviewGraph graph,
        IReadOnlyList<WordReviewDecisionCommand> commands
    )
    {
        var selections = new Dictionary<string, Selection>(StringComparer.Ordinal);
        for (var index = 0; index < commands.Count; index++)
        {
            var command = commands[index]
                ?? throw new ArgumentException("A review decision command cannot be null.");
            ArgumentException.ThrowIfNullOrWhiteSpace(command.RevisionId);
            if (!Enum.IsDefined(command.Decision))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(commands),
                    command.Decision,
                    "Review decision must be Accept or Reject."
                );
            }
            if (!graph.TryGetRevision(command.RevisionId, out var revision) || revision is null)
            {
                throw new KeyNotFoundException(
                    $"Review revision '{command.RevisionId}' does not exist."
                );
            }
            if (!selections.TryAdd(
                    revision.Id,
                    new Selection(revision, command.Decision, index, isImplicit: false)
                ))
            {
                throw new WordSemanticEditException(
                    $"Review revision '{revision.Id}' is targeted more than once."
                );
            }
        }
        return selections;
    }

    private IReadOnlyDictionary<string, MoveBundle> ExpandMoveDependencies(
        WordReviewGraph graph,
        Dictionary<string, Selection> selections,
        List<WordReviewDecisionBlock> blocks,
        HashSet<string> blockKeys,
        CancellationToken cancellationToken
    )
    {
        var bundles = new Dictionary<string, MoveBundle>(StringComparer.Ordinal);
        foreach (var selection in selections.Values.ToArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (selection.Revision.Kind is not WordRevisionKind.MoveFrom
                and not WordRevisionKind.MoveTo)
            {
                continue;
            }
            var ranges = graph.MoveRanges
                .Where(range => range.RevisionIds.Contains(
                    selection.Revision.Id,
                    StringComparer.Ordinal
                ))
                .ToArray();
            if (ranges.Length != 1)
            {
                AddBlock(
                    blocks,
                    blockKeys,
                    "move_range_unresolved",
                    "The moved revision does not belong to exactly one tracked move range.",
                    selection.Revision,
                    Array.Empty<string>()
                );
                continue;
            }
            var range = ranges[0];
            var pairs = graph.Moves.Where(move =>
                string.Equals(move.SourceRangeId, range.Id, StringComparison.Ordinal)
                || string.Equals(move.DestinationRangeId, range.Id, StringComparison.Ordinal)
            ).ToArray();
            if (
                pairs.Length != 1
                || pairs[0].Status != WordMovePairStatus.Complete
                || pairs[0].SourceRangeId is null
                || pairs[0].DestinationRangeId is null
            )
            {
                AddBlock(
                    blocks,
                    blockKeys,
                    "move_pair_incomplete",
                    "The moved revision has no unique, complete source and destination pair.",
                    selection.Revision,
                    Array.Empty<string>()
                );
                continue;
            }
            var pair = pairs[0];
            var sourceRange = graph.MoveRanges.Single(item =>
                string.Equals(item.Id, pair.SourceRangeId, StringComparison.Ordinal)
            );
            var destinationRange = graph.MoveRanges.Single(item =>
                string.Equals(item.Id, pair.DestinationRangeId, StringComparison.Ordinal)
            );
            var markerOrdinals = new[]
            {
                sourceRange.StartElementOrdinal,
                sourceRange.EndElementOrdinal,
                destinationRange.StartElementOrdinal,
                destinationRange.EndElementOrdinal,
            };
            if (
                sourceRange.Status != WordReviewRangeStatus.Complete
                || destinationRange.Status != WordReviewRangeStatus.Complete
                || !string.Equals(sourceRange.PartUri, destinationRange.PartUri, StringComparison.Ordinal)
                || !string.Equals(sourceRange.StoryId, destinationRange.StoryId, StringComparison.Ordinal)
                || markerOrdinals.Any(ordinal => ordinal is null)
            )
            {
                AddBlock(
                    blocks,
                    blockKeys,
                    "move_markers_incomplete",
                    "The move pair has incomplete, cross-story, or cross-part range markers.",
                    selection.Revision,
                    Array.Empty<string>()
                );
                continue;
            }
            var relatedIds = sourceRange.RevisionIds
                .Concat(destinationRange.RevisionIds)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();
            if (relatedIds.Length == 0)
            {
                AddBlock(
                    blocks,
                    blockKeys,
                    "move_content_missing",
                    "The move pair contains no source or destination revision wrappers.",
                    selection.Revision,
                    Array.Empty<string>()
                );
                continue;
            }
            bundles.TryAdd(
                pair.Id,
                new MoveBundle(
                    pair.Id,
                    sourceRange.PartUri,
                    markerOrdinals.Select(ordinal => ordinal!.Value).ToArray(),
                    relatedIds
                )
            );
            foreach (var relatedId in relatedIds)
            {
                if (selections.TryGetValue(relatedId, out var relatedSelection))
                {
                    if (relatedSelection.Decision != selection.Decision)
                    {
                        AddBlock(
                            blocks,
                            blockKeys,
                            "move_decision_conflict",
                            "Source and destination revisions of one move have different decisions.",
                            selection.Revision,
                            [relatedId]
                        );
                    }
                    continue;
                }
                if (!_options.AllowCascadingRevisions)
                {
                    AddBlock(
                        blocks,
                        blockKeys,
                        "move_pair_not_selected",
                        "A move decision must include every source and destination revision in the pair.",
                        selection.Revision,
                        [relatedId]
                    );
                    continue;
                }
                if (selections.Count >= _options.MaxCommands)
                {
                    throw new WordReviewTransactionLimitException(
                        $"Cascading review transaction exceeds {_options.MaxCommands} revisions."
                    );
                }
                if (!graph.TryGetRevision(relatedId, out var relatedRevision)
                    || relatedRevision is null)
                {
                    throw new WordSemanticPreconditionException(
                        $"Move dependency revision '{relatedId}' disappeared from the review graph."
                    );
                }
                selections.Add(
                    relatedId,
                    new Selection(
                        relatedRevision,
                        selection.Decision,
                        explicitIndex: null,
                        isImplicit: true
                    )
                );
            }
        }
        return bundles;
    }

    private PartSource SourceFor(
        OpcPackageSnapshot package,
        string partUri,
        Dictionary<string, PartSource> sources,
        CancellationToken cancellationToken
    )
    {
        if (sources.TryGetValue(partUri, out var existing))
        {
            return existing;
        }
        if (!package.Parts.TryGetValue(partUri, out var part))
        {
            throw new WordSemanticPreconditionException(
                $"Review source part '{partUri}' no longer exists."
            );
        }
        try
        {
            var created = new PartSource(
                part,
                LosslessXmlDocument.Parse(part.Entry.Content, _xmlOptions, cancellationToken)
            );
            sources.Add(partUri, created);
            return created;
        }
        catch (LosslessXmlException exception)
        {
            throw new WordSemanticEditException(
                $"Review source part '{partUri}' cannot be edited losslessly.",
                exception
            );
        }
    }

    private RevisionBinding BindRevision(
        OpcPackageSnapshot package,
        WordRevisionDefinition revision,
        Dictionary<string, PartSource> sources,
        CancellationToken cancellationToken
    )
    {
        var source = SourceFor(package, revision.PartUri, sources, cancellationToken);
        XmlSourceElement element;
        XElement parsed;
        try
        {
            element = source.Xml.GetElement(revision.SourceElementOrdinal);
            parsed = source.Xml.GetParsedElement(revision.SourceElementOrdinal);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new WordSemanticPreconditionException(
                $"Revision source element {revision.SourceElementOrdinal} no longer exists.",
                exception
            );
        }
        if (!MatchesRevisionElement(revision.Kind, element))
        {
            throw new WordSemanticPreconditionException(
                $"Source element {element.Ordinal} no longer matches revision '{revision.Id}'."
            );
        }
        return new RevisionBinding(revision, source.Part, source.Xml, element, parsed);
    }

    private OperationDraft BuildDraft(
        RevisionBinding binding,
        Selection selection,
        List<WordReviewDecisionBlock> blocks,
        HashSet<string> blockKeys,
        CancellationToken cancellationToken
    )
    {
        var kind = selection.Revision.Kind;
        var decision = selection.Decision;
        if (kind is WordRevisionKind.Insertion or WordRevisionKind.ConflictInsertion)
        {
            return binding.Element.Children.Count != 0
                ? decision == WordReviewDecision.Accept
                    ? UnwrapDraft(binding, selection, "accept_insertion")
                    : RemoveDraft(binding, selection, "reject_insertion")
                : BuildEmptyInsertionDraft(binding, selection, blocks, blockKeys);
        }
        if (kind is WordRevisionKind.Deletion or WordRevisionKind.ConflictDeletion)
        {
            return binding.Element.Children.Count != 0
                ? decision == WordReviewDecision.Accept
                    ? RemoveDraft(binding, selection, "accept_deletion")
                    : RestoreDeletedContentDraft(binding, selection, cancellationToken)
                : BuildEmptyDeletionDraft(binding, selection, blocks, blockKeys);
        }
        if (kind == WordRevisionKind.MoveFrom)
        {
            if (binding.Element.Children.Count == 0)
            {
                return BlockedDraft(
                    binding,
                    selection,
                    blocks,
                    blockKeys,
                    "unsupported_empty_move",
                    "An empty move-from marker has no safe standalone decision semantics."
                );
            }
            return decision == WordReviewDecision.Accept
                ? RemoveDraft(binding, selection, "accept_move_source")
                : UnwrapDraft(binding, selection, "reject_move_source");
        }
        if (kind == WordRevisionKind.MoveTo)
        {
            if (binding.Element.Children.Count == 0)
            {
                return BlockedDraft(
                    binding,
                    selection,
                    blocks,
                    blockKeys,
                    "unsupported_empty_move",
                    "An empty move-to marker has no safe standalone decision semantics."
                );
            }
            return decision == WordReviewDecision.Accept
                ? UnwrapDraft(binding, selection, "accept_move_destination")
                : RemoveDraft(binding, selection, "reject_move_destination");
        }
        if (IsPropertyChange(kind))
        {
            return decision == WordReviewDecision.Accept
                ? RemoveDraft(binding, selection, "accept_property_change")
                : RestorePropertyDraft(binding, selection, blocks, blockKeys);
        }
        if (kind == WordRevisionKind.NumberingChange)
        {
            return decision == WordReviewDecision.Accept
                ? RemoveMarkerDraft(
                    binding,
                    selection,
                    "accept_numbering_change"
                )
                : BlockedDraft(
                    binding,
                    selection,
                    blocks,
                    blockKeys,
                    "unsupported_numbering_revision",
                    "Rejecting a numberingChange revision requires restoring or recalculating its previous LISTNUM or paragraph-numbering state."
                );
        }
        if (kind == WordRevisionKind.CellInsertion)
        {
            if (decision == WordReviewDecision.Accept)
            {
                return RemoveMarkerDraft(binding, selection, "accept_cell_insertion");
            }
            return BlockedDraft(
                binding,
                selection,
                blocks,
                blockKeys,
                "unsupported_table_revision",
                "Rejecting an inserted cell requires table-grid reconstruction."
            );
        }
        if (kind == WordRevisionKind.CellDeletion)
        {
            if (decision == WordReviewDecision.Reject)
            {
                return RemoveMarkerDraft(
                    binding,
                    selection,
                    "reject_cell_deletion"
                );
            }
            return BlockedDraft(
                binding,
                selection,
                blocks,
                blockKeys,
                "unsupported_table_revision",
                "Accepting a deleted cell requires table-grid reconstruction."
            );
        }
        if (kind == WordRevisionKind.CellMerge)
        {
            return BlockedDraft(
                binding,
                selection,
                blocks,
                blockKeys,
                "unsupported_table_revision",
                "Accepting or rejecting a cellMerge revision requires restoring the current or original vertical-merge state across the affected cells."
            );
        }
        return BlockedDraft(
            binding,
            selection,
            blocks,
            blockKeys,
            "unsupported_revision_kind",
            $"Revision kind '{kind}' is not covered by a proven lossless decision transform."
        );
    }

    private OperationDraft BuildEmptyInsertionDraft(
        RevisionBinding binding,
        Selection selection,
        List<WordReviewDecisionBlock> blocks,
        HashSet<string> blockKeys
    )
    {
        if (selection.Decision == WordReviewDecision.Accept)
        {
            return RemoveMarkerDraft(binding, selection, "accept_structural_insertion");
        }
        var parent = binding.ParsedElement.Parent;
        if (parent is not null && IsWordElement(parent, "trPr"))
        {
            return RemoveAncestorDraft(
                binding,
                selection,
                "tr",
                "reject_inserted_row",
                blocks,
                blockKeys
            );
        }
        if (parent is not null && IsWordElement(parent, "numPr"))
        {
            return RemoveAncestorDraft(
                binding,
                selection,
                "numPr",
                "reject_inserted_numbering_properties",
                blocks,
                blockKeys
            );
        }
        return BlockedDraft(
            binding,
            selection,
            blocks,
            blockKeys,
            "unsupported_structural_insertion",
            "Rejecting this structural insertion requires paragraph or math reconstruction."
        );
    }

    private OperationDraft BuildEmptyDeletionDraft(
        RevisionBinding binding,
        Selection selection,
        List<WordReviewDecisionBlock> blocks,
        HashSet<string> blockKeys
    )
    {
        if (selection.Decision == WordReviewDecision.Reject)
        {
            return RemoveMarkerDraft(binding, selection, "reject_structural_deletion");
        }
        var parent = binding.ParsedElement.Parent;
        if (parent is not null && IsWordElement(parent, "trPr"))
        {
            return RemoveAncestorDraft(
                binding,
                selection,
                "tr",
                "accept_deleted_row",
                blocks,
                blockKeys
            );
        }
        return BlockedDraft(
            binding,
            selection,
            blocks,
            blockKeys,
            "unsupported_structural_deletion",
            "Accepting this structural deletion requires paragraph or math reconstruction."
        );
    }

    private static OperationDraft RemoveDraft(
        RevisionBinding binding,
        Selection selection,
        string transformation
    )
    {
        var patch = binding.Source.CreateElementRemovalPatch(binding.Element.Ordinal);
        return new OperationDraft(
            binding,
            selection,
            transformation,
            [patch],
            binding.Element.FullSpan,
            forbiddenNestedSpan: null,
            disallowDestructiveDependencies: false
        );
    }

    private static OperationDraft RemoveMarkerDraft(
        RevisionBinding binding,
        Selection selection,
        string transformation
    ) => new(
        binding,
        selection,
        transformation,
        [binding.Source.CreateElementRemovalPatch(binding.Element.Ordinal)],
        binding.Element.FullSpan,
        forbiddenNestedSpan: null,
        disallowDestructiveDependencies: false
    );

    private static OperationDraft UnwrapDraft(
        RevisionBinding binding,
        Selection selection,
        string transformation
    ) => new(
        binding,
        selection,
        transformation,
        binding.Source.CreateElementUnwrapPatches(binding.Element.Ordinal),
        destructiveSpan: null,
        forbiddenNestedSpan: null,
        disallowDestructiveDependencies: false
    );

    private static OperationDraft RestoreDeletedContentDraft(
        RevisionBinding binding,
        Selection selection,
        CancellationToken cancellationToken
    )
    {
        var patches = binding.Source.CreateElementUnwrapPatches(binding.Element.Ordinal)
            .ToList();
        var affected = 1;
        foreach (var element in Descendants(binding.Element, cancellationToken))
        {
            if (!IsWordNamespace(element.NamespaceUri))
            {
                continue;
            }
            var newName = element.LocalName switch
            {
                "delText" => "t",
                "delInstrText" => "instrText",
                _ => null,
            };
            if (newName is null)
            {
                continue;
            }
            patches.AddRange(
                binding.Source.CreateElementLocalNameRenamePatches(
                    element.Ordinal,
                    newName
                )
            );
            affected++;
        }
        return new OperationDraft(
            binding,
            selection,
            "reject_deletion_restore_content",
            patches,
            destructiveSpan: null,
            forbiddenNestedSpan: binding.Element.FullSpan,
            disallowDestructiveDependencies: false,
            affectedElementCount: affected
        );
    }

    private OperationDraft RestorePropertyDraft(
        RevisionBinding binding,
        Selection selection,
        List<WordReviewDecisionBlock> blocks,
        HashSet<string> blockKeys
    )
    {
        var parent = binding.ParsedElement.Parent;
        var expectedParent = ExpectedPropertyParent(selection.Revision.Kind, binding.Element.LocalName);
        if (
            parent is null
            || !IsWordNamespace(parent.Name.NamespaceName)
            || !string.Equals(parent.Name.LocalName, expectedParent, StringComparison.Ordinal)
        )
        {
            return BlockedDraft(
                binding,
                selection,
                blocks,
                blockKeys,
                "property_parent_invalid",
                "The property change is not nested in its required property container."
            );
        }
        var snapshots = binding.ParsedElement.Elements(parent.Name).ToArray();
        if (snapshots.Length != 1)
        {
            return BlockedDraft(
                binding,
                selection,
                blocks,
                blockKeys,
                "property_snapshot_invalid",
                "The property change does not contain exactly one previous property snapshot."
            );
        }
        var parentOrdinal = binding.Source.GetElementOrdinal(parent);
        var snapshotOrdinal = binding.Source.GetElementOrdinal(snapshots[0]);
        var parentElement = binding.Source.GetElement(parentOrdinal);
        var snapshotElement = binding.Source.GetElement(snapshotOrdinal);
        if (!string.Equals(
                parentElement.QualifiedName,
                snapshotElement.QualifiedName,
                StringComparison.Ordinal
            ))
        {
            return BlockedDraft(
                binding,
                selection,
                blocks,
                blockKeys,
                "property_prefix_mismatch",
                "The previous property snapshot uses a different lexical prefix than its container."
            );
        }
        var replacement = Slice(binding.Source, snapshotElement.FullSpan);
        var affectedElements = 2;
        if (string.Equals(expectedParent, "pPr", StringComparison.Ordinal))
        {
            var currentRunProperties = parent.Elements()
                .Where(element => IsWordElement(element, "rPr"))
                .ToArray();
            var snapshotRunProperties = snapshots[0].Elements()
                .Where(element => IsWordElement(element, "rPr"))
                .ToArray();
            if (
                currentRunProperties.Length > 1
                || snapshotRunProperties.Length > 1
                || (currentRunProperties.Length == 1 && snapshotRunProperties.Length == 1)
            )
            {
                return BlockedDraft(
                    binding,
                    selection,
                    blocks,
                    blockKeys,
                    "paragraph_mark_properties_ambiguous",
                    "Paragraph property rejection found duplicate or competing paragraph-mark run properties."
                );
            }
            if (currentRunProperties.Length == 1)
            {
                var currentRunPropertiesElement = binding.Source.GetElement(
                    binding.Source.GetElementOrdinal(currentRunProperties[0])
                );
                replacement = AppendChildToSnapshot(
                    binding.Source,
                    snapshotElement,
                    currentRunPropertiesElement,
                    parentElement
                );
                affectedElements++;
            }
        }
        var patch = binding.Source.CreateElementReplacementPatch(
            parentOrdinal,
            replacement
        );
        return new OperationDraft(
            binding,
            selection,
            "reject_property_change_restore_snapshot",
            [patch],
            parentElement.FullSpan,
            forbiddenNestedSpan: null,
            disallowDestructiveDependencies: true,
            affectedElementCount: affectedElements
        );
    }

    private OperationDraft RemoveAncestorDraft(
        RevisionBinding binding,
        Selection selection,
        string ancestorLocalName,
        string transformation,
        List<WordReviewDecisionBlock> blocks,
        HashSet<string> blockKeys
    )
    {
        var ancestor = binding.ParsedElement.AncestorsAndSelf().FirstOrDefault(element =>
            IsWordElement(element, ancestorLocalName)
        );
        if (ancestor is null)
        {
            return BlockedDraft(
                binding,
                selection,
                blocks,
                blockKeys,
                "structural_parent_invalid",
                $"The revision has no required w:{ancestorLocalName} ancestor."
            );
        }
        var ordinal = binding.Source.GetElementOrdinal(ancestor);
        var element = binding.Source.GetElement(ordinal);
        return new OperationDraft(
            binding,
            selection,
            transformation,
            [binding.Source.CreateElementRemovalPatch(ordinal)],
            element.FullSpan,
            forbiddenNestedSpan: null,
            disallowDestructiveDependencies: false,
            affectedElementCount: 2
        );
    }

    private static OperationDraft BlockedDraft(
        RevisionBinding binding,
        Selection selection,
        List<WordReviewDecisionBlock> blocks,
        HashSet<string> blockKeys,
        string code,
        string message
    )
    {
        AddBlock(
            blocks,
            blockKeys,
            code,
            message,
            selection.Revision,
            Array.Empty<string>()
        );
        return new OperationDraft(
            binding,
            selection,
            "blocked",
            Array.Empty<XmlSourcePatch>(),
            destructiveSpan: null,
            forbiddenNestedSpan: null,
            disallowDestructiveDependencies: false,
            isBlocked: true,
            affectedElementCount: 0
        );
    }

    private IReadOnlyList<WordRevisionDefinition> RevisionsInside(
        WordReviewGraph graph,
        OpcPackageSnapshot package,
        Dictionary<string, PartSource> sources,
        WordRevisionDefinition owner,
        XmlSourceSpan span,
        CancellationToken cancellationToken
    )
    {
        var source = SourceFor(package, owner.PartUri, sources, cancellationToken);
        var result = new List<WordRevisionDefinition>();
        var inspected = 0;
        foreach (var revision in graph.Revisions)
        {
            if ((inspected++ & 0xff) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
            if (
                string.Equals(revision.Id, owner.Id, StringComparison.Ordinal)
                || !string.Equals(revision.PartUri, owner.PartUri, StringComparison.Ordinal)
            )
            {
                continue;
            }
            var candidate = source.Xml.GetElement(revision.SourceElementOrdinal).FullSpan;
            if (IsStrictlyInside(candidate, span))
            {
                result.Add(revision);
            }
        }
        return result.OrderBy(revision => revision.SourceElementOrdinal).ToArray();
    }

    private static IEnumerable<XmlSourceElement> Descendants(
        XmlSourceElement root,
        CancellationToken cancellationToken
    )
    {
        var pending = new Stack<XmlSourceElement>();
        for (var index = root.Children.Count - 1; index >= 0; index--)
        {
            pending.Push(root.Children[index]);
        }
        var visited = 0;
        while (pending.TryPop(out var current))
        {
            if ((visited++ & 0xff) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
            yield return current;
            for (var index = current.Children.Count - 1; index >= 0; index--)
            {
                pending.Push(current.Children[index]);
            }
        }
    }

    private static IReadOnlyList<WordReviewDecisionOperationPlan> BuildOperationPlans(
        IReadOnlyDictionary<string, Selection> selections,
        IReadOnlyDictionary<string, OperationDraft> drafts,
        IReadOnlyList<WordReviewDecisionBlock> blocks
    )
    {
        var blockedIds = blocks
            .Where(block => block.RevisionId is not null)
            .Select(block => block.RevisionId!)
            .ToHashSet(StringComparer.Ordinal);
        return selections.Values
            .OrderBy(selection => selection.Revision.PartUri, StringComparer.Ordinal)
            .ThenBy(selection => selection.Revision.SourceElementOrdinal)
            .ThenBy(selection => selection.Revision.Id, StringComparer.Ordinal)
            .Select((selection, index) =>
            {
                var draft = drafts[selection.Revision.Id];
                return new WordReviewDecisionOperationPlan(
                    index,
                    selection.Revision.Id,
                    selection.Decision,
                    selection.Revision.Kind,
                    selection.AbsorbedByRevisionId is null
                        ? draft.Transformation
                        : "absorbed_by_destructive_parent",
                    selection.Revision.PartUri,
                    selection.Revision.SourceElementOrdinal,
                    selection.IsImplicit,
                    selection.AbsorbedByRevisionId is not null,
                    selection.AbsorbedByRevisionId,
                    draft.IsBlocked || blockedIds.Contains(selection.Revision.Id),
                    selection.AbsorbedByRevisionId is null ? draft.XmlByteDelta : 0,
                    selection.AbsorbedByRevisionId is null ? draft.AffectedElementCount : 0
                );
            })
            .ToArray();
    }

    private static int AddMoveMarkerPatches(
        IReadOnlyDictionary<string, MoveBundle> bundles,
        IReadOnlyDictionary<string, PartSource> sources,
        Dictionary<string, List<XmlSourcePatch>> patchesByPart,
        IEnumerable<OperationDraft> drafts
    )
    {
        var count = 0;
        var destructiveSpans = drafts
            .Where(draft => draft.Selection.AbsorbedByRevisionId is null)
            .Where(draft => draft.DestructiveSpan is not null)
            .GroupBy(draft => draft.Binding.Part.Uri, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(draft => draft.DestructiveSpan!.Value).ToArray(),
                StringComparer.Ordinal
            );
        foreach (var bundle in bundles.Values.OrderBy(bundle => bundle.Id, StringComparer.Ordinal))
        {
            if (!sources.TryGetValue(bundle.PartUri, out var source))
            {
                continue;
            }
            if (!patchesByPart.TryGetValue(bundle.PartUri, out var patches))
            {
                patches = [];
                patchesByPart.Add(bundle.PartUri, patches);
            }
            foreach (var ordinal in bundle.MarkerOrdinals.Distinct())
            {
                count++;
                var marker = source.Xml.GetElement(ordinal);
                if (
                    destructiveSpans.TryGetValue(bundle.PartUri, out var covered)
                    && covered.Any(span => IsInsideOrEqual(marker.FullSpan, span))
                )
                {
                    continue;
                }
                patches.Add(source.Xml.CreateElementRemovalPatch(ordinal));
            }
        }
        return count;
    }

    private static byte[] AppendChildToSnapshot(
        LosslessXmlDocument source,
        XmlSourceElement snapshot,
        XmlSourceElement child,
        XmlSourceElement parent
    )
    {
        var childBytes = Slice(source, child.FullSpan);
        if (snapshot.IsSelfClosing)
        {
            var slashOffset = snapshot.SelfClosingSlashByteOffset
                ?? throw new WordSemanticEditException(
                    "Self-closing property snapshot has no lexical slash position."
                );
            var tailBytes = snapshot.StartTagSpan.EndByteOffset - slashOffset;
            if (tailBytes <= 1 || tailBytes % 2 != 0)
            {
                throw new WordSemanticEditException(
                    "Self-closing property snapshot has an invalid lexical terminator."
                );
            }
            var slashBytes = tailBytes / 2;
            var beforeSlash = source.SourceBytes.Slice(
                snapshot.StartTagSpan.ByteOffset,
                slashOffset - snapshot.StartTagSpan.ByteOffset
            );
            var closingAngle = source.SourceBytes.Slice(
                slashOffset + slashBytes,
                snapshot.StartTagSpan.EndByteOffset - slashOffset - slashBytes
            );
            var parentEnd = parent.EndTagSpan
                ?? throw new WordSemanticEditException(
                    "Property container has no lexical end tag."
                );
            return Combine(
                beforeSlash,
                closingAngle,
                childBytes,
                source.SourceBytes.Slice(parentEnd.ByteOffset, parentEnd.ByteLength)
            );
        }
        var snapshotEnd = snapshot.EndTagSpan
            ?? throw new WordSemanticEditException(
                "Property snapshot has no lexical end tag."
            );
        return Combine(
            source.SourceBytes.Slice(
                snapshot.FullSpan.ByteOffset,
                snapshotEnd.ByteOffset - snapshot.FullSpan.ByteOffset
            ),
            childBytes,
            source.SourceBytes.Slice(snapshotEnd.ByteOffset, snapshotEnd.ByteLength)
        );
    }

    private static byte[] Slice(LosslessXmlDocument source, XmlSourceSpan span) =>
        source.SourceBytes.Slice(span.ByteOffset, span.ByteLength).ToArray();

    private static byte[] Combine(params ReadOnlyMemory<byte>[] segments)
    {
        var length = segments.Sum(segment => (long)segment.Length);
        if (length > int.MaxValue)
        {
            throw new WordReviewTransactionLimitException(
                "Property snapshot replacement exceeds the supported byte length."
            );
        }
        var result = new byte[(int)length];
        var offset = 0;
        foreach (var segment in segments)
        {
            segment.Span.CopyTo(result.AsSpan(offset));
            offset += segment.Length;
        }
        return result;
    }

    private static void AddBlock(
        List<WordReviewDecisionBlock> blocks,
        HashSet<string> blockKeys,
        string code,
        string message,
        WordRevisionDefinition? revision,
        IEnumerable<string> relatedRevisionIds,
        string? partUri = null
    )
    {
        var related = relatedRevisionIds
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        var key = string.Join(
            '\u001f',
            code,
            revision?.Id ?? string.Empty,
            partUri ?? revision?.PartUri ?? string.Empty,
            string.Join(',', related)
        );
        if (!blockKeys.Add(key))
        {
            return;
        }
        blocks.Add(
            new WordReviewDecisionBlock(
                code,
                message,
                revision?.Id,
                partUri ?? revision?.PartUri,
                revision?.SourceElementOrdinal,
                related
            )
        );
    }

    private static string CreatePlanId(
        string packageFingerprint,
        string resultPackageFingerprint,
        IReadOnlyList<WordReviewDecisionCommand> commands,
        WordReviewTransactionOptions options
    )
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendHashField(hash, "word-review-decisions-v1");
        AppendHashField(hash, packageFingerprint);
        AppendHashField(hash, resultPackageFingerprint);
        AppendHashField(hash, options.AllowCascadingRevisions ? "cascade" : "exact");
        foreach (var command in commands.OrderBy(command => command.RevisionId, StringComparer.Ordinal))
        {
            AppendHashField(hash, command.RevisionId);
            AppendHashField(hash, command.Decision.ToString());
        }
        var digest = hash.GetHashAndReset();
        return "wrplan_" + Convert.ToBase64String(digest.AsSpan(0, 15))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static void AppendHashField(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[sizeof(int)];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }

    private static bool MatchesRevisionElement(
        WordRevisionKind kind,
        XmlSourceElement element
    )
    {
        var expected = kind switch
        {
            WordRevisionKind.Insertion => "ins",
            WordRevisionKind.Deletion => "del",
            WordRevisionKind.MoveFrom => "moveFrom",
            WordRevisionKind.MoveTo => "moveTo",
            WordRevisionKind.ConflictInsertion => "conflictIns",
            WordRevisionKind.ConflictDeletion => "conflictDel",
            WordRevisionKind.RunPropertiesChange => "rPrChange",
            WordRevisionKind.ParagraphPropertiesChange => "pPrChange",
            WordRevisionKind.TablePropertiesChange => "tblPrChange",
            WordRevisionKind.TableGridChange => "tblGridChange",
            WordRevisionKind.TableRowPropertiesChange => "trPrChange",
            WordRevisionKind.TableCellPropertiesChange => "tcPrChange",
            WordRevisionKind.SectionPropertiesChange => "sectPrChange",
            WordRevisionKind.NumberingPropertiesChange => "numPrChange",
            WordRevisionKind.NumberingChange => "numberingChange",
            WordRevisionKind.CellInsertion => "cellIns",
            WordRevisionKind.CellDeletion => "cellDel",
            WordRevisionKind.CellMerge => "cellMerge",
            WordRevisionKind.CustomXmlInsertion => "customXmlInsRangeStart",
            WordRevisionKind.CustomXmlDeletion => "customXmlDelRangeStart",
            WordRevisionKind.OtherPropertyChange => element.LocalName.EndsWith(
                "PrChange",
                StringComparison.Ordinal
            )
                || string.Equals(
                    element.LocalName,
                    "tblPrExChange",
                    StringComparison.Ordinal
                )
                ? element.LocalName
                : string.Empty,
            _ => string.Empty,
        };
        var namespaceMatches = kind is WordRevisionKind.ConflictInsertion
            or WordRevisionKind.ConflictDeletion
            ? string.Equals(
                element.NamespaceUri,
                Word2010Namespace,
                StringComparison.Ordinal
            )
            : IsWordNamespace(element.NamespaceUri);
        return namespaceMatches
            && expected.Length != 0
            && string.Equals(element.LocalName, expected, StringComparison.Ordinal);
    }

    private static bool IsPropertyChange(WordRevisionKind kind) => kind is
        WordRevisionKind.RunPropertiesChange
        or WordRevisionKind.ParagraphPropertiesChange
        or WordRevisionKind.TablePropertiesChange
        or WordRevisionKind.TableGridChange
        or WordRevisionKind.TableRowPropertiesChange
        or WordRevisionKind.TableCellPropertiesChange
        or WordRevisionKind.SectionPropertiesChange
        or WordRevisionKind.NumberingPropertiesChange
        or WordRevisionKind.OtherPropertyChange;

    private static string ExpectedPropertyParent(
        WordRevisionKind kind,
        string changeLocalName
    ) => kind switch
    {
        WordRevisionKind.RunPropertiesChange => "rPr",
        WordRevisionKind.ParagraphPropertiesChange => "pPr",
        WordRevisionKind.TablePropertiesChange => "tblPr",
        WordRevisionKind.TableGridChange => "tblGrid",
        WordRevisionKind.TableRowPropertiesChange => "trPr",
        WordRevisionKind.TableCellPropertiesChange => "tcPr",
        WordRevisionKind.SectionPropertiesChange => "sectPr",
        WordRevisionKind.NumberingPropertiesChange => "numPr",
        WordRevisionKind.OtherPropertyChange when changeLocalName.EndsWith(
            "Change",
            StringComparison.Ordinal
        ) => changeLocalName[..^"Change".Length],
        _ => string.Empty,
    };

    private static bool IsWordElement(XElement element, string localName) =>
        IsWordNamespace(element.Name.NamespaceName)
        && string.Equals(element.Name.LocalName, localName, StringComparison.Ordinal);

    private static bool IsWordNamespace(string namespaceUri) =>
        namespaceUri is WordTransitionalNamespace or WordStrictNamespace;

    private static bool IsStrictlyInside(XmlSourceSpan candidate, XmlSourceSpan owner) =>
        candidate.ByteOffset >= owner.ByteOffset
        && candidate.EndByteOffset <= owner.EndByteOffset
        && (
            candidate.ByteOffset != owner.ByteOffset
            || candidate.ByteLength != owner.ByteLength
        );

    private static bool IsInsideOrEqual(XmlSourceSpan candidate, XmlSourceSpan owner) =>
        candidate.ByteOffset >= owner.ByteOffset
        && candidate.EndByteOffset <= owner.EndByteOffset;

    private sealed record PartSource(OpcPart Part, LosslessXmlDocument Xml);

    private sealed record RevisionBinding(
        WordRevisionDefinition Revision,
        OpcPart Part,
        LosslessXmlDocument Source,
        XmlSourceElement Element,
        XElement ParsedElement
    );

    private sealed class Selection
    {
        internal Selection(
            WordRevisionDefinition revision,
            WordReviewDecision decision,
            int? explicitIndex,
            bool isImplicit
        )
        {
            Revision = revision;
            Decision = decision;
            ExplicitIndex = explicitIndex;
            IsImplicit = isImplicit;
        }

        internal WordRevisionDefinition Revision { get; }
        internal WordReviewDecision Decision { get; }
        internal int? ExplicitIndex { get; }
        internal bool IsImplicit { get; }
        internal string? AbsorbedByRevisionId { get; set; }
    }

    private sealed class OperationDraft
    {
        internal OperationDraft(
            RevisionBinding binding,
            Selection selection,
            string transformation,
            IReadOnlyList<XmlSourcePatch> patches,
            XmlSourceSpan? destructiveSpan,
            XmlSourceSpan? forbiddenNestedSpan,
            bool disallowDestructiveDependencies,
            bool isBlocked = false,
            int? affectedElementCount = null
        )
        {
            Binding = binding;
            Selection = selection;
            Transformation = transformation;
            Patches = patches;
            DestructiveSpan = destructiveSpan;
            ForbiddenNestedSpan = forbiddenNestedSpan;
            DisallowDestructiveDependencies = disallowDestructiveDependencies;
            IsBlocked = isBlocked;
            AffectedElementCount = affectedElementCount ?? patches.Count;
            XmlByteDelta = patches.Sum(patch =>
                patch.Replacement.Length - patch.ByteLength
            );
        }

        internal RevisionBinding Binding { get; }
        internal Selection Selection { get; }
        internal string Transformation { get; }
        internal IReadOnlyList<XmlSourcePatch> Patches { get; }
        internal XmlSourceSpan? DestructiveSpan { get; }
        internal XmlSourceSpan? ForbiddenNestedSpan { get; }
        internal bool DisallowDestructiveDependencies { get; }
        internal bool IsBlocked { get; }
        internal int XmlByteDelta { get; }
        internal int AffectedElementCount { get; }
    }

    private sealed record MoveBundle(
        string Id,
        string PartUri,
        IReadOnlyList<int> MarkerOrdinals,
        IReadOnlyList<string> RevisionIds
    );
}

public sealed class WordReviewDecisionBlockedException : WordSemanticEditException
{
    public WordReviewDecisionBlockedException(
        IReadOnlyList<WordReviewDecisionBlock> blocks
    )
        : base(
            blocks.Count == 0
                ? "Review decision plan is blocked."
                : "Review decision plan is blocked: "
                    + string.Join(" | ", blocks.Take(5).Select(block =>
                        $"{block.Code}: {block.Message}"
                    ))
        )
    {
        Blocks = blocks;
    }

    public IReadOnlyList<WordReviewDecisionBlock> Blocks { get; }
}

public sealed class WordReviewTransactionLimitException : WordSemanticEditException
{
    public WordReviewTransactionLimitException(string message)
        : base(message)
    {
    }
}
