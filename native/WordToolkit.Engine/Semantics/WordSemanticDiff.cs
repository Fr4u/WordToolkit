using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using WordToolkit.Engine.Packaging;

namespace WordToolkit.Engine.Semantics;

public enum WordPackageEntryDifferenceKind
{
    Added,
    Removed,
    Modified,
}

public enum WordSemanticDifferenceKind
{
    Added,
    Removed,
    Moved,
    TextChanged,
    PropertiesChanged,
    StructureChanged,
    UnmodeledMarkupChanged,
}

public enum WordSemanticMatchBasis
{
    DocumentRole,
    ExactNodeId,
    DurableIdentity,
    ExactSubtree,
    ContextualSimilarity,
}

public enum WordSemanticMatchConfidence
{
    Exact,
    High,
    Medium,
    Low,
}

public sealed record WordSemanticDiffOptions
{
    public static WordSemanticDiffOptions Default { get; } = new();

    public int MaxNodesPerDocument { get; init; } = 1_000_000;

    public int MaxChanges { get; init; } = 200_000;

    public int MaxDiagnostics { get; init; } = 1_000;

    public long MaxAlignmentCells { get; init; } = 4_000_000;

    public int MaxSimilarityTextCharacters { get; init; } = 8_192;

    public long MaxTotalTextCharactersProcessedPerDocument { get; init; } =
        256L * 1024 * 1024;

    public long MaxTotalTextCharactersCapturedPerDocument { get; init; } =
        64L * 1024 * 1024;

    public int GreedyAlignmentWindow { get; init; } = 32;

    public double MinimumContextSimilarity { get; init; } = 0.56;

    public bool CompareText { get; init; } = true;

    public bool CompareProperties { get; init; } = true;

    public bool CompareWhitespace { get; init; } = true;

    public bool CaseSensitive { get; init; } = true;

    public bool DetectMoves { get; init; } = true;

    internal void Validate()
    {
        if (MaxNodesPerDocument <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxNodesPerDocument));
        }
        if (MaxChanges <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxChanges));
        }
        if (MaxDiagnostics <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxDiagnostics));
        }
        if (MaxAlignmentCells <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxAlignmentCells));
        }
        if (MaxSimilarityTextCharacters is < 64 or > 1_000_000)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxSimilarityTextCharacters));
        }
        if (MaxTotalTextCharactersProcessedPerDocument <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxTotalTextCharactersProcessedPerDocument)
            );
        }
        if (MaxTotalTextCharactersCapturedPerDocument <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxTotalTextCharactersCapturedPerDocument)
            );
        }
        if (GreedyAlignmentWindow is < 1 or > 1_024)
        {
            throw new ArgumentOutOfRangeException(nameof(GreedyAlignmentWindow));
        }
        if (
            double.IsNaN(MinimumContextSimilarity)
            || MinimumContextSimilarity is < 0.25 or > 0.99
        )
        {
            throw new ArgumentOutOfRangeException(nameof(MinimumContextSimilarity));
        }
    }
}

public sealed record WordPackageEntryDifference(
    WordPackageEntryDifferenceKind Kind,
    string EntryName,
    string? PartUri,
    string? ContentType,
    long? BeforeBytes,
    long? AfterBytes,
    string? BeforeSha256,
    string? AfterSha256,
    bool IsInfrastructure,
    bool IsProjectedSemanticPart
);

public sealed record WordSemanticNodeLocation(
    SemanticNodeId NodeId,
    WordSemanticNodeKind Kind,
    SemanticNodeId? ParentId,
    int SourceOrder,
    int SiblingIndex,
    string SourcePartUri,
    string SourcePath,
    int SourceElementOrdinal,
    string ScopeFamily
);

public sealed record WordSemanticTextSnapshot(
    int CharacterCount,
    string Sha256,
    string CapturedText,
    bool CapturedTextTruncated
);

public sealed record WordSemanticTextDifference(
    WordSemanticTextSnapshot? Before,
    WordSemanticTextSnapshot? After
);

public sealed record WordSemanticPropertyDifference(
    string Name,
    string? BeforeValue,
    string? AfterValue
);

public sealed record WordSemanticDifference(
    string DifferenceId,
    IReadOnlyList<WordSemanticDifferenceKind> Kinds,
    WordSemanticNodeKind NodeKind,
    WordSemanticMatchBasis? MatchBasis,
    WordSemanticMatchConfidence? MatchConfidence,
    double? MatchScore,
    WordSemanticNodeLocation? Before,
    WordSemanticNodeLocation? After,
    WordSemanticTextDifference? Text,
    IReadOnlyList<WordSemanticPropertyDifference> Properties,
    string? BeforeSubtreeFingerprint,
    string? AfterSubtreeFingerprint
);

public sealed record WordSemanticDiffDiagnostic(
    string Code,
    string Message,
    WordSemanticNodeKind? NodeKind = null,
    string? ScopeFamily = null,
    int? BeforeCount = null,
    int? AfterCount = null
);

public sealed class WordSemanticDiffResult
{
    internal WordSemanticDiffResult(
        string diffId,
        string beforePackageFingerprint,
        string afterPackageFingerprint,
        IReadOnlyList<WordPackageEntryDifference> entryDifferences,
        IReadOnlyList<WordSemanticDifference> semanticDifferences,
        IReadOnlyList<WordSemanticDiffDiagnostic> diagnostics,
        int beforeNodeCount,
        int afterNodeCount,
        MatchStatistics matchStatistics,
        int ambiguousIdentityGroupCount,
        int ambiguousContextualMatchCount,
        int alignmentFallbackCount,
        long alignmentCellsEvaluated,
        int unclassifiedProjectedEntryCount
    )
    {
        DiffId = diffId;
        BeforePackageFingerprint = beforePackageFingerprint;
        AfterPackageFingerprint = afterPackageFingerprint;
        EntryDifferences = new ReadOnlyCollection<WordPackageEntryDifference>(
            entryDifferences.ToArray()
        );
        SemanticDifferences = new ReadOnlyCollection<WordSemanticDifference>(
            semanticDifferences.ToArray()
        );
        Diagnostics = new ReadOnlyCollection<WordSemanticDiffDiagnostic>(
            diagnostics.ToArray()
        );
        BeforeNodeCount = beforeNodeCount;
        AfterNodeCount = afterNodeCount;
        MatchedNodeCount = matchStatistics.Total;
        ExactNodeIdMatchCount = matchStatistics.ExactNodeId;
        DurableIdentityMatchCount = matchStatistics.DurableIdentity;
        ExactSubtreeMatchCount = matchStatistics.ExactSubtree;
        ContextualMatchCount = matchStatistics.Contextual;
        RoleMatchCount = matchStatistics.DocumentRole;
        AmbiguousIdentityGroupCount = ambiguousIdentityGroupCount;
        AmbiguousContextualMatchCount = ambiguousContextualMatchCount;
        AlignmentFallbackCount = alignmentFallbackCount;
        AlignmentCellsEvaluated = alignmentCellsEvaluated;
        UnclassifiedProjectedEntryCount = unclassifiedProjectedEntryCount;
    }

    public string DiffId { get; }

    public string BeforePackageFingerprint { get; }

    public string AfterPackageFingerprint { get; }

    public IReadOnlyList<WordPackageEntryDifference> EntryDifferences { get; }

    public IReadOnlyList<WordSemanticDifference> SemanticDifferences { get; }

    public IReadOnlyList<WordSemanticDiffDiagnostic> Diagnostics { get; }

    public int BeforeNodeCount { get; }

    public int AfterNodeCount { get; }

    public int MatchedNodeCount { get; }

    public int ExactNodeIdMatchCount { get; }

    public int DurableIdentityMatchCount { get; }

    public int ExactSubtreeMatchCount { get; }

    public int ContextualMatchCount { get; }

    public int RoleMatchCount { get; }

    public int AmbiguousIdentityGroupCount { get; }

    public int AmbiguousContextualMatchCount { get; }

    public int AlignmentFallbackCount { get; }

    public long AlignmentCellsEvaluated { get; }

    public int UnclassifiedProjectedEntryCount { get; }

    public bool PackageEquivalent => EntryDifferences.Count == 0;

    public bool SemanticallyEquivalent => SemanticDifferences.Count == 0;

    public bool MatchingComplete => AlignmentFallbackCount == 0
        && AmbiguousIdentityGroupCount == 0
        && AmbiguousContextualMatchCount == 0;

    public int AddedNodeCount => Count(WordSemanticDifferenceKind.Added);

    public int RemovedNodeCount => Count(WordSemanticDifferenceKind.Removed);

    public int MovedNodeCount => Count(WordSemanticDifferenceKind.Moved);

