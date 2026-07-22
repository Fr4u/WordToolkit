using System.Collections.ObjectModel;

namespace WordToolkit.Engine.Semantics;

public enum WordSemanticTextMatchMode
{
    Contains,
    Equals,
    StartsWith,
    EndsWith,
}

public enum WordSemanticTextScope
{
    Node,
    Subtree,
}

public sealed record WordSemanticRelatedNodePredicate
{
    public IReadOnlyCollection<WordSemanticNodeKind>? Kinds { get; init; }

    public IReadOnlyDictionary<string, string>? PropertyEquals { get; init; }

    internal void Validate(string name)
    {
        if (Kinds is null && PropertyEquals is null)
        {
            throw new ArgumentException(
                $"{name} must contain kinds or property equality predicates.",
                name
            );
        }

        if (Kinds is { Count: 0 or > 64 })
        {
            throw new ArgumentException(
                $"{name}.Kinds must contain between 1 and 64 values.",
                name
            );
        }

        if (PropertyEquals is { Count: 0 or > 16 })
        {
            throw new ArgumentException(
                $"{name}.PropertyEquals must contain between 1 and 16 entries.",
                name
            );
        }

        if (PropertyEquals is not null)
        {
            foreach (var (propertyName, value) in PropertyEquals)
            {
                if (string.IsNullOrWhiteSpace(propertyName) || propertyName.Length > 128)
                {
                    throw new ArgumentException(
                        $"{name} property names must contain 1 to 128 characters.",
                        name
                    );
                }

                if (value is null || value.Length > 1_024)
                {
                    throw new ArgumentException(
                        $"{name} property values cannot exceed 1,024 characters.",
                        name
                    );
                }
            }
        }
    }

    internal bool Matches(WordSemanticNode node)
    {
        if (Kinds is not null && !Kinds.Contains(node.Kind))
        {
            return false;
        }

        if (PropertyEquals is null)
        {
            return true;
        }

        foreach (var (name, expected) in PropertyEquals)
        {
            if (
                !node.Properties.TryGetValue(name, out var actual)
                || !string.Equals(actual, expected, StringComparison.Ordinal)
            )
            {
                return false;
            }
        }

        return true;
    }
}

public sealed record WordSemanticQuery
{
    public IReadOnlyCollection<WordSemanticNodeKind>? Kinds { get; init; }

    public string? Text { get; init; }

    public WordSemanticTextMatchMode TextMatch { get; init; } =
        WordSemanticTextMatchMode.Contains;

    public WordSemanticTextScope TextScope { get; init; } =
        WordSemanticTextScope.Node;

    public bool CaseSensitive { get; init; }

    public IReadOnlyDictionary<string, string>? PropertyEquals { get; init; }

    public WordSemanticRelatedNodePredicate? Ancestor { get; init; }

    public WordSemanticRelatedNodePredicate? Descendant { get; init; }

    public SemanticNodeId? WithinNodeId { get; init; }

    public string? SourcePartUri { get; init; }

    public int Offset { get; init; }

    public int Limit { get; init; } = 80;

    public int TextPreviewCharacters { get; init; } = 160;

    public bool IncludeProperties { get; init; }

    public bool IncludeSource { get; init; }

