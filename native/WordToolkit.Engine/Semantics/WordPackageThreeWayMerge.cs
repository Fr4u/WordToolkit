using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using WordToolkit.Engine.Packaging;

namespace WordToolkit.Engine.Semantics;

public enum WordPackageMergeConflictKind
{
    AddedDifferently,
    ModifiedDifferently,
    DeletedOnLeftModifiedOnRight,
    ModifiedOnLeftDeletedOnRight,
    SemanticTextChangedDifferently,
}

public enum WordPackageMergeResolutionChoice
{
    UseAncestor,
    UseLeft,
    UseRight,
}

public enum WordPackageMergeEntryOutcome
{
    Unchanged,
    Left,
    Right,
    Shared,
    SemanticTextMerge,
    Conflict,
}

public sealed record WordPackageMergeTextSnapshot(
    int CharacterCount,
    string Sha256,
    string Preview,
    bool PreviewTruncated
);

public sealed record WordPackageMergeConflict(
    string ConflictId,
    WordPackageMergeConflictKind Kind,
    string EntryName,
    string? PartUri,
    string? SourcePath,
    SemanticNodeId? AncestorNodeId,
    string? AncestorSha256,
    long? AncestorBytes,
    string? LeftSha256,
    long? LeftBytes,
    string? RightSha256,
    long? RightBytes,
    WordPackageMergeTextSnapshot? AncestorText,
    WordPackageMergeTextSnapshot? LeftText,
    WordPackageMergeTextSnapshot? RightText,
    bool IsInfrastructure
);

public sealed record WordPackageMergeResolution(
    string ConflictId,
    WordPackageMergeResolutionChoice Choice
);

public sealed record WordPackageMergeEntryDecision(
    string EntryName,
    string? PartUri,
    WordPackageMergeEntryOutcome Outcome,
    int SemanticTextChangeCount,
    int ConflictCount,
    bool IsInfrastructure
);

public sealed record WordPackageMergeOptions
{
    public static WordPackageMergeOptions Default { get; } = new();

    public int MaxEntries { get; init; } = 20_000;

    public int MaxConflicts { get; init; } = 20_000;

    public int MaxSemanticTextNodesPerPart { get; init; } = 250_000;

    public int MaxSemanticTextChanges { get; init; } = 20_000;

    public long MaxSemanticReplacementCharacters { get; init; } =
        64L * 1024 * 1024;

    public int TextPreviewCharacters { get; init; } = 160;

    internal void Validate()
    {
        if (MaxEntries <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxEntries));
        }
        if (MaxConflicts <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxConflicts));
        }
        if (MaxSemanticTextNodesPerPart <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxSemanticTextNodesPerPart));
        }
        if (MaxSemanticTextChanges <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxSemanticTextChanges));
        }
        if (MaxSemanticReplacementCharacters <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxSemanticReplacementCharacters)
            );
        }
        if (TextPreviewCharacters is < 1 or > 4_096)
        {
            throw new ArgumentOutOfRangeException(nameof(TextPreviewCharacters));
        }
    }
}

public sealed record WordPackageMergePolicyDecision(
    bool CanApply,
    IReadOnlyList<string> BlockCodes
);

public sealed class WordPackageMergePlan
{
    internal WordPackageMergePlan(
        string mergeId,
        string ancestorPackageFingerprint,
        string leftPackageFingerprint,
        string rightPackageFingerprint,
        IReadOnlyList<WordPackageMergeEntryDecision> entryDecisions,
        IReadOnlyList<WordPackageMergeConflict> conflicts,
        IReadOnlyDictionary<string, WordPackageMergeResolutionChoice> resolutions,
        WordPackagePatchPlan? resultPlan,
        OpcPackageSnapshot? candidatePackage
    )
    {
        MergeId = mergeId;
        AncestorPackageFingerprint = ancestorPackageFingerprint;
        LeftPackageFingerprint = leftPackageFingerprint;
        RightPackageFingerprint = rightPackageFingerprint;
        EntryDecisions = new ReadOnlyCollection<WordPackageMergeEntryDecision>(
            entryDecisions.ToArray()
        );
        Conflicts = new ReadOnlyCollection<WordPackageMergeConflict>(
            conflicts.ToArray()
        );
        Resolutions = new ReadOnlyDictionary<string, WordPackageMergeResolutionChoice>(
            new Dictionary<string, WordPackageMergeResolutionChoice>(
                resolutions,
                StringComparer.Ordinal
            )
        );
        ResultPlan = resultPlan;
        CandidatePackage = candidatePackage;
        UnresolvedConflictIds = new ReadOnlyCollection<string>(
            conflicts.Where(conflict => !resolutions.ContainsKey(conflict.ConflictId))
                .Select(conflict => conflict.ConflictId)
                .ToArray()
        );
    }