    public int TextChangedNodeCount => Count(WordSemanticDifferenceKind.TextChanged);

    public int PropertiesChangedNodeCount => Count(
        WordSemanticDifferenceKind.PropertiesChanged
    );

    public int StructureChangedNodeCount => Count(
        WordSemanticDifferenceKind.StructureChanged
    );

    public int UnmodeledMarkupChangedNodeCount => Count(
        WordSemanticDifferenceKind.UnmodeledMarkupChanged
    );

    private int Count(WordSemanticDifferenceKind kind) => SemanticDifferences.Count(
        difference => difference.Kinds.Contains(kind)
    );

    internal sealed record MatchStatistics(
        int Total,
        int DocumentRole,
        int ExactNodeId,
        int DurableIdentity,
        int ExactSubtree,
        int Contextual
    );
}

public sealed class WordSemanticDiffEngine
{
    private static readonly HashSet<WordSemanticNodeKind> StoryRootKinds =
    [
        WordSemanticNodeKind.Header,
        WordSemanticNodeKind.Footer,
        WordSemanticNodeKind.Footnotes,
        WordSemanticNodeKind.Endnotes,
        WordSemanticNodeKind.Comments,
        WordSemanticNodeKind.GlossaryDocument,
    ];

    private static readonly HashSet<WordSemanticNodeKind> StructuralReportKinds =
    [
        WordSemanticNodeKind.Paragraph,
        WordSemanticNodeKind.Table,
        WordSemanticNodeKind.TableRow,
        WordSemanticNodeKind.TableCell,
        WordSemanticNodeKind.Hyperlink,
        WordSemanticNodeKind.Field,
        WordSemanticNodeKind.Equation,
        WordSemanticNodeKind.ContentControl,
        WordSemanticNodeKind.Bookmark,
        WordSemanticNodeKind.BookmarkEnd,
        WordSemanticNodeKind.CommentAnchor,
        WordSemanticNodeKind.Revision,
        WordSemanticNodeKind.Drawing,
        WordSemanticNodeKind.AlternateContent,
        WordSemanticNodeKind.ExtensionIsland,
        WordSemanticNodeKind.Header,
        WordSemanticNodeKind.Footer,
        WordSemanticNodeKind.Footnote,
        WordSemanticNodeKind.Endnote,
        WordSemanticNodeKind.Comment,
        WordSemanticNodeKind.GlossaryEntry,
        WordSemanticNodeKind.TextBox,
        WordSemanticNodeKind.HeaderReference,
        WordSemanticNodeKind.FooterReference,
        WordSemanticNodeKind.FootnoteReference,
        WordSemanticNodeKind.EndnoteReference,
        WordSemanticNodeKind.Section,
    ];

    private static readonly HashSet<WordSemanticNodeKind> PropertyReportKinds =
    [
        .. StructuralReportKinds,
        WordSemanticNodeKind.Run,
    ];

    private static readonly HashSet<WordSemanticNodeKind> TextComparisonKinds =
    [
        WordSemanticNodeKind.Paragraph,
        WordSemanticNodeKind.Run,
        WordSemanticNodeKind.Text,
        WordSemanticNodeKind.Hyperlink,
        WordSemanticNodeKind.Field,
        WordSemanticNodeKind.Equation,
        WordSemanticNodeKind.ContentControl,
        WordSemanticNodeKind.Footnote,
        WordSemanticNodeKind.Endnote,
        WordSemanticNodeKind.Comment,
        WordSemanticNodeKind.GlossaryEntry,
        WordSemanticNodeKind.TextBox,
    ];

    private static readonly HashSet<WordSemanticNodeKind> TextReportKinds =
    [
        WordSemanticNodeKind.Paragraph,
        WordSemanticNodeKind.Hyperlink,
        WordSemanticNodeKind.Field,
        WordSemanticNodeKind.Equation,
        WordSemanticNodeKind.ContentControl,
        WordSemanticNodeKind.Footnote,
        WordSemanticNodeKind.Endnote,
        WordSemanticNodeKind.Comment,
        WordSemanticNodeKind.GlossaryEntry,
        WordSemanticNodeKind.TextBox,
    ];

    private readonly WordSemanticDiffOptions _options;

    public WordSemanticDiffEngine(WordSemanticDiffOptions? options = null)
    {
        _options = options ?? WordSemanticDiffOptions.Default;
        _options.Validate();
    }

    public WordSemanticDiffResult Compare(
        OpcPackageSnapshot beforePackage,
        WordSemanticDocument beforeDocument,
        OpcPackageSnapshot afterPackage,
        WordSemanticDocument afterDocument,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(beforePackage);
        ArgumentNullException.ThrowIfNull(beforeDocument);
        ArgumentNullException.ThrowIfNull(afterPackage);
        ArgumentNullException.ThrowIfNull(afterDocument);
        cancellationToken.ThrowIfCancellationRequested();
        VerifyProjection(beforePackage, beforeDocument, "before");
        VerifyProjection(afterPackage, afterDocument, "after");
        if (
            beforeDocument.NodeCount > _options.MaxNodesPerDocument
            || afterDocument.NodeCount > _options.MaxNodesPerDocument
        )
        {
            throw new WordSemanticDiffLimitException(
                $"Semantic diff accepts at most {_options.MaxNodesPerDocument} nodes per document."
            );
        }

        var beforeInfos = BuildNodeInfos(beforeDocument, cancellationToken);
        var afterInfos = BuildNodeInfos(afterDocument, cancellationToken);
        var state = new MatchingState(
            beforeInfos,
            afterInfos,
            _options,
            cancellationToken
        );
        MatchDocumentRoles(state, beforeDocument, afterDocument);
        MatchExactNodeIds(state);
        MatchUniqueIdentities(state, durableOnly: true);
        MatchUniqueIdentities(state, durableOnly: false);
        AlignMatchedSubtrees(state);
        var moved = _options.DetectMoves
            ? DetectTopLevelMoves(state)
            : new HashSet<SemanticNodeId>();
        var entryDifferences = CompareEntries(
            beforePackage,
            beforeDocument,
            afterPackage,
            afterDocument
        );
        var semanticDifferences = BuildSemanticDifferences(
            state,
            moved,
            beforePackage.Fingerprint,
            afterPackage.Fingerprint,
            cancellationToken
        );
        var changedSemanticParts = semanticDifferences
            .SelectMany(difference => new[]
            {
                difference.Before?.SourcePartUri,
                difference.After?.SourcePartUri,
            })
            .Where(uri => uri is not null)
            .Select(uri => uri!)
            .ToHashSet(StringComparer.Ordinal);
        var unclassifiedProjectedEntryCount = entryDifferences.Count(entry =>
            entry.IsProjectedSemanticPart
            && entry.PartUri is { } partUri
            && !changedSemanticParts.Contains(partUri)
        );
        var diffId = CreateDiffId(
            beforePackage.Fingerprint,
            afterPackage.Fingerprint,
            _options
        );
        return new WordSemanticDiffResult(
            diffId,
            beforePackage.Fingerprint,
            afterPackage.Fingerprint,
            entryDifferences,
            semanticDifferences,
            state.Diagnostics,
            beforeDocument.NodeCount,
            afterDocument.NodeCount,
            state.Statistics(),
            state.AmbiguousIdentityGroupCount,
            state.AmbiguousContextualMatchCount,
            state.AlignmentFallbackCount,
            state.AlignmentCellsEvaluated,
            unclassifiedProjectedEntryCount
        );
    }

    private static void VerifyProjection(
        OpcPackageSnapshot package,
        WordSemanticDocument document,
        string side
    )
    {
        if (!string.Equals(
                package.Fingerprint,
                document.PackageFingerprint,
                StringComparison.Ordinal
            ))
        {
            throw new WordSemanticDiffPreconditionException(
                $"The {side} semantic projection does not belong to the supplied package snapshot."
            );
        }
    }

