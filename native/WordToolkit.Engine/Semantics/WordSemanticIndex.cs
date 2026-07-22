using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;

namespace WordToolkit.Engine.Semantics;

public sealed record WordSemanticIndexOptions
{
    public static WordSemanticIndexOptions Default { get; } = new();

    public int MaxNodeCount { get; init; } = 100_000;

    public int MaxPropertyOccurrences { get; init; } = 1_000_000;

    internal void Validate()
    {
        if (MaxNodeCount is < 1 or > 1_000_000)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxNodeCount));
        }

        if (MaxPropertyOccurrences is < 1 or > 8_000_000)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxPropertyOccurrences));
        }
    }
}

public sealed class WordSemanticIndexLimitException : IOException
{
    public WordSemanticIndexLimitException(string message)
        : base(message)
    {
    }
}

public sealed record WordSemanticIndexCandidateSet(
    IReadOnlyList<WordSemanticNode> Nodes,
    string Seed,
    int CandidateNodeCount
);

public sealed class WordSemanticIndex
{
    private const string FormatVersion = "word-semantic-index-v1";
    private readonly IReadOnlyList<WordSemanticNode> _nodes;
    private readonly IReadOnlyDictionary<WordSemanticNodeKind, int[]> _kindPostings;
    private readonly IReadOnlyDictionary<string, int[]> _partPostings;
    private readonly IReadOnlyDictionary<PropertyPostingKey, int[]> _propertyPostings;
    private readonly IReadOnlyDictionary<SemanticNodeId, int> _positions;

    private WordSemanticIndex(
        WordSemanticDocument document,
        IReadOnlyList<WordSemanticNode> nodes,
        IReadOnlyDictionary<WordSemanticNodeKind, int[]> kindPostings,
        IReadOnlyDictionary<string, int[]> partPostings,
        IReadOnlyDictionary<PropertyPostingKey, int[]> propertyPostings,
        IReadOnlyDictionary<SemanticNodeId, int> positions,
        int propertyOccurrenceCount
    )
    {
        Document = document;
        _nodes = nodes;
        _kindPostings = kindPostings;
        _partPostings = partPostings;
        _propertyPostings = propertyPostings;
        _positions = positions;
        PropertyOccurrenceCount = propertyOccurrenceCount;
        DistinctPropertyValueCount = propertyPostings.Count;
        KindCounts = new ReadOnlyDictionary<WordSemanticNodeKind, int>(
            kindPostings.ToDictionary(item => item.Key, item => item.Value.Length)
        );
        PartCounts = new ReadOnlyDictionary<string, int>(
            partPostings.ToDictionary(
                item => item.Key,
                item => item.Value.Length,
                StringComparer.Ordinal
            )
        );
        IndexedPropertyNames = new ReadOnlyCollection<string>(
            propertyPostings.Keys
                .Select(key => key.Name)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray()
        );
        IndexFingerprint = BuildFingerprint(document, nodes.Count, propertyOccurrenceCount);
    }

    public WordSemanticDocument Document { get; }

    public string PackageFingerprint => Document.PackageFingerprint;

    public string IndexFingerprint { get; }

    public int NodeCount => _nodes.Count;

    public int PropertyOccurrenceCount { get; }

    public int DistinctPropertyValueCount { get; }

    public IReadOnlyDictionary<WordSemanticNodeKind, int> KindCounts { get; }

    public IReadOnlyDictionary<string, int> PartCounts { get; }

    public IReadOnlyList<string> IndexedPropertyNames { get; }

    public static WordSemanticIndex Build(
        WordSemanticDocument document,
        WordSemanticIndexOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(document);
        options ??= WordSemanticIndexOptions.Default;
        options.Validate();
        cancellationToken.ThrowIfCancellationRequested();

        if (document.NodeCount > options.MaxNodeCount)
        {
            throw new WordSemanticIndexLimitException(
                $"Semantic document contains {document.NodeCount} nodes; "
                    + $"the in-memory index limit is {options.MaxNodeCount}."
            );
        }

        var nodes = document.Nodes.ToArray();
        var kindBuilders = new Dictionary<WordSemanticNodeKind, List<int>>();
        var partBuilders = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        var propertyBuilders = new Dictionary<PropertyPostingKey, List<int>>();
        var positions = new Dictionary<SemanticNodeId, int>(nodes.Length);
        var propertyOccurrences = 0;

        for (var position = 0; position < nodes.Length; position++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var node = nodes[position];
            positions.Add(node.Id, position);
            AddPosting(kindBuilders, node.Kind, position);
            AddPosting(partBuilders, node.SourcePartUri, position);

            foreach (var property in node.Properties)
            {
                checked
                {
                    propertyOccurrences++;
                }
                if (propertyOccurrences > options.MaxPropertyOccurrences)
                {
                    throw new WordSemanticIndexLimitException(
                        "Semantic property occurrence count exceeds the bounded "
                            + $"index limit of {options.MaxPropertyOccurrences}."
                    );
                }

                AddPosting(
                    propertyBuilders,
                    new PropertyPostingKey(property.Key, property.Value),
                    position
                );
            }
        }

        return new WordSemanticIndex(
            document,
            new ReadOnlyCollection<WordSemanticNode>(nodes),
            FreezePostings(kindBuilders, cancellationToken: cancellationToken),
            FreezePostings(
                partBuilders,
                StringComparer.Ordinal,
                cancellationToken
            ),
            FreezePostings(propertyBuilders, cancellationToken: cancellationToken),
            new ReadOnlyDictionary<SemanticNodeId, int>(positions),
            propertyOccurrences
        );
    }