    internal void Validate()
    {
        if (Kinds is { Count: 0 })
        {
            throw new ArgumentException("Kinds cannot be an empty collection.", nameof(Kinds));
        }

        if (Kinds is { Count: > 64 })
        {
            throw new ArgumentException("Kinds cannot contain more than 64 values.", nameof(Kinds));
        }

        if (Text is { Length: 0 })
        {
            throw new ArgumentException("Text filter cannot be empty.", nameof(Text));
        }

        if (Text is { Length: > 4_096 })
        {
            throw new ArgumentException(
                "Text filter cannot exceed 4,096 characters.",
                nameof(Text)
            );
        }

        if (PropertyEquals is { Count: > 16 })
        {
            throw new ArgumentException(
                "Property filter cannot contain more than 16 entries.",
                nameof(PropertyEquals)
            );
        }

        if (PropertyEquals is not null)
        {
            foreach (var (name, value) in PropertyEquals)
            {
                if (string.IsNullOrWhiteSpace(name) || name.Length > 128)
                {
                    throw new ArgumentException(
                        "Property filter names must contain 1 to 128 characters.",
                        nameof(PropertyEquals)
                    );
                }

                if (value is null || value.Length > 1_024)
                {
                    throw new ArgumentException(
                        "Property filter values cannot exceed 1,024 characters.",
                        nameof(PropertyEquals)
                    );
                }
            }
        }

        if (SourcePartUri is { Length: > 1_024 })
        {
            throw new ArgumentException(
                "Source part URI cannot exceed 1,024 characters.",
                nameof(SourcePartUri)
            );
        }

        if (Offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Offset));
        }

        if (Limit is < 1 or > 200)
        {
            throw new ArgumentOutOfRangeException(nameof(Limit));
        }

        if (TextPreviewCharacters is < 0 or > 400)
        {
            throw new ArgumentOutOfRangeException(nameof(TextPreviewCharacters));
        }

        Ancestor?.Validate(nameof(Ancestor));
        Descendant?.Validate(nameof(Descendant));
    }
}

internal sealed record WordSemanticRelationMatchSets(
    IReadOnlySet<SemanticNodeId>? HasMatchingAncestor,
    IReadOnlySet<SemanticNodeId>? HasMatchingDescendant
);

public sealed record WordSemanticQueryMatch(
    SemanticNodeId NodeId,
    WordSemanticNodeKind Kind,
    SemanticNodeId? ParentId,
    int SourceOrder,
    string? TextPreview,
    bool TextPreviewTruncated,
    IReadOnlyDictionary<string, string>? Properties,
    string? SourcePartUri,
    string? SourcePath,
    int? SourceElementOrdinal
);

public sealed class WordSemanticQueryResult
{
    internal WordSemanticQueryResult(
        string packageFingerprint,
        int totalNodeCount,
        int scannedNodeCount,
        int matchedNodeCount,
        int offset,
        IReadOnlyList<WordSemanticQueryMatch> matches,
        bool semanticIndexUsed,
        string candidateSeed,
        string? semanticIndexFingerprint
    )
    {
        PackageFingerprint = packageFingerprint;
        TotalNodeCount = totalNodeCount;
        ScannedNodeCount = scannedNodeCount;
        MatchedNodeCount = matchedNodeCount;
        Offset = offset;
        Matches = new ReadOnlyCollection<WordSemanticQueryMatch>(matches.ToArray());
        var consumed = (long)offset + Matches.Count;
        NextOffset = consumed < matchedNodeCount
            ? (int)consumed
            : null;
        SemanticIndexUsed = semanticIndexUsed;
        CandidateSeed = candidateSeed;
        SemanticIndexFingerprint = semanticIndexFingerprint;
    }

    public string PackageFingerprint { get; }

    public int TotalNodeCount { get; }

    public int ScannedNodeCount { get; }

    public int MatchedNodeCount { get; }

    public int Offset { get; }

    public int ReturnedNodeCount => Matches.Count;

    public int? NextOffset { get; }

    public IReadOnlyList<WordSemanticQueryMatch> Matches { get; }

    public bool SemanticIndexUsed { get; }

    public string CandidateSeed { get; }

    public string? SemanticIndexFingerprint { get; }
}

public sealed class WordSemanticQueryEngine
{
    public WordSemanticQueryResult Query(
        WordSemanticDocument document,
        WordSemanticQuery query,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(query);
        query.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        var candidates = ResolveScope(document, query.WithinNodeId);
        var relations = WordSemanticRelationshipEvaluator.Resolve(
            document,
            query,
            ancestorMatches: null,
            descendantMatches: null,
            cancellationToken
        );
        return QueryCore(
            document,
            query,
            candidates,
            semanticIndexUsed: false,
            candidateSeed: query.WithinNodeId is null ? "all_nodes" : "subtree",
            semanticIndexFingerprint: null,
            relations,
            cancellationToken
        );
    }