    public string MergeId { get; }

    public string AncestorPackageFingerprint { get; }

    public string LeftPackageFingerprint { get; }

    public string RightPackageFingerprint { get; }

    public IReadOnlyList<WordPackageMergeEntryDecision> EntryDecisions { get; }

    public IReadOnlyList<WordPackageMergeConflict> Conflicts { get; }

    public IReadOnlyDictionary<string, WordPackageMergeResolutionChoice> Resolutions { get; }

    public IReadOnlyList<string> UnresolvedConflictIds { get; }

    public WordPackagePatchPlan? ResultPlan { get; }

    public OpcPackageSnapshot? CandidatePackage { get; }

    public int ConflictCount => Conflicts.Count;

    public int ResolvedConflictCount => Resolutions.Count;

    public int UnresolvedConflictCount => UnresolvedConflictIds.Count;

    public bool HasConflicts => Conflicts.Count != 0;

    public bool CanMaterialize => ResultPlan is not null && CandidatePackage is not null;

    public string? ResultPackageFingerprint => ResultPlan?.Patch.ResultPackageFingerprint;

    public OpcPackagePatch? Patch => ResultPlan?.Patch;

    public WordPackageMergePolicyDecision Evaluate(
        WordPackagePatchApplyPolicy? policy = null
    )
    {
        var blocks = new List<string>();
        if (UnresolvedConflictCount != 0)
        {
            blocks.Add("unresolved_merge_conflicts");
        }
        if (ResultPlan is null)
        {
            if (blocks.Count == 0)
            {
                blocks.Add("merge_result_not_materialized");
            }
        }
        else
        {
            blocks.AddRange(ResultPlan.Evaluate(policy).BlockCodes);
        }
        return new WordPackageMergePolicyDecision(
            blocks.Count == 0,
            new ReadOnlyCollection<string>(blocks)
        );
    }

    public OpcPackageMutationBuilder CreateMutation(
        OpcPackageSnapshot currentAncestor
    ) => Patch?.CreateMutation(currentAncestor)
        ?? throw new WordPackageMergePreconditionException(
            "The merge still has unresolved conflicts and has no materialized result."
        );
}

public sealed class WordPackageThreeWayMergePlanner
{
    private readonly WordPackageMergeOptions _options;
    private readonly WordSemanticTransactionPlanner _textPlanner;
    private readonly WordPackagePatchPlanner _patchPlanner;
    private readonly OpcPackageReader _reader = new();
    private readonly OpcPackageSerializer _serializer = new();

    public WordPackageThreeWayMergePlanner(
        WordPackageMergeOptions? options = null,
        OpcPackagePatchLimits? patchLimits = null,
        WordSemanticDiffOptions? diffOptions = null
    )
    {
        _options = options ?? WordPackageMergeOptions.Default;
        _options.Validate();
        _textPlanner = new WordSemanticTransactionPlanner(
            new WordSemanticTransactionOptions
            {
                MaxCommands = _options.MaxSemanticTextChanges,
                MaxTotalReplacementCharacters =
                    _options.MaxSemanticReplacementCharacters,
            }
        );
        _patchPlanner = new WordPackagePatchPlanner(patchLimits, diffOptions);
    }