    public WordSemanticIndexCandidateSet ResolveCandidates(
        WordSemanticQuery query,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(query);
        query.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        var seeds = new List<(string Name, IReadOnlyList<int> Positions)>();

        if (query.Kinds is not null)
        {
            var kindPositions = query.Kinds
                .Distinct()
                .SelectMany(kind =>
                    _kindPostings.TryGetValue(kind, out var postings)
                        ? postings
                        : []
                )
                .Order()
                .ToArray();
            seeds.Add(("kind", kindPositions));
        }

        if (query.SourcePartUri is not null)
        {
            seeds.Add(
                (
                    "source_part",
                    _partPostings.TryGetValue(query.SourcePartUri, out var postings)
                        ? postings
                        : []
                )
            );
        }

        if (query.PropertyEquals is not null)
        {
            foreach (var property in query.PropertyEquals.OrderBy(
                item => item.Key,
                StringComparer.Ordinal
            ))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var key = new PropertyPostingKey(property.Key, property.Value);
                seeds.Add(
                    (
                        $"property:{property.Key}",
                        _propertyPostings.TryGetValue(key, out var postings)
                            ? postings
                            : []
                    )
                );
            }
        }

        if (query.WithinNodeId is { } withinNodeId)
        {
            if (!Document.TryGetNode(withinNodeId, out var scope) || scope is null)
            {
                throw new KeyNotFoundException(
                    $"Semantic scope node '{withinNodeId}' does not exist."
                );
            }

            var scopedPositions = scope.DescendantsAndSelf()
                .Select(node =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return _positions[node.Id];
                })
                .Order()
                .ToArray();
            seeds.Add(("subtree", scopedPositions));
        }

        var selected = seeds
            .OrderBy(seed => seed.Positions.Count)
            .ThenBy(seed => seed.Name, StringComparer.Ordinal)
            .FirstOrDefault();
        var candidatePositions = selected.Positions
            ?? Enumerable.Range(0, _nodes.Count).ToArray();
        var candidateNodes = candidatePositions.Select(position =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return _nodes[position];
        }).ToArray();
        return new WordSemanticIndexCandidateSet(
            new ReadOnlyCollection<WordSemanticNode>(candidateNodes),
            selected.Name ?? "all_nodes",
            candidateNodes.Length
        );
    }

    private static void AddPosting<TKey>(
        Dictionary<TKey, List<int>> postings,
        TKey key,
        int position
    )
        where TKey : notnull
    {
        if (!postings.TryGetValue(key, out var values))
        {
            values = [];
            postings.Add(key, values);
        }
        values.Add(position);
    }

    private static IReadOnlyDictionary<TKey, int[]> FreezePostings<TKey>(
        Dictionary<TKey, List<int>> values,
        IEqualityComparer<TKey>? comparer = null,
        CancellationToken cancellationToken = default
    )
        where TKey : notnull
    {
        var frozen = comparer is null
            ? new Dictionary<TKey, int[]>(values.Count)
            : new Dictionary<TKey, int[]>(values.Count, comparer);
        foreach (var item in values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            frozen.Add(item.Key, item.Value.ToArray());
        }
        return new ReadOnlyDictionary<TKey, int[]>(frozen);
    }

    private static string BuildFingerprint(
        WordSemanticDocument document,
        int nodeCount,
        int propertyOccurrenceCount
    )
    {
        var canonical = string.Join(
            '\0',
            FormatVersion,
            document.PackageFingerprint,
            nodeCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            propertyOccurrenceCount.ToString(
                System.Globalization.CultureInfo.InvariantCulture
            )
        );
        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonical))
        ).ToLowerInvariant();
    }

    private readonly record struct PropertyPostingKey(string Name, string Value);
}
