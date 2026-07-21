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

        if (Kinds is { Count: > 32 })
        {
            throw new ArgumentException("Kinds cannot contain more than 32 values.", nameof(Kinds));
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
    }
}

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
        int scannedNodeCount,
        int matchedNodeCount,
        int offset,
        IReadOnlyList<WordSemanticQueryMatch> matches
    )
    {
        PackageFingerprint = packageFingerprint;
        ScannedNodeCount = scannedNodeCount;
        MatchedNodeCount = matchedNodeCount;
        Offset = offset;
        Matches = new ReadOnlyCollection<WordSemanticQueryMatch>(matches.ToArray());
        var consumed = (long)offset + Matches.Count;
        NextOffset = consumed < matchedNodeCount
            ? (int)consumed
            : null;
    }

    public string PackageFingerprint { get; }

    public int ScannedNodeCount { get; }

    public int MatchedNodeCount { get; }

    public int Offset { get; }

    public int ReturnedNodeCount => Matches.Count;

    public int? NextOffset { get; }

    public IReadOnlyList<WordSemanticQueryMatch> Matches { get; }
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
            scanned,
            matched,
            query.Offset,
            page
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
        foreach (var candidate in candidates)
        {
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