    public WordPackageMergePlan Plan(
        OpcPackageSnapshot ancestorPackage,
        WordSemanticDocument ancestorDocument,
        OpcPackageSnapshot leftPackage,
        WordSemanticDocument leftDocument,
        OpcPackageSnapshot rightPackage,
        WordSemanticDocument rightDocument,
        IEnumerable<WordPackageMergeResolution>? resolutions = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(ancestorPackage);
        ArgumentNullException.ThrowIfNull(ancestorDocument);
        ArgumentNullException.ThrowIfNull(leftPackage);
        ArgumentNullException.ThrowIfNull(leftDocument);
        ArgumentNullException.ThrowIfNull(rightPackage);
        ArgumentNullException.ThrowIfNull(rightDocument);
        cancellationToken.ThrowIfCancellationRequested();
        AssertProjection(ancestorPackage, ancestorDocument, "ancestor");
        AssertProjection(leftPackage, leftDocument, "left");
        AssertProjection(rightPackage, rightDocument, "right");

        var ancestorEntries = UniqueEntries(ancestorPackage, "ancestor");
        var leftEntries = UniqueEntries(leftPackage, "left");
        var rightEntries = UniqueEntries(rightPackage, "right");
        var names = ancestorEntries.Keys.Concat(leftEntries.Keys)
            .Concat(rightEntries.Keys)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (names.Length > _options.MaxEntries)
        {
            throw new WordPackageMergeLimitException(
                $"Merge examines more than {_options.MaxEntries} package entries."
            );
        }

        var conflicts = new List<WordPackageMergeConflict>();
        var decisions = new List<WordPackageMergeEntryDecision>(names.Length);
        var work = new List<EntryWork>(names.Length);
        var semanticChangeCount = 0;
        long semanticCharacters = 0;
        foreach (var name in names)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ancestorEntries.TryGetValue(name, out var ancestorEntry);
            leftEntries.TryGetValue(name, out var leftEntry);
            rightEntries.TryGetValue(name, out var rightEntry);
            var partUri = rightEntry?.PartUri ?? leftEntry?.PartUri ?? ancestorEntry?.PartUri;
            var infrastructure = ancestorEntry?.IsInfrastructure == true
                || leftEntry?.IsInfrastructure == true
                || rightEntry?.IsInfrastructure == true;

            if (Equivalent(leftEntry, rightEntry))
            {
                var outcome = Equivalent(ancestorEntry, leftEntry)
                    ? WordPackageMergeEntryOutcome.Unchanged
                    : WordPackageMergeEntryOutcome.Shared;
                work.Add(EntryWork.Direct(
                    name,
                    ancestorEntry,
                    leftEntry,
                    rightEntry,
                    leftEntry,
                    outcome
                ));
                decisions.Add(Decision(name, partUri, outcome, 0, 0, infrastructure));
                continue;
            }
            if (Equivalent(ancestorEntry, leftEntry))
            {
                work.Add(EntryWork.Direct(
                    name,
                    ancestorEntry,
                    leftEntry,
                    rightEntry,
                    rightEntry,
                    WordPackageMergeEntryOutcome.Right
                ));
                decisions.Add(Decision(
                    name,
                    partUri,
                    WordPackageMergeEntryOutcome.Right,
                    0,
                    0,
                    infrastructure
                ));
                continue;
            }
            if (Equivalent(ancestorEntry, rightEntry))
            {
                work.Add(EntryWork.Direct(
                    name,
                    ancestorEntry,
                    leftEntry,
                    rightEntry,
                    leftEntry,
                    WordPackageMergeEntryOutcome.Left
                ));
                decisions.Add(Decision(
                    name,
                    partUri,
                    WordPackageMergeEntryOutcome.Left,
                    0,
                    0,
                    infrastructure
                ));
                continue;
            }

            var semantic = TryBuildSemanticTextWork(
                ancestorPackage,
                ancestorDocument,
                ancestorEntry,
                leftPackage,
                leftDocument,
                leftEntry,
                rightPackage,
                rightDocument,
                rightEntry,
                cancellationToken
            );
            if (semantic is not null)
            {
                semanticChangeCount = checked(
                    semanticChangeCount + semantic.Values.Count(value => value.HasChange)
                );
                semanticCharacters = checked(
                    semanticCharacters + semantic.Values.Where(value => value.HasChange)
                        .Sum(value => (long)Math.Max(
                            value.LeftText?.Length ?? 0,
                            value.RightText?.Length ?? 0
                        ))
                );
                CheckSemanticBudgets(semanticChangeCount, semanticCharacters);
                foreach (var value in semantic.Values.Where(value => value.IsConflict))
                {
                    AddConflict(conflicts, CreateSemanticConflict(
                        ancestorPackage.Fingerprint,
                        leftPackage.Fingerprint,
                        rightPackage.Fingerprint,
                        name,
                        partUri,
                        value,
                        infrastructure
                    ));
                }
                work.Add(EntryWork.CreateSemantic(
                    name,
                    ancestorEntry!,
                    leftEntry!,
                    rightEntry!,
                    semantic
                ));
                decisions.Add(Decision(
                    name,
                    partUri,
                    WordPackageMergeEntryOutcome.SemanticTextMerge,
                    semantic.Values.Count(value => value.HasChange),
                    semantic.Values.Count(value => value.IsConflict),
                    infrastructure
                ));
                continue;
            }

            var conflict = CreateEntryConflict(
                ancestorPackage.Fingerprint,
                leftPackage.Fingerprint,
                rightPackage.Fingerprint,
                name,
                ancestorEntry,
                leftEntry,
                rightEntry,
                infrastructure
            );
            AddConflict(conflicts, conflict);
            work.Add(EntryWork.CreateConflict(
                name,
                ancestorEntry,
                leftEntry,
                rightEntry,
                conflict
            ));
            decisions.Add(Decision(
                name,
                partUri,
                WordPackageMergeEntryOutcome.Conflict,
                0,
                1,
                infrastructure
            ));
        }