    public WordSemanticQueryResult Query(
        WordSemanticIndex index,
        WordSemanticQuery query,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(query);
        query.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        var candidates = index.ResolveCandidates(query, cancellationToken);
        return QueryCore(
            index.Document,
            query,
            candidates.Nodes,
            semanticIndexUsed: true,
            candidateSeed: candidates.Seed,
            semanticIndexFingerprint: index.IndexFingerprint,
            candidates.Relations,
            cancellationToken
        );
    }

    private static WordSemanticQueryResult QueryCore(
        WordSemanticDocument document,
        WordSemanticQuery query,
        IEnumerable<WordSemanticNode> candidates,
        bool semanticIndexUsed,
        string candidateSeed,
        string? semanticIndexFingerprint,
        WordSemanticRelationMatchSets relations,
        CancellationToken cancellationToken
    )
    {
        var kinds = query.Kinds is null
            ? null
            : new HashSet<WordSemanticNodeKind>(query.Kinds);
        var page = new List<WordSemanticQueryMatch>(query.Limit);
        var scanned = 0;
        var matched = 0;
        foreach (var node in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            scanned++;
            if (kinds is not null && !kinds.Contains(node.Kind))
            {
                continue;
            }

            if (
                query.SourcePartUri is not null
                && !string.Equals(
                    node.SourcePartUri,
                    query.SourcePartUri,
                    StringComparison.Ordinal
                )
            )
            {
                continue;
            }

            if (!PropertiesMatch(node, query.PropertyEquals))
            {
                continue;
            }

            if (
                relations.HasMatchingAncestor is not null
                && !relations.HasMatchingAncestor.Contains(node.Id)
            )
            {
                continue;
            }

            if (
                relations.HasMatchingDescendant is not null
                && !relations.HasMatchingDescendant.Contains(node.Id)
            )
            {
                continue;
            }

            if (
                query.Text is not null
                && !TextMatches(
                    EnumerateTextSegments(node, query.TextScope),
                    query.Text,
                    query.TextMatch,
                    query.CaseSensitive
                )
            )
            {
                continue;
            }

            if (matched >= query.Offset && page.Count < query.Limit)
            {
                page.Add(ProjectMatch(node, query));
            }

            checked
            {
                matched++;
            }
        }

        return new WordSemanticQueryResult(
            document.PackageFingerprint,
            document.NodeCount,
            scanned,
            matched,
            query.Offset,
            page,
            semanticIndexUsed,
            candidateSeed,
            semanticIndexFingerprint
        );
    }

    private static IEnumerable<WordSemanticNode> ResolveScope(
        WordSemanticDocument document,
        SemanticNodeId? withinNodeId
    )
    {
        if (withinNodeId is null)
        {
            return document.Nodes;
        }

        if (!document.TryGetNode(withinNodeId.Value, out var node) || node is null)
        {
            throw new KeyNotFoundException(
                $"Semantic scope node '{withinNodeId.Value}' does not exist."
            );
        }

        return node.DescendantsAndSelf();
    }

    private static bool PropertiesMatch(
        WordSemanticNode node,
        IReadOnlyDictionary<string, string>? filters
    )
    {
        if (filters is null)
        {
            return true;
        }

        foreach (var (name, expected) in filters)
        {
            if (
                !node.Properties.TryGetValue(name, out var actual)
                || !string.Equals(actual, expected, StringComparison.Ordinal)
            )
            {
                return false;
            }
        }

        return true;
    }