    private Dictionary<SemanticNodeId, NodeInfo> BuildNodeInfos(
        WordSemanticDocument document,
        CancellationToken cancellationToken
    )
    {
        var infos = new Dictionary<SemanticNodeId, NodeInfo>();
        var textBudget = new TextBudget(_options);
        var visited = 0;

        string Visit(
            WordSemanticNode node,
            NodeInfo? parent,
            int siblingIndex,
            int depth,
            string scopeFamily
        )
        {
            if ((visited++ & 0xff) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
            var effectiveScope = StoryRootKinds.Contains(node.Kind)
                ? node.Kind.ToString()
                : node.Kind == WordSemanticNodeKind.Body
                    ? "Main"
                    : scopeFamily;
            var text = TextComparisonKinds.Contains(node.Kind)
                ? BuildTextSnapshot(node, textBudget)
                : null;
            var info = new NodeInfo(
                node,
                parent,
                siblingIndex,
                depth,
                effectiveScope,
                text,
                BuildShingles(text?.ComparisonSample),
                ChildKindCounts(node)
            );
            infos.Add(node.Id, info);
            var childFingerprints = new string[node.Children.Count];
            for (var index = 0; index < node.Children.Count; index++)
            {
                childFingerprints[index] = Visit(
                    node.Children[index],
                    info,
                    index,
                    depth + 1,
                    effectiveScope
                );
            }
            info.ModeledSubtreeFingerprint = CreateModeledFingerprint(
                node,
                childFingerprints
            );
            return info.ModeledSubtreeFingerprint;
        }

        Visit(document.Root, null, 0, 0, "Main");
        return infos;
    }

    private NodeTextState BuildTextSnapshot(
        WordSemanticNode node,
        TextBudget textBudget
    )
    {
        var digester = new TextDigester(_options, textBudget);
        var firstParagraph = true;
        foreach (var current in node.DescendantsAndSelf())
        {
            if (current.Kind == WordSemanticNodeKind.Paragraph)
            {
                if (!firstParagraph)
                {
                    digester.Append("\n");
                }
                firstParagraph = false;
            }
            var value = current.Kind switch
            {
                WordSemanticNodeKind.Text or WordSemanticNodeKind.Field => current.Text,
                WordSemanticNodeKind.Tab => "\t",
                WordSemanticNodeKind.Break => "\n",
                _ => null,
            };
            if (!string.IsNullOrEmpty(value))
            {
                digester.Append(value);
            }
        }
        return digester.Finish();
    }

    private string CreateModeledFingerprint(
        WordSemanticNode node,
        IReadOnlyList<string> childFingerprints
    )
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendHashField(hash, node.Kind.ToString());
        if (_options.CompareText)
        {
            AppendHashField(
                hash,
                NormalizeForComparison(node.Text ?? string.Empty)
            );
        }
        if (_options.CompareProperties)
        {
            foreach (var property in node.Properties.OrderBy(
                pair => pair.Key,
                StringComparer.Ordinal
            ))
            {
                AppendHashField(hash, property.Key);
                AppendHashField(hash, property.Value);
            }
        }
        foreach (var childFingerprint in childFingerprints)
        {
            AppendHashField(hash, childFingerprint);
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private string NormalizeForComparison(string value)
    {
        if (_options.CompareWhitespace && _options.CaseSensitive)
        {
            return value;
        }
        var builder = new StringBuilder(value.Length);
        var previousWhitespace = false;
        foreach (var raw in value)
        {
            var character = _options.CaseSensitive
                ? raw
                : char.ToUpperInvariant(raw);
            if (!_options.CompareWhitespace && char.IsWhiteSpace(character))
            {
                if (!previousWhitespace)
                {
                    builder.Append(' ');
                }
                previousWhitespace = true;
                continue;
            }
            previousWhitespace = false;
            builder.Append(character);
        }
        return _options.CompareWhitespace
            ? builder.ToString()
            : builder.ToString().Trim();
    }

    private static IReadOnlyDictionary<WordSemanticNodeKind, int> ChildKindCounts(
        WordSemanticNode node
    ) => node.Children
        .GroupBy(child => child.Kind)
        .ToDictionary(group => group.Key, group => group.Count());

    private static IReadOnlySet<int>? BuildShingles(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }
        if (value.Length < 3)
        {
            return value.Select(character => (int)character).ToHashSet();
        }
        var result = new HashSet<int>();
        for (var index = 0; index <= value.Length - 3; index++)
        {
            result.Add(unchecked(
                ((value[index] * 397) ^ value[index + 1]) * 397
                    ^ value[index + 2]
            ));
        }
        return result;
    }

    private static IReadOnlyList<WordPackageEntryDifference> CompareEntries(
        OpcPackageSnapshot beforePackage,
        WordSemanticDocument beforeDocument,
        OpcPackageSnapshot afterPackage,
        WordSemanticDocument afterDocument
    )
    {
        var beforeEntries = beforePackage.Entries.ToDictionary(
            entry => entry.Name,
            StringComparer.Ordinal
        );
        var afterEntries = afterPackage.Entries.ToDictionary(
            entry => entry.Name,
            StringComparer.Ordinal
        );
        var projected = beforeDocument.ProjectedPartUris
            .Concat(afterDocument.ProjectedPartUris)
            .ToHashSet(StringComparer.Ordinal);
        var names = beforeEntries.Keys.Concat(afterEntries.Keys)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);
        var result = new List<WordPackageEntryDifference>();
        foreach (var name in names)
        {
            beforeEntries.TryGetValue(name, out var before);
            afterEntries.TryGetValue(name, out var after);
            if (
                before is not null
                && after is not null
                && before.UncompressedLength == after.UncompressedLength
                && string.Equals(
                    before.Sha256,
                    after.Sha256,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                continue;
            }
            var kind = before is null
                ? WordPackageEntryDifferenceKind.Added
                : after is null
                    ? WordPackageEntryDifferenceKind.Removed
                    : WordPackageEntryDifferenceKind.Modified;
            var partUri = after?.PartUri ?? before?.PartUri;
            string? contentType = null;
            if (partUri is not null)
            {
                contentType = afterPackage.Parts.TryGetValue(partUri, out var afterPart)
                    ? afterPart.ContentType
                    : beforePackage.Parts.TryGetValue(partUri, out var beforePart)
                        ? beforePart.ContentType
                        : null;
            }
            result.Add(new WordPackageEntryDifference(
                kind,
                name,
                partUri,
                contentType,
                before?.UncompressedLength,
                after?.UncompressedLength,
                before?.Sha256,
                after?.Sha256,
                before?.IsInfrastructure == true || after?.IsInfrastructure == true,
                partUri is not null && projected.Contains(partUri)
            ));
        }
        return result;
    }

    private static void MatchDocumentRoles(
        MatchingState state,
        WordSemanticDocument beforeDocument,
        WordSemanticDocument afterDocument
    )
    {
        if (beforeDocument.Root.Kind == afterDocument.Root.Kind)
        {
            state.Pair(
                state.Before[beforeDocument.Root.Id],
                state.After[afterDocument.Root.Id],
                WordSemanticMatchBasis.DocumentRole,
                1
            );
        }
        var roleKinds = StoryRootKinds.Append(WordSemanticNodeKind.Body).ToHashSet();
        var beforeRoles = state.Before.Values
            .Where(info => roleKinds.Contains(info.Node.Kind))
            .GroupBy(RoleKey)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var afterRoles = state.After.Values
            .Where(info => roleKinds.Contains(info.Node.Kind))
            .GroupBy(RoleKey)
            .ToDictionary(group => group.Key, group => group.ToArray());
        foreach (var key in beforeRoles.Keys.Intersect(afterRoles.Keys))
        {
            var before = beforeRoles[key];
            var after = afterRoles[key];
            if (before.Length == 1 && after.Length == 1)
            {
                state.Pair(
                    before[0],
                    after[0],
                    WordSemanticMatchBasis.DocumentRole,
                    1
                );
            }
        }
    }

    private static string RoleKey(NodeInfo info) => string.Join(
        '\u001f',
        info.Node.Kind,
        info.Node.SourcePartUri
    );

    private static void MatchExactNodeIds(MatchingState state)
    {
        foreach (var before in state.Before.Values.OrderBy(info => info.Node.SourceOrder))
        {
            state.CancellationToken.ThrowIfCancellationRequested();
            if (
                state.IsBeforeMatched(before.Node.Id)
                || !state.After.TryGetValue(before.Node.Id, out var after)
                || state.IsAfterMatched(after.Node.Id)
                || before.Node.Kind != after.Node.Kind
            )
            {
                continue;
            }
            state.Pair(
                before,
                after,
                WordSemanticMatchBasis.ExactNodeId,
                1
            );
        }
    }

    private static void MatchUniqueIdentities(
        MatchingState state,
        bool durableOnly
    )
    {
        var beforeGroups = state.Before.Values
            .Where(info => !state.IsBeforeMatched(info.Node.Id))
            .Where(info =>
                !durableOnly
                || info.Node.IdentityKind == WordSemanticIdentityKind.DurableAnchor
            )
            .Where(info => durableOnly || StructuralReportKinds.Contains(info.Node.Kind))
            .GroupBy(info => MatchIdentityKey(info, durableOnly))
            .ToDictionary(group => group.Key, group => group.ToArray());
        var afterGroups = state.After.Values
            .Where(info => !state.IsAfterMatched(info.Node.Id))
            .Where(info =>
                !durableOnly
                || info.Node.IdentityKind == WordSemanticIdentityKind.DurableAnchor
            )
            .Where(info => durableOnly || StructuralReportKinds.Contains(info.Node.Kind))
            .GroupBy(info => MatchIdentityKey(info, durableOnly))
            .ToDictionary(group => group.Key, group => group.ToArray());
        foreach (
            var key in beforeGroups.Keys.Intersect(afterGroups.Keys)
                .Order(StringComparer.Ordinal)
        )
        {
            state.CancellationToken.ThrowIfCancellationRequested();
            var before = beforeGroups[key];
            var after = afterGroups[key];
            if (before.Length == 1 && after.Length == 1)
            {
                state.Pair(
                    before[0],
                    after[0],
                    durableOnly
                        ? WordSemanticMatchBasis.DurableIdentity
                        : WordSemanticMatchBasis.ExactSubtree,
                    1
                );
                continue;
            }
            if (durableOnly)
            {
                state.AmbiguousIdentityGroupCount++;
                state.AddDiagnostic(new WordSemanticDiffDiagnostic(
                    "ambiguous_durable_identity",
                    "A durable semantic identity is duplicated and was not guessed into a match.",
                    before[0].Node.Kind,
                    before[0].ScopeFamily,
                    before.Length,
                    after.Length
                ));
            }
        }
    }

    private static string MatchIdentityKey(NodeInfo info, bool durableOnly) => string.Join(
        '\u001f',
        info.ScopeFamily,
        info.Node.Kind,
        durableOnly
            ? info.Node.IdentityFingerprint
            : info.Node.SubtreeFingerprint
    );

    private void AlignMatchedSubtrees(MatchingState state)
    {
        var queue = new Queue<SemanticNodeId>(
            state.BeforeToAfter.Keys.OrderBy(id => state.Before[id].Depth)
                .ThenBy(id => state.Before[id].Node.SourceOrder)
        );
        var queued = queue.ToHashSet();
        var processed = new HashSet<SemanticNodeId>();
        while (queue.TryDequeue(out var beforeId))
        {
            queued.Remove(beforeId);
            if (!processed.Add(beforeId))
            {
                continue;
            }
            state.CancellationToken.ThrowIfCancellationRequested();
            if (!state.BeforeToAfter.TryGetValue(beforeId, out var afterId))
            {
                continue;
            }
            foreach (var pair in AlignChildren(
                state,
                state.Before[beforeId],
                state.After[afterId]
            ))
            {
                if (processed.Contains(pair.Before.Node.Id) || !queued.Add(pair.Before.Node.Id))
                {
                    continue;
                }
                queue.Enqueue(pair.Before.Node.Id);
            }
        }
    }

    private IReadOnlyList<NodePair> AlignChildren(
        MatchingState state,
        NodeInfo beforeParent,
        NodeInfo afterParent
    )
    {
        var beforeChildren = beforeParent.Node.Children
            .Select(child => state.Before[child.Id])
            .ToArray();
        var afterChildren = afterParent.Node.Children
            .Select(child => state.After[child.Id])
            .ToArray();
        if (beforeChildren.Length == 0 || afterChildren.Length == 0)
        {
            return Array.Empty<NodePair>();
        }
        var existing = new List<IndexedNodePair>();
        foreach (var before in beforeChildren)
        {
            if (
                !state.BeforeToAfter.TryGetValue(before.Node.Id, out var afterId)
                || !state.After.TryGetValue(afterId, out var after)
                || after.Parent?.Node.Id != afterParent.Node.Id
            )
            {
                continue;
            }
            existing.Add(new IndexedNodePair(
                before.SiblingIndex,
                after.SiblingIndex,
                before,
                after
            ));
        }
        var anchors = LongestIncreasingPairs(existing);
        var boundaries = new List<IndexedNodePair>(anchors.Count + 2)
        {
            new(-1, -1, beforeParent, afterParent),
        };
        boundaries.AddRange(anchors);
        boundaries.Add(new IndexedNodePair(
            beforeChildren.Length,
            afterChildren.Length,
            beforeParent,
            afterParent
        ));
        var added = new List<NodePair>();
        for (var boundaryIndex = 0; boundaryIndex < boundaries.Count - 1; boundaryIndex++)
        {
            var left = boundaries[boundaryIndex];
            var right = boundaries[boundaryIndex + 1];
            var beforeGap = beforeChildren
                .Skip(left.BeforeIndex + 1)
                .Take(right.BeforeIndex - left.BeforeIndex - 1)
                .Where(info => !state.IsBeforeMatched(info.Node.Id))
                .ToArray();
            var afterGap = afterChildren
                .Skip(left.AfterIndex + 1)
                .Take(right.AfterIndex - left.AfterIndex - 1)
                .Where(info => !state.IsAfterMatched(info.Node.Id))
                .ToArray();
            if (beforeGap.Length == 0 || afterGap.Length == 0)
            {
                continue;
            }
            var candidates = AlignGap(
                state,
                beforeGap,
                afterGap,
                beforeParent.ScopeFamily
            );
            foreach (var candidate in candidates)
            {
                if (state.Pair(
                        candidate.Before,
                        candidate.After,
                        WordSemanticMatchBasis.ContextualSimilarity,
                        candidate.Score
                    ))
                {
                    added.Add(new NodePair(candidate.Before, candidate.After));
                }
            }
        }
        return added;
    }

    private IReadOnlyList<MatchCandidate> AlignGap(
        MatchingState state,
        IReadOnlyList<NodeInfo> before,
        IReadOnlyList<NodeInfo> after,
        string scopeFamily
    )
    {
        var rowLength = checked(after.Count + 1);
        var cells = checked((long)(before.Count + 1) * rowLength);
        if (!state.TryReserveAlignmentCells(cells))
        {
            state.AlignmentFallbackCount++;
            state.AddDiagnostic(new WordSemanticDiffDiagnostic(
                "alignment_budget_fallback",
                "A large unmatched sibling region used bounded greedy alignment; unmatched nodes remain explicit.",
                ScopeFamily: scopeFamily,
                BeforeCount: before.Count,
                AfterCount: after.Count
            ));
            return RemoveAmbiguousContextualMatches(
                state,
                GreedyAlign(before, after),
                before,
                after,
                scopeFamily,
                boundedWindow: true
            );
        }

        var directions = new byte[checked((int)cells)];
        var previous = new float[rowLength];
        var current = new float[rowLength];
        for (var column = 1; column < rowLength; column++)
        {
            directions[column] = 2;
        }
        for (var row = 1; row <= before.Count; row++)
        {
            state.CancellationToken.ThrowIfCancellationRequested();
            current[0] = 0;
            directions[row * rowLength] = 1;
            for (var column = 1; column <= after.Count; column++)
            {
                var best = previous[column];
                byte direction = 1;
                if (current[column - 1] > best + 0.00001f)
                {
                    best = current[column - 1];
                    direction = 2;
                }
                var similarity = Similarity(before[row - 1], after[column - 1]);
                if (similarity >= _options.MinimumContextSimilarity)
                {
                    var diagonal = previous[column - 1] + (float)similarity;
                    if (diagonal > best + 0.00001f)
                    {
                        best = diagonal;
                        direction = 3;
                    }
                }
                current[column] = best;
                directions[row * rowLength + column] = direction;
            }
            (previous, current) = (current, previous);
        }

        var result = new List<MatchCandidate>();
        var beforeIndex = before.Count;
        var afterIndex = after.Count;
        while (beforeIndex > 0 || afterIndex > 0)
        {
            var direction = directions[beforeIndex * rowLength + afterIndex];
            if (direction == 3)
            {
                var score = Similarity(
                    before[beforeIndex - 1],
                    after[afterIndex - 1]
                );
                result.Add(new MatchCandidate(
                    before[beforeIndex - 1],
                    after[afterIndex - 1],
                    score
                ));
                beforeIndex--;
                afterIndex--;
            }
            else if (direction == 1 && beforeIndex > 0)
            {
                beforeIndex--;
            }
            else if (afterIndex > 0)
            {
                afterIndex--;
            }
            else
            {
                break;
            }
        }
        result.Reverse();
        return RemoveAmbiguousContextualMatches(
            state,
            result,
            before,
            after,
            scopeFamily,
            boundedWindow: false
        );
    }

    private IReadOnlyList<MatchCandidate> RemoveAmbiguousContextualMatches(
        MatchingState state,
        IReadOnlyList<MatchCandidate> candidates,
        IReadOnlyList<NodeInfo> before,
        IReadOnlyList<NodeInfo> after,
        string scopeFamily,
        bool boundedWindow
    )
    {
        if (candidates.Count == 0)
        {
            return candidates;
        }
        const double margin = 0.02;
        var beforeIndex = before.Select((node, index) => (node.Node.Id, index))
            .ToDictionary(item => item.Id, item => item.index);
        var afterIndex = after.Select((node, index) => (node.Node.Id, index))
            .ToDictionary(item => item.Id, item => item.index);
        var accepted = new List<MatchCandidate>(candidates.Count);
        var rejected = 0;
        foreach (var candidate in candidates)
        {
            if (string.Equals(
                    candidate.Before.ModeledSubtreeFingerprint,
                    candidate.After.ModeledSubtreeFingerprint,
                    StringComparison.Ordinal
                ))
            {
                accepted.Add(candidate);
                continue;
            }
            var leftIndex = beforeIndex[candidate.Before.Node.Id];
            var rightIndex = afterIndex[candidate.After.Node.Id];
            var afterStart = boundedWindow
                ? Math.Max(0, rightIndex - _options.GreedyAlignmentWindow)
                : 0;
            var afterEnd = boundedWindow
                ? Math.Min(after.Count, rightIndex + _options.GreedyAlignmentWindow + 1)
                : after.Count;
            var beforeStart = boundedWindow
                ? Math.Max(0, leftIndex - _options.GreedyAlignmentWindow)
                : 0;
            var beforeEnd = boundedWindow
                ? Math.Min(before.Count, leftIndex + _options.GreedyAlignmentWindow + 1)
                : before.Count;
            var rowScores = after.Skip(afterStart)
                .Take(afterEnd - afterStart)
                .Where(node => node.Node.Kind == candidate.Before.Node.Kind)
                .Select(node => Similarity(candidate.Before, node))
                .Where(score => score >= _options.MinimumContextSimilarity)
                .OrderDescending()
                .Take(2)
                .ToArray();
            var columnScores = before.Skip(beforeStart)
                .Take(beforeEnd - beforeStart)
                .Where(node => node.Node.Kind == candidate.After.Node.Kind)
                .Select(node => Similarity(node, candidate.After))
                .Where(score => score >= _options.MinimumContextSimilarity)
                .OrderDescending()
                .Take(2)
                .ToArray();
            var rowAmbiguous = rowScores.Length > 1
                && rowScores[0] - rowScores[1] < margin;
            var columnAmbiguous = columnScores.Length > 1
                && columnScores[0] - columnScores[1] < margin;
            var notBest = (
                    rowScores.Length != 0
                    && candidate.Score + 0.00001 < rowScores[0]
                )
                || (
                    columnScores.Length != 0
                    && candidate.Score + 0.00001 < columnScores[0]
                );
            if (rowAmbiguous || columnAmbiguous || notBest)
            {
                rejected++;
                continue;
            }
            accepted.Add(candidate);
        }
        if (rejected != 0)
        {
            state.AmbiguousContextualMatchCount += rejected;
            state.AddDiagnostic(new WordSemanticDiffDiagnostic(
                "ambiguous_contextual_match",
                "Contextual candidates had equal or near-equal evidence and were left unmatched.",
                ScopeFamily: scopeFamily,
                BeforeCount: rejected,
                AfterCount: rejected
            ));
        }
        return accepted;
    }

    private IReadOnlyList<MatchCandidate> GreedyAlign(
        IReadOnlyList<NodeInfo> before,
        IReadOnlyList<NodeInfo> after
    )
    {
        var result = new List<MatchCandidate>();
        var beforeIndex = 0;
        var afterIndex = 0;
        while (beforeIndex < before.Count && afterIndex < after.Count)
        {
            MatchCandidate? best = null;
            var bestBefore = -1;
            var bestAfter = -1;
            var beforeEnd = Math.Min(
                before.Count,
                beforeIndex + _options.GreedyAlignmentWindow
            );
            var afterEnd = Math.Min(
                after.Count,
                afterIndex + _options.GreedyAlignmentWindow
            );
            for (var left = beforeIndex; left < beforeEnd; left++)
            {
                for (var right = afterIndex; right < afterEnd; right++)
                {
                    var score = Similarity(before[left], after[right]);
                    if (score < _options.MinimumContextSimilarity)
                    {
                        continue;
                    }
                    var adjusted = score - 0.002 * (
                        left - beforeIndex + right - afterIndex
                    );
                    if (
                        best is null
                        || adjusted > best.Score + 0.00001
                        || (
                            Math.Abs(adjusted - best.Score) < 0.00001
                            && (left < bestBefore || (left == bestBefore && right < bestAfter))
                        )
                    )
                    {
                        best = new MatchCandidate(before[left], after[right], adjusted);
                        bestBefore = left;
                        bestAfter = right;
                    }
                }
            }
            if (best is null)
            {
                if (before.Count - beforeIndex > after.Count - afterIndex)
                {
                    beforeIndex++;
                }
                else
                {
                    afterIndex++;
                }
                continue;
            }
            var rawScore = Similarity(best.Before, best.After);
            result.Add(best with { Score = rawScore });
            beforeIndex = bestBefore + 1;
            afterIndex = bestAfter + 1;
        }
        return result;
    }

    private double Similarity(NodeInfo before, NodeInfo after)
    {
        if (before.Node.Kind != after.Node.Kind)
        {
            return 0;
        }
        if (string.Equals(
                before.ModeledSubtreeFingerprint,
                after.ModeledSubtreeFingerprint,
                StringComparison.Ordinal
            ))
        {
            return 1;
        }
        if (string.Equals(
                before.Node.SubtreeFingerprint,
                after.Node.SubtreeFingerprint,
                StringComparison.Ordinal
            ))
        {
            return 1;
        }
        if (
            before.Node.IdentityKind == WordSemanticIdentityKind.DurableAnchor
            && after.Node.IdentityKind == WordSemanticIdentityKind.DurableAnchor
            && string.Equals(
                before.Node.IdentityFingerprint,
                after.Node.IdentityFingerprint,
                StringComparison.Ordinal
            )
        )
        {
            return 0.99;
        }
        var properties = PropertySimilarity(
            before.Node.Properties,
            after.Node.Properties
        );
        var structure = ChildKindSimilarity(
            before.ChildKindCounts,
            after.ChildKindCounts
        );
        if (
            _options.CompareText
            && before.Text is not null
            && after.Text is not null
        )
        {
            var text = TextSimilarity(before, after);
            return 0.67 * text + 0.18 * properties + 0.15 * structure;
        }
        return 0.58 * properties + 0.42 * structure;
    }

    private static double TextSimilarity(NodeInfo before, NodeInfo after)
    {
        if (string.Equals(
                before.Text!.ComparisonSha256,
                after.Text!.ComparisonSha256,
                StringComparison.Ordinal
            ))
        {
            return 1;
        }
        var left = before.Text.ComparisonSample;
        var right = after.Text.ComparisonSample;
        if (left.Length == 0 || right.Length == 0)
        {
            return 0;
        }
        var lengthRatio = (double)Math.Min(left.Length, right.Length)
            / Math.Max(left.Length, right.Length);
        var shingles = Dice(before.Shingles, after.Shingles);
        var prefix = 0;
        var prefixLimit = Math.Min(left.Length, right.Length);
        while (prefix < prefixLimit && left[prefix] == right[prefix])
        {
            prefix++;
        }
        var suffix = 0;
        while (
            suffix < prefixLimit - prefix
            && left[left.Length - 1 - suffix] == right[right.Length - 1 - suffix]
        )
        {
            suffix++;
        }
        var edgeRatio = (double)(prefix + suffix) / Math.Max(left.Length, right.Length);
        return Math.Min(1, 0.67 * shingles + 0.2 * lengthRatio + 0.13 * edgeRatio);
    }

    private static double Dice(IReadOnlySet<int>? left, IReadOnlySet<int>? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null ? 1 : 0;
        }
        var intersection = left.Count <= right.Count
            ? left.Count(right.Contains)
            : right.Count(left.Contains);
        return left.Count + right.Count == 0
            ? 1
            : 2d * intersection / (left.Count + right.Count);
    }