        var resolutionMap = ValidateResolutions(resolutions, conflicts);
        var unresolved = conflicts.Any(conflict =>
            !resolutionMap.ContainsKey(conflict.ConflictId)
        );
        WordPackagePatchPlan? resultPlan = null;
        OpcPackageSnapshot? candidate = null;
        if (!unresolved)
        {
            var mutation = new OpcPackageMutationBuilder(ancestorPackage);
            foreach (var entryWork in work)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ApplyEntryWork(
                    mutation,
                    ancestorPackage,
                    ancestorDocument,
                    entryWork,
                    resolutionMap,
                    cancellationToken
                );
            }
            candidate = Materialize(mutation, cancellationToken);
            var candidateDocument = new WordSemanticProjector().Project(
                candidate,
                cancellationToken
            );
            resultPlan = _patchPlanner.Plan(
                ancestorPackage,
                ancestorDocument,
                candidate,
                candidateDocument,
                cancellationToken
            );
        }

        var mergeId = ComputeMergeId(
            ancestorPackage.Fingerprint,
            leftPackage.Fingerprint,
            rightPackage.Fingerprint,
            decisions,
            conflicts,
            resolutionMap,
            resultPlan?.Patch.PatchId
        );
        return new WordPackageMergePlan(
            mergeId,
            ancestorPackage.Fingerprint,
            leftPackage.Fingerprint,
            rightPackage.Fingerprint,
            decisions,
            conflicts,
            resolutionMap,
            resultPlan,
            candidate
        );
    }

    private SemanticEntryWork? TryBuildSemanticTextWork(
        OpcPackageSnapshot ancestorPackage,
        WordSemanticDocument ancestorDocument,
        OpcPackageEntry? ancestorEntry,
        OpcPackageSnapshot leftPackage,
        WordSemanticDocument leftDocument,
        OpcPackageEntry? leftEntry,
        OpcPackageSnapshot rightPackage,
        WordSemanticDocument rightDocument,
        OpcPackageEntry? rightEntry,
        CancellationToken cancellationToken
    )
    {
        if (
            ancestorEntry?.PartUri is not { } partUri
            || leftEntry?.PartUri != partUri
            || rightEntry?.PartUri != partUri
            || !SameContentType(ancestorPackage, leftPackage, partUri)
            || !SameContentType(ancestorPackage, rightPackage, partUri)
        )
        {
            return null;
        }

        var ancestorNodes = TextNodes(ancestorDocument, partUri);
        var leftNodes = TextNodes(leftDocument, partUri);
        var rightNodes = TextNodes(rightDocument, partUri);
        if (
            ancestorNodes.Count == 0
            || ancestorNodes.Count > _options.MaxSemanticTextNodesPerPart
            || leftNodes.Count != ancestorNodes.Count
            || rightNodes.Count != ancestorNodes.Count
            || !SameKeys(ancestorNodes, leftNodes)
            || !SameKeys(ancestorNodes, rightNodes)
        )
        {
            return null;
        }

        var leftChanges = ExtractChanges(ancestorNodes, leftNodes);
        var rightChanges = ExtractChanges(ancestorNodes, rightNodes);
        if (leftChanges.Count == 0 || rightChanges.Count == 0)
        {
            return null;
        }
        if (!ReconstructsEntryExactly(
                ancestorPackage,
                ancestorDocument,
                leftEntry,
                leftChanges.Values,
                cancellationToken
            )
            || !ReconstructsEntryExactly(
                ancestorPackage,
                ancestorDocument,
                rightEntry,
                rightChanges.Values,
                cancellationToken
            ))
        {
            return null;
        }

        var paths = leftChanges.Keys.Concat(rightChanges.Keys)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var values = new List<SemanticValueWork>(paths.Length);
        foreach (var path in paths)
        {
            var ancestorNode = ancestorNodes[path];
            leftChanges.TryGetValue(path, out var left);
            rightChanges.TryGetValue(path, out var right);
            var ancestorText = ancestorNode.Text ?? string.Empty;
            var leftText = left?.NewText;
            var rightText = right?.NewText;
            values.Add(new SemanticValueWork(
                ancestorNode,
                ancestorText,
                leftText,
                rightText,
                leftText is not null
                    && rightText is not null
                    && !string.Equals(leftText, rightText, StringComparison.Ordinal)
            ));
        }
        return new SemanticEntryWork(partUri, values);
    }

    private IReadOnlyDictionary<string, WordSemanticNode> TextNodes(
        WordSemanticDocument document,
        string partUri
    )
    {
        var nodes = document.Nodes.Where(node =>
            node.Kind == WordSemanticNodeKind.Text
            && string.Equals(node.SourcePartUri, partUri, StringComparison.Ordinal)
        ).ToArray();
        if (nodes.Length > _options.MaxSemanticTextNodesPerPart)
        {
            throw new WordPackageMergeLimitException(
                $"Part '{partUri}' exceeds the semantic text-node merge limit."
            );
        }
        var duplicate = nodes.GroupBy(node => node.SourcePath, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new WordPackageMergePreconditionException(
                $"Part '{partUri}' has duplicate semantic source path '{duplicate.Key}'."
            );
        }
        return nodes.ToDictionary(node => node.SourcePath, StringComparer.Ordinal);
    }

    private bool ReconstructsEntryExactly(
        OpcPackageSnapshot ancestorPackage,
        WordSemanticDocument ancestorDocument,
        OpcPackageEntry expectedEntry,
        IEnumerable<TextBranchChange> changes,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var commands = changes.Select(change => new WordTextReplacementCommand(
                change.AncestorNode.Id,
                change.NewText,
                change.AncestorNode.Text ?? string.Empty
            )).ToArray();
            if (commands.Length > _options.MaxSemanticTextChanges)
            {
                throw new WordPackageMergeLimitException(
                    "A text-only merge branch exceeds the semantic change limit."
                );
            }
            var plan = _textPlanner.PlanTextReplacements(
                ancestorPackage,
                ancestorDocument,
                commands,
                cancellationToken
            );
            var materialized = plan.CreateMutation(ancestorPackage)
                .Materialize(OpcSerializationMode.Preserve);
            var actual = materialized.Single(entry =>
                string.Equals(entry.Name, expectedEntry.Name, StringComparison.Ordinal)
            );
            return actual.Content.AsSpan().SequenceEqual(expectedEntry.Content.Span);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (WordPackageMergeLimitException)
        {
            throw;
        }
        catch (WordSemanticEditException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private void ApplyEntryWork(
        OpcPackageMutationBuilder mutation,
        OpcPackageSnapshot ancestorPackage,
        WordSemanticDocument ancestorDocument,
        EntryWork work,
        IReadOnlyDictionary<string, WordPackageMergeResolutionChoice> resolutions,
        CancellationToken cancellationToken
    )
    {
        if (work.Semantic is not null)
        {
            var commands = new List<WordTextReplacementCommand>();
            foreach (var value in work.Semantic.Values)
            {
                var selectedText = value.SelectedText(resolutions, work.Name);
                if (!string.Equals(
                        selectedText,
                        value.AncestorText,
                        StringComparison.Ordinal
                    ))
                {
                    commands.Add(new WordTextReplacementCommand(
                        value.AncestorNode.Id,
                        selectedText,
                        value.AncestorText
                    ));
                }
            }
            if (commands.Count == 0)
            {
                return;
            }
            var plan = _textPlanner.PlanTextReplacements(
                ancestorPackage,
                ancestorDocument,
                commands,
                cancellationToken
            );
            var content = plan.CreateMutation(ancestorPackage)
                .Materialize(OpcSerializationMode.Preserve)
                .Single(entry => string.Equals(
                    entry.Name,
                    work.Name,
                    StringComparison.Ordinal
                )).Content;
            mutation.ReplaceEntry(
                work.Name,
                content,
                work.Ancestor?.Sha256
            );
            return;
        }

        var selected = work.Conflict is null
            ? work.Selected
            : SelectEntry(
                work,
                resolutions[work.Conflict.ConflictId]
            );
        ApplySelectedEntry(mutation, work.Ancestor, selected, work.Name);
    }

    private static OpcPackageEntry? SelectEntry(
        EntryWork work,
        WordPackageMergeResolutionChoice choice
    ) => choice switch
    {
        WordPackageMergeResolutionChoice.UseAncestor => work.Ancestor,
        WordPackageMergeResolutionChoice.UseLeft => work.Left,
        WordPackageMergeResolutionChoice.UseRight => work.Right,
        _ => throw new WordPackageMergePreconditionException(
            $"Unsupported merge resolution '{choice}'."
        ),
    };

    private static void ApplySelectedEntry(
        OpcPackageMutationBuilder mutation,
        OpcPackageEntry? ancestor,
        OpcPackageEntry? selected,
        string entryName
    )
    {
        if (Equivalent(ancestor, selected))
        {
            return;
        }
        if (ancestor is null)
        {
            mutation.AddEntry(entryName, selected!.Content);
        }
        else if (selected is null)
        {
            mutation.DeleteEntry(entryName, ancestor.Sha256);
        }
        else
        {
            mutation.ReplaceEntry(entryName, selected.Content, ancestor.Sha256);
        }
    }

    private OpcPackageSnapshot Materialize(
        OpcPackageMutationBuilder mutation,
        CancellationToken cancellationToken
    )
    {
        using var stream = new MemoryStream();
        _serializer.Write(stream, mutation, OpcSerializationMode.Preserve);
        stream.Position = 0;
        return _reader.Read(stream, cancellationToken);
    }

    private WordPackageMergeConflict CreateSemanticConflict(
        string ancestorFingerprint,
        string leftFingerprint,
        string rightFingerprint,
        string entryName,
        string? partUri,
        SemanticValueWork value,
        bool infrastructure
    )
    {
        var id = ComputeConflictId(
            ancestorFingerprint,
            leftFingerprint,
            rightFingerprint,
            WordPackageMergeConflictKind.SemanticTextChangedDifferently,
            entryName,
            value.AncestorNode.SourcePath
        );
        value.ConflictId = id;
        return new WordPackageMergeConflict(
            id,
            WordPackageMergeConflictKind.SemanticTextChangedDifferently,
            entryName,
            partUri,
            value.AncestorNode.SourcePath,
            value.AncestorNode.Id,
            null,
            null,
            null,
            null,
            null,
            null,
            TextSnapshot(value.AncestorText),
            TextSnapshot(value.LeftText!),
            TextSnapshot(value.RightText!),
            infrastructure
        );
    }

    private WordPackageMergeTextSnapshot TextSnapshot(string value)
    {
        var previewLength = Math.Min(value.Length, _options.TextPreviewCharacters);
        return new WordPackageMergeTextSnapshot(
            value.Length,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
                .ToLowerInvariant(),
            value[..previewLength],
            previewLength != value.Length
        );
    }

    private static WordPackageMergeConflict CreateEntryConflict(
        string ancestorFingerprint,
        string leftFingerprint,
        string rightFingerprint,
        string entryName,
        OpcPackageEntry? ancestor,
        OpcPackageEntry? left,
        OpcPackageEntry? right,
        bool infrastructure
    )
    {
        var kind = ancestor is null
            ? WordPackageMergeConflictKind.AddedDifferently
            : left is null
                ? WordPackageMergeConflictKind.DeletedOnLeftModifiedOnRight
                : right is null
                    ? WordPackageMergeConflictKind.ModifiedOnLeftDeletedOnRight
                    : WordPackageMergeConflictKind.ModifiedDifferently;
        return new WordPackageMergeConflict(
            ComputeConflictId(
                ancestorFingerprint,
                leftFingerprint,
                rightFingerprint,
                kind,
                entryName,
                null
            ),
            kind,
            entryName,
            right?.PartUri ?? left?.PartUri ?? ancestor?.PartUri,
            null,
            null,
            ancestor?.Sha256,
            ancestor?.UncompressedLength,
            left?.Sha256,
            left?.UncompressedLength,
            right?.Sha256,
            right?.UncompressedLength,
            null,
            null,
            null,
            infrastructure
        );
    }

    private void AddConflict(
        ICollection<WordPackageMergeConflict> conflicts,
        WordPackageMergeConflict conflict
    )
    {
        if (conflicts.Count >= _options.MaxConflicts)
        {
            throw new WordPackageMergeLimitException(
                $"Merge contains more than {_options.MaxConflicts} conflicts."
            );
        }
        conflicts.Add(conflict);
    }

    private void CheckSemanticBudgets(int changes, long characters)
    {
        if (changes > _options.MaxSemanticTextChanges)
        {
            throw new WordPackageMergeLimitException(
                "Merge exceeds the semantic text-change limit."
            );
        }
        if (characters > _options.MaxSemanticReplacementCharacters)
        {
            throw new WordPackageMergeLimitException(
                "Merge exceeds the semantic replacement-character limit."
            );
        }
    }

    private static IReadOnlyDictionary<string, WordPackageMergeResolutionChoice>
        ValidateResolutions(
            IEnumerable<WordPackageMergeResolution>? resolutions,
            IReadOnlyList<WordPackageMergeConflict> conflicts
        )
    {
        var known = conflicts.Select(conflict => conflict.ConflictId)
            .ToHashSet(StringComparer.Ordinal);
        var result = new Dictionary<string, WordPackageMergeResolutionChoice>(
            StringComparer.Ordinal
        );
        foreach (var resolution in resolutions ?? [])
        {
            ArgumentNullException.ThrowIfNull(resolution);
            if (!known.Contains(resolution.ConflictId))
            {
                throw new WordPackageMergePreconditionException(
                    $"Resolution references unknown conflict '{resolution.ConflictId}'."
                );
            }
            if (!result.TryAdd(resolution.ConflictId, resolution.Choice))
            {
                throw new WordPackageMergePreconditionException(
                    $"Conflict '{resolution.ConflictId}' is resolved more than once."
                );
            }
        }
        return result;
    }

    private static IReadOnlyDictionary<string, OpcPackageEntry> UniqueEntries(
        OpcPackageSnapshot package,
        string role
    )
    {
        var duplicate = package.Entries.GroupBy(
            entry => entry.Name,
            StringComparer.Ordinal
        ).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new WordPackageMergePreconditionException(
                $"The {role} package contains duplicate entry '{duplicate.Key}'."
            );
        }
        return package.Entries.ToDictionary(entry => entry.Name, StringComparer.Ordinal);
    }

    private static void AssertProjection(
        OpcPackageSnapshot package,
        WordSemanticDocument document,
        string role
    )
    {
        if (!string.Equals(
                package.Fingerprint,
                document.PackageFingerprint,
                StringComparison.Ordinal
            ))
        {
            throw new WordPackageMergePreconditionException(
                $"The {role} semantic projection does not belong to its package snapshot."
            );
        }
    }

    private static bool SameContentType(
        OpcPackageSnapshot first,
        OpcPackageSnapshot second,
        string partUri
    ) => string.Equals(
        first.Parts.TryGetValue(partUri, out var firstPart)
            ? firstPart.ContentType
            : null,
        second.Parts.TryGetValue(partUri, out var secondPart)
            ? secondPart.ContentType
            : null,
        StringComparison.OrdinalIgnoreCase
    );

    private static bool SameKeys<T>(
        IReadOnlyDictionary<string, T> first,
        IReadOnlyDictionary<string, T> second
    ) => first.Keys.ToHashSet(StringComparer.Ordinal)
        .SetEquals(second.Keys);

    private static IReadOnlyDictionary<string, TextBranchChange> ExtractChanges(
        IReadOnlyDictionary<string, WordSemanticNode> ancestor,
        IReadOnlyDictionary<string, WordSemanticNode> branch
    ) => ancestor.Where(pair => !string.Equals(
            pair.Value.Text,
            branch[pair.Key].Text,
            StringComparison.Ordinal
        )).ToDictionary(
            pair => pair.Key,
            pair => new TextBranchChange(
                pair.Value,
                branch[pair.Key].Text ?? string.Empty
            ),
            StringComparer.Ordinal
        );

    private static bool Equivalent(OpcPackageEntry? first, OpcPackageEntry? second)
    {
        if (ReferenceEquals(first, second))
        {
            return true;
        }
        if (first is null || second is null)
        {
            return false;
        }
        return first.UncompressedLength == second.UncompressedLength
            && string.Equals(first.Sha256, second.Sha256, StringComparison.OrdinalIgnoreCase);
    }

    private static WordPackageMergeEntryDecision Decision(
        string entryName,
        string? partUri,
        WordPackageMergeEntryOutcome outcome,
        int semanticTextChanges,
        int conflicts,
        bool infrastructure
    ) => new(
        entryName,
        partUri,
        outcome,
        semanticTextChanges,
        conflicts,
        infrastructure
    );

    private static string ComputeConflictId(
        string ancestorFingerprint,
        string leftFingerprint,
        string rightFingerprint,
        WordPackageMergeConflictKind kind,
        string entryName,
        string? sourcePath
    )
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, "wordtoolkit-package-merge-conflict-v1");
        Append(hash, ancestorFingerprint);
        Append(hash, leftFingerprint);
        Append(hash, rightFingerprint);
        Append(hash, kind.ToString());
        Append(hash, entryName);
        Append(hash, sourcePath ?? string.Empty);
        return "wtmc_" + Base64Id(hash.GetHashAndReset(), 15);
    }

    private static string ComputeMergeId(
        string ancestorFingerprint,
        string leftFingerprint,
        string rightFingerprint,
        IReadOnlyList<WordPackageMergeEntryDecision> decisions,
        IReadOnlyList<WordPackageMergeConflict> conflicts,
        IReadOnlyDictionary<string, WordPackageMergeResolutionChoice> resolutions,
        string? patchId
    )
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, "wordtoolkit-package-three-way-merge-v1");
        Append(hash, ancestorFingerprint);
        Append(hash, leftFingerprint);
        Append(hash, rightFingerprint);
        foreach (var decision in decisions)
        {
            Append(hash, decision.EntryName);
            Append(hash, decision.Outcome.ToString());
            Append(hash, decision.SemanticTextChangeCount.ToString(
                System.Globalization.CultureInfo.InvariantCulture
            ));
            Append(hash, decision.ConflictCount.ToString(
                System.Globalization.CultureInfo.InvariantCulture
            ));
        }
        foreach (var conflict in conflicts)
        {
            Append(hash, conflict.ConflictId);
            Append(hash, resolutions.TryGetValue(conflict.ConflictId, out var choice)
                ? choice.ToString()
                : string.Empty);
        }
        Append(hash, patchId ?? string.Empty);
        return "wtmerge_" + Base64Id(hash.GetHashAndReset(), 18);
    }

    private static void Append(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }

    private static string Base64Id(byte[] digest, int length) => Convert.ToBase64String(
        digest.AsSpan(0, length)
    ).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed record TextBranchChange(
        WordSemanticNode AncestorNode,
        string NewText
    );

    private sealed class SemanticValueWork
    {
        public SemanticValueWork(
            WordSemanticNode ancestorNode,
            string ancestorText,
            string? leftText,
            string? rightText,
            bool isConflict
        )
        {
            AncestorNode = ancestorNode;
            AncestorText = ancestorText;
            LeftText = leftText;
            RightText = rightText;
            IsConflict = isConflict;
        }

        public WordSemanticNode AncestorNode { get; }

        public string AncestorText { get; }

        public string? LeftText { get; }

        public string? RightText { get; }

        public bool IsConflict { get; }

        public bool HasChange => LeftText is not null || RightText is not null;

        public string? ConflictId { get; set; }

        public string SelectedText(
            IReadOnlyDictionary<string, WordPackageMergeResolutionChoice> resolutions,
            string entryName
        )
        {
            if (!IsConflict)
            {
                return LeftText ?? RightText ?? AncestorText;
            }
            if (ConflictId is null || !resolutions.TryGetValue(ConflictId, out var choice))
            {
                throw new WordPackageMergePreconditionException(
                    $"Semantic conflict in '{entryName}' has no resolution."
                );
            }
            return choice switch
            {
                WordPackageMergeResolutionChoice.UseAncestor => AncestorText,
                WordPackageMergeResolutionChoice.UseLeft => LeftText!,
                WordPackageMergeResolutionChoice.UseRight => RightText!,
                _ => throw new WordPackageMergePreconditionException(
                    $"Unsupported merge resolution '{choice}'."
                ),
            };
        }
    }

    private sealed record SemanticEntryWork(
        string PartUri,
        IReadOnlyList<SemanticValueWork> Values
    );

    private sealed record EntryWork(
        string Name,
        OpcPackageEntry? Ancestor,
        OpcPackageEntry? Left,
        OpcPackageEntry? Right,
        OpcPackageEntry? Selected,
        WordPackageMergeEntryOutcome Outcome,
        SemanticEntryWork? Semantic,
        WordPackageMergeConflict? Conflict
    )
    {
        public static EntryWork Direct(
            string name,
            OpcPackageEntry? ancestor,
            OpcPackageEntry? left,
            OpcPackageEntry? right,
            OpcPackageEntry? selected,
            WordPackageMergeEntryOutcome outcome
        ) => new(name, ancestor, left, right, selected, outcome, null, null);

        public static EntryWork CreateSemantic(
            string name,
            OpcPackageEntry ancestor,
            OpcPackageEntry left,
            OpcPackageEntry right,
            SemanticEntryWork semantic
        ) => new(
            name,
            ancestor,
            left,
            right,
            null,
            WordPackageMergeEntryOutcome.SemanticTextMerge,
            semantic,
            null
        );

        public static EntryWork CreateConflict(
            string name,
            OpcPackageEntry? ancestor,
            OpcPackageEntry? left,
            OpcPackageEntry? right,
            WordPackageMergeConflict conflict
        ) => new(
            name,
            ancestor,
            left,
            right,
            null,
            WordPackageMergeEntryOutcome.Conflict,
            null,
            conflict
        );
    }
}

public class WordPackageMergeException : InvalidOperationException
{
    public WordPackageMergeException(string message)
        : base(message)
    {
    }
}

public sealed class WordPackageMergePreconditionException : WordPackageMergeException
{
    public WordPackageMergePreconditionException(string message)
        : base(message)
    {
    }
}

public sealed class WordPackageMergeLimitException : WordPackageMergeException
{
    public WordPackageMergeLimitException(string message)
        : base(message)
    {
    }
}