    private static WordSemanticQueryMatch ProjectMatch(
        WordSemanticNode node,
        WordSemanticQuery query
    )
    {
        string? preview = null;
        var truncated = false;
        if (query.TextPreviewCharacters != 0)
        {
            var raw = node.TextPreview(checked(query.TextPreviewCharacters + 1));
            truncated = raw.Length > query.TextPreviewCharacters;
            preview = truncated ? raw[..query.TextPreviewCharacters] : raw;
        }

        return new WordSemanticQueryMatch(
            node.Id,
            node.Kind,
            node.ParentId,
            node.SourceOrder,
            preview,
            truncated,
            query.IncludeProperties ? node.Properties : null,
            query.IncludeSource ? node.SourcePartUri : null,
            query.IncludeSource ? node.SourcePath : null,
            query.IncludeSource ? node.SourceElementOrdinal : null
        );
    }

    private static IEnumerable<string> EnumerateTextSegments(
        WordSemanticNode node,
        WordSemanticTextScope scope
    )
    {
        var candidates = scope == WordSemanticTextScope.Subtree
            ? node.DescendantsAndSelf()
            : [node];
        var hasText = false;
        foreach (var candidate in candidates)
        {
            if (candidate.Kind == WordSemanticNodeKind.Paragraph && hasText)
            {
                yield return "\n";
            }

            var value = candidate.Kind switch
            {
                WordSemanticNodeKind.Text or WordSemanticNodeKind.Field =>
                    candidate.Text,
                WordSemanticNodeKind.Tab => "\t",
                WordSemanticNodeKind.Break => "\n",
                _ => null,
            };
            if (!string.IsNullOrEmpty(value))
            {
                yield return value;
                hasText = true;
            }
        }
    }

    private static bool TextMatches(
        IEnumerable<string> segments,
        string pattern,
        WordSemanticTextMatchMode mode,
        bool caseSensitive
    )
    {
        var comparison = caseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;
        return mode switch
        {
            WordSemanticTextMatchMode.Contains =>
                ContainsAcrossSegments(segments, pattern, comparison),
            WordSemanticTextMatchMode.Equals =>
                EqualsAcrossSegments(segments, pattern, comparison),
            WordSemanticTextMatchMode.StartsWith =>
                StartsWithAcrossSegments(segments, pattern, comparison),
            WordSemanticTextMatchMode.EndsWith =>
                EndsWithAcrossSegments(segments, pattern, comparison),
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };
    }

    private static bool ContainsAcrossSegments(
        IEnumerable<string> segments,
        string pattern,
        StringComparison comparison
    )
    {
        var tail = string.Empty;
        var boundaryLength = pattern.Length - 1;
        foreach (var segment in segments)
        {
            if (segment.Contains(pattern, comparison))
            {
                return true;
            }

            if (boundaryLength > 0 && tail.Length != 0)
            {
                var prefixLength = Math.Min(boundaryLength, segment.Length);
                if ((tail + segment[..prefixLength]).Contains(pattern, comparison))
                {
                    return true;
                }
            }

            if (boundaryLength <= 0)
            {
                continue;
            }

            if (segment.Length >= boundaryLength)
            {
                tail = segment[^boundaryLength..];
            }
            else
            {
                tail += segment;
                if (tail.Length > boundaryLength)
                {
                    tail = tail[^boundaryLength..];
                }
            }
        }

        return false;
    }

    private static bool EqualsAcrossSegments(
        IEnumerable<string> segments,
        string pattern,
        StringComparison comparison
    )
    {
        var position = 0;
        foreach (var segment in segments)
        {
            if (segment.Length > pattern.Length - position)
            {
                return false;
            }

            if (
                !segment.AsSpan().Equals(
                    pattern.AsSpan(position, segment.Length),
                    comparison
                )
            )
            {
                return false;
            }

            position += segment.Length;
        }

        return position == pattern.Length;
    }

    private static bool StartsWithAcrossSegments(
        IEnumerable<string> segments,
        string pattern,
        StringComparison comparison
    )
    {
        var position = 0;
        foreach (var segment in segments)
        {
            var compared = Math.Min(segment.Length, pattern.Length - position);
            if (
                compared > 0
                && !segment.AsSpan(0, compared).Equals(
                    pattern.AsSpan(position, compared),
                    comparison
                )
            )
            {
                return false;
            }

            position += compared;
            if (position == pattern.Length)
            {
                return true;
            }
        }

        return false;
    }