    private static double PropertySimilarity(
        IReadOnlyDictionary<string, string> left,
        IReadOnlyDictionary<string, string> right
    )
    {
        if (left.Count == 0 && right.Count == 0)
        {
            return 1;
        }
        var names = left.Keys.Concat(right.Keys).Distinct(StringComparer.Ordinal).ToArray();
        var equal = names.Count(name =>
            left.TryGetValue(name, out var leftValue)
            && right.TryGetValue(name, out var rightValue)
            && string.Equals(leftValue, rightValue, StringComparison.Ordinal)
        );
        return names.Length == 0 ? 1 : (double)equal / names.Length;
    }

    private static double ChildKindSimilarity(
        IReadOnlyDictionary<WordSemanticNodeKind, int> left,
        IReadOnlyDictionary<WordSemanticNodeKind, int> right
    )
    {
        if (left.Count == 0 && right.Count == 0)
        {
            return 1;
        }
        var kinds = left.Keys.Concat(right.Keys).Distinct().ToArray();
        var intersection = kinds.Sum(kind => Math.Min(
            left.GetValueOrDefault(kind),
            right.GetValueOrDefault(kind)
        ));
        var total = left.Values.Sum() + right.Values.Sum();
        return total == 0 ? 1 : 2d * intersection / total;
    }