    private static bool EndsWithAcrossSegments(
        IEnumerable<string> segments,
        string pattern,
        StringComparison comparison
    )
    {
        var tail = string.Empty;
        long totalLength = 0;
        foreach (var segment in segments)
        {
            checked
            {
                totalLength += segment.Length;
            }

            if (segment.Length >= pattern.Length)
            {
                tail = segment[^pattern.Length..];
                continue;
            }

            tail += segment;
            if (tail.Length > pattern.Length)
            {
                tail = tail[^pattern.Length..];
            }
        }

        return totalLength >= pattern.Length
            && string.Equals(tail, pattern, comparison);
    }
}

internal static class WordSemanticRelationshipEvaluator
{
    internal static WordSemanticRelationMatchSets Resolve(
        WordSemanticDocument document,
        WordSemanticQuery query,
        IReadOnlyCollection<WordSemanticNode>? ancestorMatches,
        IReadOnlyCollection<WordSemanticNode>? descendantMatches,
        CancellationToken cancellationToken
    )
    {
        var hasAncestor = query.Ancestor is null
            ? null
            : ResolveHavingAncestor(
                document,
                ancestorMatches ?? FindMatches(document, query.Ancestor, cancellationToken),
                cancellationToken
            );
        var hasDescendant = query.Descendant is null
            ? null
            : ResolveHavingDescendant(
                document,
                descendantMatches
                    ?? FindMatches(document, query.Descendant, cancellationToken),
                cancellationToken
            );
        return new WordSemanticRelationMatchSets(hasAncestor, hasDescendant);
    }

    private static IReadOnlyCollection<WordSemanticNode> FindMatches(
        WordSemanticDocument document,
        WordSemanticRelatedNodePredicate predicate,
        CancellationToken cancellationToken
    )
    {
        var matches = new List<WordSemanticNode>();
        foreach (var node in document.Nodes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (predicate.Matches(node))
            {
                matches.Add(node);
            }
        }
        return matches;
    }

    private static IReadOnlySet<SemanticNodeId> ResolveHavingAncestor(
        WordSemanticDocument document,
        IReadOnlyCollection<WordSemanticNode> matchingAncestors,
        CancellationToken cancellationToken
    )
    {
        var matchingIds = matchingAncestors.Select(node => node.Id).ToHashSet();
        var result = new HashSet<SemanticNodeId>();
        var stack = new Stack<(WordSemanticNode Node, bool AncestorMatched)>();
        stack.Push((document.Root, false));
        while (stack.TryPop(out var item))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var childHasMatchingAncestor =
                item.AncestorMatched || matchingIds.Contains(item.Node.Id);
            for (var index = item.Node.Children.Count - 1; index >= 0; index--)
            {
                var child = item.Node.Children[index];
                if (childHasMatchingAncestor)
                {
                    result.Add(child.Id);
                }
                stack.Push((child, childHasMatchingAncestor));
            }
        }
        return result;
    }

    private static IReadOnlySet<SemanticNodeId> ResolveHavingDescendant(
        WordSemanticDocument document,
        IReadOnlyCollection<WordSemanticNode> matchingDescendants,
        CancellationToken cancellationToken
    )
    {
        var matchingIds = matchingDescendants.Select(node => node.Id).ToHashSet();
        var subtreeContainsMatch = new HashSet<SemanticNodeId>();
        var result = new HashSet<SemanticNodeId>();
        var nodes = document.Root.DescendantsAndSelf().ToArray();
        for (var index = nodes.Length - 1; index >= 0; index--)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var node = nodes[index];
            var descendantMatches = node.Children.Any(child =>
                matchingIds.Contains(child.Id)
                || subtreeContainsMatch.Contains(child.Id)
            );
            if (descendantMatches)
            {
                result.Add(node.Id);
            }
            if (descendantMatches || matchingIds.Contains(node.Id))
            {
                subtreeContainsMatch.Add(node.Id);
            }
        }
        return result;
    }
}