    private static IReadOnlyList<IndexedNodePair> LongestIncreasingPairs(
        IReadOnlyList<IndexedNodePair> pairs
    )
    {
        if (pairs.Count == 0)
        {
            return Array.Empty<IndexedNodePair>();
        }
        var ordered = pairs.OrderBy(pair => pair.BeforeIndex)
            .ThenBy(pair => pair.AfterIndex)
            .ToArray();
        var tails = new int[ordered.Length];
        var previous = Enumerable.Repeat(-1, ordered.Length).ToArray();
        var length = 0;
        for (var index = 0; index < ordered.Length; index++)
        {
            var low = 0;
            var high = length;
            while (low < high)
            {
                var middle = low + (high - low) / 2;
                if (ordered[tails[middle]].AfterIndex < ordered[index].AfterIndex)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle;
                }
            }
            if (low > 0)
            {
                previous[index] = tails[low - 1];
            }
            tails[low] = index;
            if (low == length)
            {
                length++;
            }
        }
        var result = new IndexedNodePair[length];
        var current = tails[length - 1];
        for (var index = length - 1; index >= 0; index--)
        {
            result[index] = ordered[current];
            current = previous[current];
        }
        return result;
    }

    private static HashSet<SemanticNodeId> DetectTopLevelMoves(MatchingState state)
    {
        var candidates = new HashSet<SemanticNodeId>();
        foreach (var (beforeId, afterId) in state.BeforeToAfter)
        {
            var before = state.Before[beforeId];
            var after = state.After[afterId];
            if (before.Parent is null || after.Parent is null)
            {
                continue;
            }
            if (
                !state.BeforeToAfter.TryGetValue(
                    before.Parent.Node.Id,
                    out var mappedParent
                )
                || !state.AfterToBefore.ContainsKey(after.Parent.Node.Id)
            )
            {
                continue;
            }
            if (mappedParent != after.Parent.Node.Id)
            {
                candidates.Add(beforeId);
            }
        }

        foreach (var (beforeParentId, afterParentId) in state.BeforeToAfter)
        {
            var pairs = state.Before[beforeParentId].Node.Children
                .Select(child => state.Before[child.Id])
                .Where(before =>
                    state.BeforeToAfter.TryGetValue(before.Node.Id, out var afterId)
                    && state.After[afterId].Parent?.Node.Id == afterParentId
                )
                .Select(before =>
                {
                    var after = state.After[state.BeforeToAfter[before.Node.Id]];
                    return new IndexedNodePair(
                        before.SiblingIndex,
                        after.SiblingIndex,
                        before,
                        after
                    );
                })
                .OrderBy(pair => pair.BeforeIndex)
                .ToArray();
            if (pairs.Length < 2)
            {
                continue;
            }
            var ordered = LongestIncreasingPairs(pairs)
                .Select(pair => pair.Before.Node.Id)
                .ToHashSet();
            foreach (var pair in pairs)
            {
                if (!ordered.Contains(pair.Before.Node.Id))
                {
                    candidates.Add(pair.Before.Node.Id);
                }
            }
        }

        var topLevel = new HashSet<SemanticNodeId>();
        foreach (
            var candidate in candidates.OrderBy(id => state.Before[id].Depth)
                .ThenBy(id => state.Before[id].Node.SourceOrder)
        )
        {
            var ancestor = state.Before[candidate].Parent;
            var covered = false;
            while (ancestor is not null)
            {
                if (topLevel.Contains(ancestor.Node.Id))
                {
                    covered = true;
                    break;
                }
                ancestor = ancestor.Parent;
            }
            if (!covered)
            {
                topLevel.Add(candidate);
            }
        }
        return topLevel;
    }

    private IReadOnlyList<WordSemanticDifference> BuildSemanticDifferences(
        MatchingState state,
        IReadOnlySet<SemanticNodeId> moved,
        string beforeFingerprint,
        string afterFingerprint,
        CancellationToken cancellationToken
    )
    {
        var differences = new List<WordSemanticDifference>();
        var inspected = 0;
        foreach (
            var (beforeId, afterId) in state.BeforeToAfter
                .OrderBy(pair => state.Before[pair.Key].Node.SourceOrder)
        )
        {
            if ((inspected++ & 0xff) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
            var before = state.Before[beforeId];
            var after = state.After[afterId];
            if (
                !StructuralReportKinds.Contains(before.Node.Kind)
                && !PropertyReportKinds.Contains(before.Node.Kind)
                && !TextReportKinds.Contains(before.Node.Kind)
            )
            {
                continue;
            }
            var kinds = new List<WordSemanticDifferenceKind>();
            if (moved.Contains(beforeId))
            {
                kinds.Add(WordSemanticDifferenceKind.Moved);
            }
            WordSemanticTextDifference? textDifference = null;
            if (
                _options.CompareText
                && TextReportKinds.Contains(before.Node.Kind)
                && before.Text is not null
                && after.Text is not null
                && !string.Equals(
                    before.Text.ComparisonSha256,
                    after.Text.ComparisonSha256,
                    StringComparison.Ordinal
                )
            )
            {
                kinds.Add(WordSemanticDifferenceKind.TextChanged);
                textDifference = new WordSemanticTextDifference(
                    before.Text.PublicSnapshot,
                    after.Text.PublicSnapshot
                );
            }
            var detectedPropertyDifferences = PropertyReportKinds.Contains(
                before.Node.Kind
            )
                ? CompareProperties(before.Node.Properties, after.Node.Properties)
                : Array.Empty<WordSemanticPropertyDifference>();
            var propertyDifferences = _options.CompareProperties
                ? detectedPropertyDifferences
                : Array.Empty<WordSemanticPropertyDifference>();
            if (propertyDifferences.Count != 0)
            {
                kinds.Add(WordSemanticDifferenceKind.PropertiesChanged);
            }
            if (
                StructuralReportKinds.Contains(before.Node.Kind)
                && DirectStructureChanged(state, before, after)
            )
            {
                kinds.Add(WordSemanticDifferenceKind.StructureChanged);
            }
            var structuralMarkupChanged = !string.Equals(
                before.Node.StructuralFingerprint,
                after.Node.StructuralFingerprint,
                StringComparison.Ordinal
            );
            if (
                structuralMarkupChanged
                && detectedPropertyDifferences.Count == 0
                && !kinds.Contains(WordSemanticDifferenceKind.StructureChanged)
            )
            {
                kinds.Add(WordSemanticDifferenceKind.UnmodeledMarkupChanged);
            }
            if (kinds.Count == 0)
            {
                continue;
            }
            var match = state.MatchByBefore[beforeId];
            AddDifference(differences, CreateDifference(
                beforeFingerprint,
                afterFingerprint,
                kinds,
                before.Node.Kind,
                match.Basis,
                match.Confidence,
                match.Score,
                before,
                after,
                textDifference,
                propertyDifferences
            ));
        }

        foreach (
            var before in state.Before.Values
                .Where(info => !state.IsBeforeMatched(info.Node.Id))
                .Where(info => StructuralReportKinds.Contains(info.Node.Kind))
                .OrderBy(info => info.Node.SourceOrder)
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (HasUnmatchedStructuralAncestor(before, state, beforeSide: true))
            {
                continue;
            }
            AddDifference(differences, CreateDifference(
                beforeFingerprint,
                afterFingerprint,
                [WordSemanticDifferenceKind.Removed],
                before.Node.Kind,
                null,
                null,
                null,
                before,
                null,
                TextReportKinds.Contains(before.Node.Kind) && before.Text is not null
                    ? new WordSemanticTextDifference(before.Text.PublicSnapshot, null)
                    : null,
                Array.Empty<WordSemanticPropertyDifference>()
            ));
        }
        foreach (
            var after in state.After.Values
                .Where(info => !state.IsAfterMatched(info.Node.Id))
                .Where(info => StructuralReportKinds.Contains(info.Node.Kind))
                .OrderBy(info => info.Node.SourceOrder)
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (HasUnmatchedStructuralAncestor(after, state, beforeSide: false))
            {
                continue;
            }
            AddDifference(differences, CreateDifference(
                beforeFingerprint,
                afterFingerprint,
                [WordSemanticDifferenceKind.Added],
                after.Node.Kind,
                null,
                null,
                null,
                null,
                after,
                TextReportKinds.Contains(after.Node.Kind) && after.Text is not null
                    ? new WordSemanticTextDifference(null, after.Text.PublicSnapshot)
                    : null,
                Array.Empty<WordSemanticPropertyDifference>()
            ));
        }
        return differences
            .OrderBy(difference => DifferenceOrder(difference))
            .ThenBy(difference => difference.DifferenceId, StringComparer.Ordinal)
            .ToArray();
    }

    private void AddDifference(
        ICollection<WordSemanticDifference> differences,
        WordSemanticDifference difference
    )
    {
        if (differences.Count >= _options.MaxChanges)
        {
            throw new WordSemanticDiffLimitException(
                $"Semantic diff produced more than {_options.MaxChanges} reportable differences."
            );
        }
        differences.Add(difference);
    }

    private static bool HasUnmatchedStructuralAncestor(
        NodeInfo info,
        MatchingState state,
        bool beforeSide
    )
    {
        var ancestor = info.Parent;
        while (ancestor is not null)
        {
            var matched = beforeSide
                ? state.IsBeforeMatched(ancestor.Node.Id)
                : state.IsAfterMatched(ancestor.Node.Id);
            if (matched)
            {
                return false;
            }
            if (StructuralReportKinds.Contains(ancestor.Node.Kind))
            {
                return true;
            }
            ancestor = ancestor.Parent;
        }
        return false;
    }

    private static int DifferenceOrder(WordSemanticDifference difference) => Math.Min(
        difference.Before?.SourceOrder ?? int.MaxValue,
        difference.After?.SourceOrder ?? int.MaxValue
    );

    private static bool DirectStructureChanged(
        MatchingState state,
        NodeInfo before,
        NodeInfo after
    )
    {
        var beforeMapped = new List<SemanticNodeId>();
        foreach (var child in before.Node.Children)
        {
            if (
                !state.BeforeToAfter.TryGetValue(child.Id, out var afterChildId)
                || state.After[afterChildId].Parent?.Node.Id != after.Node.Id
            )
            {
                return true;
            }
            beforeMapped.Add(afterChildId);
        }
        var afterDirect = new List<SemanticNodeId>();
        foreach (var child in after.Node.Children)
        {
            if (
                !state.AfterToBefore.TryGetValue(child.Id, out var beforeChildId)
                || state.Before[beforeChildId].Parent?.Node.Id != before.Node.Id
            )
            {
                return true;
            }
            afterDirect.Add(child.Id);
        }
        return !beforeMapped.SequenceEqual(afterDirect);
    }

    private static IReadOnlyList<WordSemanticPropertyDifference> CompareProperties(
        IReadOnlyDictionary<string, string> before,
        IReadOnlyDictionary<string, string> after
    )
    {
        var names = before.Keys.Concat(after.Keys)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);
        var result = new List<WordSemanticPropertyDifference>();
        foreach (var name in names)
        {
            before.TryGetValue(name, out var beforeValue);
            after.TryGetValue(name, out var afterValue);
            if (string.Equals(beforeValue, afterValue, StringComparison.Ordinal))
            {
                continue;
            }
            result.Add(new WordSemanticPropertyDifference(
                name,
                beforeValue,
                afterValue
            ));
        }
        return result;
    }

    private static WordSemanticDifference CreateDifference(
        string beforePackageFingerprint,
        string afterPackageFingerprint,
        IReadOnlyList<WordSemanticDifferenceKind> kinds,
        WordSemanticNodeKind nodeKind,
        WordSemanticMatchBasis? matchBasis,
        WordSemanticMatchConfidence? confidence,
        double? score,
        NodeInfo? before,
        NodeInfo? after,
        WordSemanticTextDifference? text,
        IReadOnlyList<WordSemanticPropertyDifference> properties
    )
    {
        var orderedKinds = kinds.Distinct()
            .OrderBy(kind => kind)
            .ToArray();
        var id = CreateDifferenceId(
            beforePackageFingerprint,
            afterPackageFingerprint,
            nodeKind,
            orderedKinds,
            before?.Node.Id,
            after?.Node.Id
        );
        return new WordSemanticDifference(
            id,
            orderedKinds,
            nodeKind,
            matchBasis,
            confidence,
            score is null ? null : Math.Round(score.Value, 6),
            before?.Location,
            after?.Location,
            text,
            new ReadOnlyCollection<WordSemanticPropertyDifference>(properties.ToArray()),
            before?.Node.SubtreeFingerprint,
            after?.Node.SubtreeFingerprint
        );
    }

    private static string CreateDifferenceId(
        string beforePackageFingerprint,
        string afterPackageFingerprint,
        WordSemanticNodeKind nodeKind,
        IReadOnlyList<WordSemanticDifferenceKind> kinds,
        SemanticNodeId? beforeId,
        SemanticNodeId? afterId
    )
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendHashField(hash, "word-semantic-difference-v1");
        AppendHashField(hash, beforePackageFingerprint);
        AppendHashField(hash, afterPackageFingerprint);
        AppendHashField(hash, nodeKind.ToString());
        AppendHashField(hash, string.Join(',', kinds));
        AppendHashField(hash, beforeId?.Value ?? string.Empty);
        AppendHashField(hash, afterId?.Value ?? string.Empty);
        return "wdd_" + Base64Id(hash.GetHashAndReset(), 15);
    }

    private static string CreateDiffId(
        string beforePackageFingerprint,
        string afterPackageFingerprint,
        WordSemanticDiffOptions options
    )
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendHashField(hash, "word-semantic-diff-v1");
        AppendHashField(hash, beforePackageFingerprint);
        AppendHashField(hash, afterPackageFingerprint);
        AppendHashField(hash, options.CompareText ? "text" : "no-text");
        AppendHashField(hash, options.CompareProperties ? "properties" : "no-properties");
        AppendHashField(hash, options.CompareWhitespace ? "whitespace" : "ignore-whitespace");
        AppendHashField(hash, options.CaseSensitive ? "case" : "ignore-case");
        AppendHashField(hash, options.DetectMoves ? "moves" : "no-moves");
        AppendHashField(
            hash,
            options.MinimumContextSimilarity.ToString("R", CultureInfo.InvariantCulture)
        );
        return "wddiff_" + Base64Id(hash.GetHashAndReset(), 18);
    }

    private static string Base64Id(byte[] digest, int bytes) => Convert.ToBase64String(
        digest.AsSpan(0, bytes)
    ).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static void AppendHashField(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }

    private sealed class MatchingState
    {
        private readonly WordSemanticDiffOptions _options;
        private int _roleMatches;
        private int _exactNodeMatches;
        private int _durableMatches;
        private int _subtreeMatches;
        private int _contextualMatches;

        internal MatchingState(
            IReadOnlyDictionary<SemanticNodeId, NodeInfo> before,
            IReadOnlyDictionary<SemanticNodeId, NodeInfo> after,
            WordSemanticDiffOptions options,
            CancellationToken cancellationToken
        )
        {
            Before = before;
            After = after;
            _options = options;
            CancellationToken = cancellationToken;
        }

        internal IReadOnlyDictionary<SemanticNodeId, NodeInfo> Before { get; }

        internal IReadOnlyDictionary<SemanticNodeId, NodeInfo> After { get; }

        internal Dictionary<SemanticNodeId, SemanticNodeId> BeforeToAfter { get; } = [];

        internal Dictionary<SemanticNodeId, SemanticNodeId> AfterToBefore { get; } = [];

        internal Dictionary<SemanticNodeId, MatchMetadata> MatchByBefore { get; } = [];

        internal List<WordSemanticDiffDiagnostic> Diagnostics { get; } = [];

        internal CancellationToken CancellationToken { get; }

        internal int AmbiguousIdentityGroupCount { get; set; }

        internal int AmbiguousContextualMatchCount { get; set; }

        internal int AlignmentFallbackCount { get; set; }

        internal long AlignmentCellsEvaluated { get; private set; }

        internal bool IsBeforeMatched(SemanticNodeId id) => BeforeToAfter.ContainsKey(id);

        internal bool IsAfterMatched(SemanticNodeId id) => AfterToBefore.ContainsKey(id);

        internal bool Pair(
            NodeInfo before,
            NodeInfo after,
            WordSemanticMatchBasis basis,
            double score
        )
        {
            if (before.Node.Kind != after.Node.Kind)
            {
                throw new WordSemanticDiffPreconditionException(
                    "Semantic matcher attempted to pair different node kinds."
                );
            }
            if (
                IsBeforeMatched(before.Node.Id)
                || IsAfterMatched(after.Node.Id)
            )
            {
                return false;
            }
            BeforeToAfter.Add(before.Node.Id, after.Node.Id);
            AfterToBefore.Add(after.Node.Id, before.Node.Id);
            var confidence = basis switch
            {
                WordSemanticMatchBasis.DocumentRole
                    or WordSemanticMatchBasis.ExactNodeId
                    or WordSemanticMatchBasis.ExactSubtree => WordSemanticMatchConfidence.Exact,
                WordSemanticMatchBasis.DurableIdentity => WordSemanticMatchConfidence.High,
                WordSemanticMatchBasis.ContextualSimilarity when score >= 0.85 =>
                    WordSemanticMatchConfidence.High,
                WordSemanticMatchBasis.ContextualSimilarity when score >= 0.7 =>
                    WordSemanticMatchConfidence.Medium,
                _ => WordSemanticMatchConfidence.Low,
            };
            MatchByBefore.Add(before.Node.Id, new MatchMetadata(basis, confidence, score));
            switch (basis)
            {
                case WordSemanticMatchBasis.DocumentRole:
                    _roleMatches++;
                    break;
                case WordSemanticMatchBasis.ExactNodeId:
                    _exactNodeMatches++;
                    break;
                case WordSemanticMatchBasis.DurableIdentity:
                    _durableMatches++;
                    break;
                case WordSemanticMatchBasis.ExactSubtree:
                    _subtreeMatches++;
                    break;
                case WordSemanticMatchBasis.ContextualSimilarity:
                    _contextualMatches++;
                    break;
            }
            return true;
        }

        internal bool TryReserveAlignmentCells(long cells)
        {
            if (AlignmentCellsEvaluated + cells > _options.MaxAlignmentCells)
            {
                return false;
            }
            AlignmentCellsEvaluated += cells;
            return true;
        }

        internal void AddDiagnostic(WordSemanticDiffDiagnostic diagnostic)
        {
            if (Diagnostics.Count < _options.MaxDiagnostics)
            {
                Diagnostics.Add(diagnostic);
            }
        }

        internal WordSemanticDiffResult.MatchStatistics Statistics() => new(
            BeforeToAfter.Count,
            _roleMatches,
            _exactNodeMatches,
            _durableMatches,
            _subtreeMatches,
            _contextualMatches
        );
    }

    private sealed class NodeInfo
    {
        internal NodeInfo(
            WordSemanticNode node,
            NodeInfo? parent,
            int siblingIndex,
            int depth,
            string scopeFamily,
            NodeTextState? text,
            IReadOnlySet<int>? shingles,
            IReadOnlyDictionary<WordSemanticNodeKind, int> childKindCounts
        )
        {
            Node = node;
            Parent = parent;
            SiblingIndex = siblingIndex;
            Depth = depth;
            ScopeFamily = scopeFamily;
            Text = text;
            Shingles = shingles;
            ChildKindCounts = childKindCounts;
            Location = new WordSemanticNodeLocation(
                node.Id,
                node.Kind,
                node.ParentId,
                node.SourceOrder,
                siblingIndex,
                node.SourcePartUri,
                node.SourcePath,
                node.SourceElementOrdinal,
                scopeFamily
            );
        }

        internal WordSemanticNode Node { get; }

        internal NodeInfo? Parent { get; }

        internal int SiblingIndex { get; }

        internal int Depth { get; }

        internal string ScopeFamily { get; }

        internal NodeTextState? Text { get; }

        internal IReadOnlySet<int>? Shingles { get; }

        internal IReadOnlyDictionary<WordSemanticNodeKind, int> ChildKindCounts { get; }

        internal WordSemanticNodeLocation Location { get; }

        internal string ModeledSubtreeFingerprint { get; set; } = string.Empty;
    }

    private sealed class TextDigester
    {
        private readonly WordSemanticDiffOptions _options;
        private readonly TextBudget _budget;
        private readonly IncrementalHash _hash = IncrementalHash.CreateHash(
            HashAlgorithmName.SHA256
        );
        private readonly StringBuilder _captured;
        private readonly StringBuilder _comparisonSample;
        private bool _hasComparisonContent;
        private bool _pendingWhitespace;
        private int _rawCharacters;
        private int _comparisonCharacters;

        internal TextDigester(
            WordSemanticDiffOptions options,
            TextBudget budget
        )
        {
            _options = options;
            _budget = budget;
            _captured = new StringBuilder(options.MaxSimilarityTextCharacters);
            _comparisonSample = new StringBuilder(options.MaxSimilarityTextCharacters);
        }

        internal void Append(string value)
        {
            _budget.AddProcessed(value.Length);
            _rawCharacters = checked(_rawCharacters + value.Length);
            var captureRemaining = _options.MaxSimilarityTextCharacters - _captured.Length;
            if (captureRemaining > 0)
            {
                var captured = Math.Min(captureRemaining, value.Length);
                _budget.AddCaptured(captured);
                _captured.Append(value.AsSpan(0, captured));
            }
            var normalized = new StringBuilder(value.Length + 1);
            foreach (var raw in value)
            {
                var character = _options.CaseSensitive
                    ? raw
                    : char.ToUpperInvariant(raw);
                if (!_options.CompareWhitespace && char.IsWhiteSpace(character))
                {
                    if (_hasComparisonContent)
                    {
                        _pendingWhitespace = true;
                    }
                    continue;
                }
                if (_pendingWhitespace)
                {
                    normalized.Append(' ');
                    _pendingWhitespace = false;
                }
                normalized.Append(character);
                _hasComparisonContent = true;
            }
            if (_options.CompareWhitespace)
            {
                _hasComparisonContent |= value.Length != 0;
            }
            AppendNormalized(normalized.ToString());
        }

        internal NodeTextState Finish()
        {
            var hash = Convert.ToHexString(_hash.GetHashAndReset()).ToLowerInvariant();
            _hash.Dispose();
            return new NodeTextState(
                new WordSemanticTextSnapshot(
                    _rawCharacters,
                    hash,
                    _captured.ToString(),
                    _rawCharacters > _captured.Length
                ),
                hash,
                _comparisonSample.ToString(),
                _comparisonCharacters > _comparisonSample.Length
            );
        }

        private void AppendNormalized(string value)
        {
            if (value.Length == 0)
            {
                return;
            }
            var bytes = Encoding.UTF8.GetBytes(value);
            _hash.AppendData(bytes);
            _comparisonCharacters = checked(_comparisonCharacters + value.Length);
            var remaining = _options.MaxSimilarityTextCharacters
                - _comparisonSample.Length;
            if (remaining > 0)
            {
                var captured = Math.Min(remaining, value.Length);
                _budget.AddCaptured(captured);
                _comparisonSample.Append(value.AsSpan(0, captured));
            }
        }
    }

    private sealed class TextBudget
    {
        private readonly WordSemanticDiffOptions _options;
        private long _processed;
        private long _captured;

        internal TextBudget(WordSemanticDiffOptions options)
        {
            _options = options;
        }

        internal void AddProcessed(int characters)
        {
            _processed = checked(_processed + characters);
            if (_processed > _options.MaxTotalTextCharactersProcessedPerDocument)
            {
                throw new WordSemanticDiffLimitException(
                    "Semantic diff exceeded its per-document text-processing budget."
                );
            }
        }

        internal void AddCaptured(int characters)
        {
            _captured = checked(_captured + characters);
            if (_captured > _options.MaxTotalTextCharactersCapturedPerDocument)
            {
                throw new WordSemanticDiffLimitException(
                    "Semantic diff exceeded its per-document captured-text budget."
                );
            }
        }
    }

    private sealed record NodeTextState(
        WordSemanticTextSnapshot PublicSnapshot,
        string ComparisonSha256,
        string ComparisonSample,
        bool ComparisonSampleTruncated
    );

    private sealed record MatchMetadata(
        WordSemanticMatchBasis Basis,
        WordSemanticMatchConfidence Confidence,
        double Score
    );

    private sealed record NodePair(NodeInfo Before, NodeInfo After);

    private sealed record IndexedNodePair(
        int BeforeIndex,
        int AfterIndex,
        NodeInfo Before,
        NodeInfo After
    );

    private sealed record MatchCandidate(
        NodeInfo Before,
        NodeInfo After,
        double Score
    );
}

public sealed class WordSemanticDiffPreconditionException : InvalidOperationException
{
    public WordSemanticDiffPreconditionException(string message)
        : base(message)
    {
    }
}

public sealed class WordSemanticDiffLimitException : InvalidOperationException
{
    public WordSemanticDiffLimitException(string message)
        : base(message)
    {
    }
}
