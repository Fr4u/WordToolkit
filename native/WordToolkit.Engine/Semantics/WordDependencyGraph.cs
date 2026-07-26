using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Resources;

namespace WordToolkit.Engine.Semantics;

public enum WordDependencyNodeKind
{
    Package,
    Part,
    ExternalTarget,
    SemanticNode,
    Style,
    AbstractNumbering,
    NumberingInstance,
    PictureBullet,
    Story,
    Bookmark,
    Field,
    ReferenceTarget,
    Section,
    Chart,
    ChartSeries,
    ChartAxis,
    Figure,
    FigureRepresentation,
    FigureShape,
    FigureResource,
    Caption,
    ContentControl,
    CustomXmlStore,
    CustomXmlBindingTarget,
    BibliographyCollection,
    BibliographySource,
    ActiveContentPayload,
    ActiveContentDeclaration,
    ActiveXControl,
    DocumentProperty,
    DocumentVariable,
    Diagram,
    DiagramPoint,
    MailMergeConfiguration,
    MailMergeDataSourceObject,
    MailMergeFieldMapping,
    MailMergeRecipientData,
    MailMergeRecipient,
    MailMergeField,
}

public enum WordDependencyEdgeKind
{
    PackageRelationship,
    PartContainsSemanticRoot,
    SemanticContainment,
    DefinesStyle,
    UsesStyle,
    DefaultStyle,
    StyleBasedOn,
    StyleNext,
    StyleLinked,
    DefinesAbstractNumbering,
    DefinesNumberingInstance,
    DefinesPictureBullet,
    NumberingInstanceUsesAbstract,
    NumberingLevelUsesStyle,
    AbstractNumberingUsesStyle,
    UsesNumbering,
    StyleUsesNumbering,
    NumberingUsesPictureBullet,
    PictureBulletRelationship,
    DefinesStory,
    StoryContainsBookmark,
    StoryContainsField,
    FieldContainsField,
    FieldReference,
    DefinesSection,
    SectionBindsStory,
    DefinesChart,
    ChartContainsSeries,
    ChartContainsAxis,
    ChartUsesPart,
    DefinesFigure,
    FigureHasRepresentation,
    FigureRepresentationContainsShape,
    FigureShapeContainsShape,
    FigureUsesResource,
    FigureResourceTargetsPart,
    DefinesCaption,
    FigureCaptionAssociation,
    DefinesContentControl,
    DefinesCustomXmlStore,
    ContentControlUsesStore,
    CustomXmlStoreContainsTarget,
    ContentControlBindsTarget,
    RepeatingSectionContainsItem,
    TableNestsTable,
    TableCellContinuesVerticalMerge,
    DefinesBibliographyCollection,
    BibliographyContainsSource,
    DefinesActiveContentPayload,
    ActiveContentRelationshipTargetsPayload,
    DefinesActiveContentDeclaration,
    ActiveContentDeclarationUsesPayload,
    DefinesActiveXControl,
    ActiveXControlUsesBinaryPayload,
    DefinesDocumentProperty,
    DefinesDocumentVariable,
    DefinesDiagram,
    DiagramContainsPoint,
    DiagramConnectsPoints,
    DiagramUsesPart,
    OutlineParent,
    OutlineLevelDerivedFromStyle,
    DefinesMailMergeConfiguration,
    MailMergeUsesDataSource,
    MailMergeUsesHeaderSource,
    MailMergeDefinesDataSourceObject,
    MailMergeDataSourceUsesPart,
    MailMergeDataSourceContainsMapping,
    MailMergeDataSourceUsesRecipientData,
    RecipientDataContainsRecipient,
    MailMergeContainsField,
    MailMergeFieldUsesMapping,
}

public enum WordDependencyIssueSeverity
{
    Info,
    Warning,
    Error,
}

public sealed record WordDependencyIssue(
    string Code,
    WordDependencyIssueSeverity Severity,
    string Message,
    string? NodeId = null,
    string? EdgeId = null,
    string? PartUri = null,
    int? SourceElementOrdinal = null
);

public sealed record WordDependencyNode(
    string Id,
    WordDependencyNodeKind Kind,
    string Key,
    bool IsResolved,
    bool IsExternal,
    bool IsPackageReachable,
    string? PartUri,
    int? SourceElementOrdinal,
    SemanticNodeId? SemanticNodeId,
    WordSemanticNodeKind? SemanticKind
);

public sealed record WordDependencyEdge(
    string Id,
    WordDependencyEdgeKind Kind,
    string SourceNodeId,
    string TargetNodeId,
    bool IsResolved,
    bool IsExternal,
    string? Qualifier,
    string? PartUri,
    int? SourceElementOrdinal,
    string? RelationshipId,
    string? RelationshipType
);

public sealed record WordDependencyCoverage(
    bool PackageRelationships,
    bool SemanticContainment,
    bool Styles,
    bool Numbering,
    bool References,
    bool Sections,
    bool Charts,
    bool FiguresAndCaptions,
    bool ContentControlsAndCustomXml,
    bool TablesAndCellTopology,
    bool BibliographySources,
    bool ActiveContent,
    bool DocumentPropertiesAndVariables,
    bool SmartArtDiagrams,
    bool HeadingsAndOutline,
    bool MailMerge,
    IReadOnlyList<string> ExplicitlyUnmodeledDomains
);

public sealed record WordDependencyResourceUsage(
    string AccountingModel,
    long AccountedBytes,
    long MaximumAccountedBytes,
    int NodeCount,
    int EdgeCount,
    int IssueCount,
    long AdjacencyIndexBytes
);

public readonly struct WordDependencyEdgeCollection : IReadOnlyList<WordDependencyEdge>
{
    private readonly IReadOnlyList<WordDependencyEdge>? _edges;
    private readonly int[]? _edgeIndexes;
    private readonly int _offset;

    internal WordDependencyEdgeCollection(
        IReadOnlyList<WordDependencyEdge> edges,
        int[] edgeIndexes,
        int offset,
        int count
    )
    {
        _edges = edges;
        _edgeIndexes = edgeIndexes;
        _offset = offset;
        Count = count;
    }

    public int Count { get; }

    public WordDependencyEdge this[int index]
    {
        get
        {
            if ((uint)index >= (uint)Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }
            return _edges![_edgeIndexes![_offset + index]];
        }
    }

    public Enumerator GetEnumerator() => new(this);

    IEnumerator<WordDependencyEdge> IEnumerable<WordDependencyEdge>.GetEnumerator() =>
        GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
        GetEnumerator();

    public struct Enumerator : IEnumerator<WordDependencyEdge>
    {
        private readonly WordDependencyEdgeCollection _collection;
        private int _index;

        internal Enumerator(WordDependencyEdgeCollection collection)
        {
            _collection = collection;
            _index = -1;
        }

        public WordDependencyEdge Current =>
            _index >= 0 && _index < _collection.Count
                ? _collection[_index]
                : throw new InvalidOperationException(
                    "The dependency-edge enumerator is not positioned on an item."
                );

        object System.Collections.IEnumerator.Current => Current;

        public bool MoveNext()
        {
            var next = _index + 1;
            if (next >= _collection.Count)
            {
                _index = _collection.Count;
                return false;
            }
            _index = next;
            return true;
        }

        public void Reset() => _index = -1;

        public void Dispose() { }
    }
}

public sealed class WordDependencyGraph
{
    private readonly IReadOnlyDictionary<string, int> _nodeIndexById;
    private readonly int[] _incomingOffsets;
    private readonly int[] _incomingEdgeIndexes;
    private readonly int[] _outgoingOffsets;
    private readonly int[] _outgoingEdgeIndexes;

    internal WordDependencyGraph(
        string packageFingerprint,
        string mainPartUri,
        IReadOnlyList<WordDependencyNode> nodes,
        IReadOnlyList<WordDependencyEdge> edges,
        IReadOnlyList<WordDependencyIssue> issues,
        WordDependencyCoverage coverage,
        WordDependencyResourceUsage resourceUsage,
        WordOperationResourceLease? operationResourceLease,
        CancellationToken cancellationToken,
        int packageDiagnosticCount,
        int styleIssueCount,
        int numberingIssueCount,
        int referenceIssueCount,
        int unboundSectionStoryCount,
        int chartIssueCount,
        int figureIssueCount,
        int contentControlIssueCount,
        int tableIssueCount,
        int bibliographyIssueCount,
        int activeContentIssueCount,
        int documentPropertyIssueCount,
        int settingsIssueCount,
        int diagramIssueCount,
        int outlineIssueCount,
        int mailMergeIssueCount
    )
    {
        var nodeArray = nodes as WordDependencyNode[] ?? nodes.ToArray();
        var edgeArray = edges as WordDependencyEdge[] ?? edges.ToArray();
        var issueArray = issues as WordDependencyIssue[] ?? issues.ToArray();
        PackageFingerprint = packageFingerprint;
        MainPartUri = mainPartUri;
        Nodes = new ReadOnlyCollection<WordDependencyNode>(nodeArray);
        Edges = new ReadOnlyCollection<WordDependencyEdge>(edgeArray);
        Issues = new ReadOnlyCollection<WordDependencyIssue>(issueArray);
        Coverage = coverage;
        ResourceUsage = resourceUsage;
        PackageDiagnosticCount = packageDiagnosticCount;
        StyleIssueCount = styleIssueCount;
        NumberingIssueCount = numberingIssueCount;
        ReferenceIssueCount = referenceIssueCount;
        UnboundSectionStoryCount = unboundSectionStoryCount;
        ChartIssueCount = chartIssueCount;
        FigureIssueCount = figureIssueCount;
        ContentControlIssueCount = contentControlIssueCount;
        TableIssueCount = tableIssueCount;
        BibliographyIssueCount = bibliographyIssueCount;
        ActiveContentIssueCount = activeContentIssueCount;
        DocumentPropertyIssueCount = documentPropertyIssueCount;
        SettingsIssueCount = settingsIssueCount;
        DiagramIssueCount = diagramIssueCount;
        OutlineIssueCount = outlineIssueCount;
        MailMergeIssueCount = mailMergeIssueCount;
        var nodeIndexes = new Dictionary<string, int>(nodeArray.Length, StringComparer.Ordinal);
        for (var index = 0; index < nodeArray.Length; index++)
        {
            if ((index & 4095) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
            if (!nodeIndexes.TryAdd(nodeArray[index].Id, index))
            {
                throw new WordDependencyProjectionException(
                    "Dependency graph contains a duplicate node ID."
                );
            }
        }
        _nodeIndexById = new ReadOnlyDictionary<string, int>(nodeIndexes);
        (_incomingOffsets, _incomingEdgeIndexes) = BuildAdjacency(
            nodeIndexes,
            edgeArray,
            incoming: true,
            cancellationToken
        );
        (_outgoingOffsets, _outgoingEdgeIndexes) = BuildAdjacency(
            nodeIndexes,
            edgeArray,
            incoming: false,
            cancellationToken
        );
        OperationResourceUsage = operationResourceLease?.Snapshot();
    }

    public string PackageFingerprint { get; }

    public string MainPartUri { get; }

    public IReadOnlyList<WordDependencyNode> Nodes { get; }

    public IReadOnlyList<WordDependencyEdge> Edges { get; }

    public IReadOnlyList<WordDependencyIssue> Issues { get; }

    public WordDependencyCoverage Coverage { get; }

    public WordDependencyResourceUsage ResourceUsage { get; }

    public WordOperationResourceUsage? OperationResourceUsage { get; }

    public int PackageDiagnosticCount { get; }

    public int StyleIssueCount { get; }

    public int NumberingIssueCount { get; }

    public int ReferenceIssueCount { get; }

    public int UnboundSectionStoryCount { get; }

    public int ChartIssueCount { get; }

    public int FigureIssueCount { get; }

    public int ContentControlIssueCount { get; }

    public int TableIssueCount { get; }

    public int BibliographyIssueCount { get; }

    public int ActiveContentIssueCount { get; }

    public int DocumentPropertyIssueCount { get; }

    public int SettingsIssueCount { get; }

    public int DiagramIssueCount { get; }

    public int OutlineIssueCount { get; }

    public int MailMergeIssueCount { get; }

    public bool TryGetNode(string nodeId, out WordDependencyNode? node)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
        if (_nodeIndexById.TryGetValue(nodeId, out var index))
        {
            node = Nodes[index];
            return true;
        }
        node = null;
        return false;
    }

    public IReadOnlyList<WordDependencyEdge> Incoming(string nodeId) =>
        IncomingView(nodeId);

    public WordDependencyEdgeCollection IncomingView(string nodeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
        return _nodeIndexById.TryGetValue(nodeId, out var nodeIndex)
            ? EdgeCollection(_incomingOffsets, _incomingEdgeIndexes, nodeIndex)
            : default;
    }

    public IReadOnlyList<WordDependencyEdge> Outgoing(string nodeId) =>
        OutgoingView(nodeId);

    public WordDependencyEdgeCollection OutgoingView(string nodeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
        return _nodeIndexById.TryGetValue(nodeId, out var nodeIndex)
            ? EdgeCollection(_outgoingOffsets, _outgoingEdgeIndexes, nodeIndex)
            : default;
    }

    private WordDependencyEdgeCollection EdgeCollection(
        int[] offsets,
        int[] edgeIndexes,
        int nodeIndex
    ) => new(
        Edges,
        edgeIndexes,
        offsets[nodeIndex],
        offsets[nodeIndex + 1] - offsets[nodeIndex]
    );

    private static (int[] Offsets, int[] EdgeIndexes) BuildAdjacency(
        IReadOnlyDictionary<string, int> nodeIndexes,
        IReadOnlyList<WordDependencyEdge> edges,
        bool incoming,
        CancellationToken cancellationToken
    )
    {
        var offsets = new int[nodeIndexes.Count + 1];
        for (var edgeIndex = 0; edgeIndex < edges.Count; edgeIndex++)
        {
            if ((edgeIndex & 4095) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
            var edge = edges[edgeIndex];
            var nodeId = incoming ? edge.TargetNodeId : edge.SourceNodeId;
            if (!nodeIndexes.TryGetValue(nodeId, out var nodeIndex))
            {
                throw new WordDependencyProjectionException(
                    "Dependency graph contains an edge with a missing endpoint."
                );
            }
            offsets[nodeIndex + 1]++;
        }
        for (var index = 1; index < offsets.Length; index++)
        {
            if ((index & 4095) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
            offsets[index] += offsets[index - 1];
        }

        var edgeIndexes = new int[edges.Count];
        var cursors = (int[])offsets.Clone();
        for (var edgeIndex = 0; edgeIndex < edges.Count; edgeIndex++)
        {
            if ((edgeIndex & 4095) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
            var edge = edges[edgeIndex];
            var nodeId = incoming ? edge.TargetNodeId : edge.SourceNodeId;
            var nodeIndex = nodeIndexes[nodeId];
            edgeIndexes[cursors[nodeIndex]++] = edgeIndex;
        }

        var comparer = Comparer<int>.Create((left, right) =>
        {
            var kind = edges[left].Kind.CompareTo(edges[right].Kind);
            return kind != 0
                ? kind
                : StringComparer.Ordinal.Compare(edges[left].Id, edges[right].Id);
        });
        for (var nodeIndex = 0; nodeIndex < nodeIndexes.Count; nodeIndex++)
        {
            if ((nodeIndex & 4095) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
            var offset = offsets[nodeIndex];
            var count = offsets[nodeIndex + 1] - offset;
            if (count > 1)
            {
                SortAdjacencyRange(
                    edgeIndexes,
                    offset,
                    count,
                    comparer,
                    cancellationToken
                );
            }
        }
        return (offsets, edgeIndexes);
    }

    private static void SortAdjacencyRange(
        int[] edgeIndexes,
        int offset,
        int count,
        IComparer<int> comparer,
        CancellationToken cancellationToken
    )
    {
        const int maximumNonCancellableSort = 4_096;
        cancellationToken.ThrowIfCancellationRequested();
        if (count <= maximumNonCancellableSort)
        {
            Array.Sort(edgeIndexes, offset, count, comparer);
            cancellationToken.ThrowIfCancellationRequested();
            return;
        }

        // Array.Sort cannot observe a CancellationToken. Use an in-place heap sort only
        // for very high-degree nodes so cancellation stays bounded without allocating a
        // second segment-sized scratch array.
        for (var root = count / 2 - 1; root >= 0; root--)
        {
            if ((root & 1023) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
            SiftDown(edgeIndexes, offset, root, count, comparer);
        }
        for (var end = count - 1; end > 0; end--)
        {
            if ((end & 1023) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
            (edgeIndexes[offset], edgeIndexes[offset + end]) = (
                edgeIndexes[offset + end],
                edgeIndexes[offset]
            );
            SiftDown(edgeIndexes, offset, 0, end, comparer);
        }
        cancellationToken.ThrowIfCancellationRequested();
    }

    private static void SiftDown(
        int[] edgeIndexes,
        int offset,
        int root,
        int count,
        IComparer<int> comparer
    )
    {
        while (true)
        {
            var child = checked(root * 2 + 1);
            if (child >= count)
            {
                return;
            }
            var candidate = root;
            if (
                comparer.Compare(
                    edgeIndexes[offset + candidate],
                    edgeIndexes[offset + child]
                ) < 0
            )
            {
                candidate = child;
            }
            if (
                child + 1 < count
                && comparer.Compare(
                    edgeIndexes[offset + candidate],
                    edgeIndexes[offset + child + 1]
                ) < 0
            )
            {
                candidate = child + 1;
            }
            if (candidate == root)
            {
                return;
            }
            (edgeIndexes[offset + root], edgeIndexes[offset + candidate]) = (
                edgeIndexes[offset + candidate],
                edgeIndexes[offset + root]
            );
            root = candidate;
        }
    }
}

public sealed record WordDependencyGraphOptions
{
    public static WordDependencyGraphOptions Default { get; } = new();

    public int MaxNodes { get; init; } = 1_000_000;

    public int MaxEdges { get; init; } = 2_000_000;

    public int MaxIssues { get; init; } = 10_000;

    public int MaxKeyCharacters { get; init; } = 65_536;

    public int MaxMetadataCharacters { get; init; } = 65_536;

    public long MaxAccountedBytes { get; init; } = 128L * 1024 * 1024;

    internal void Validate()
    {
        if (MaxNodes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxNodes));
        }
        if (MaxEdges <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxEdges));
        }
        if (MaxIssues <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxIssues));
        }
        if (MaxKeyCharacters <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxKeyCharacters));
        }
        if (MaxMetadataCharacters <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxMetadataCharacters));
        }
        if (MaxAccountedBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxAccountedBytes));
        }
    }
}

public sealed class WordDependencyGraphBuilder
{
    private static readonly IReadOnlyList<string> ExplicitlyUnmodeledDomains =
        new ReadOnlyCollection<string>(
            [
                "drawingml_vml_rendered_geometry_and_layout_execution",
                "active_content_binary_internals_and_execution",
                "signature_cryptographic_validation_and_resigning",
                "encrypted_package_adapter",
                "coauthoring_sessions",
            ]
        );

    private readonly WordDependencyGraphOptions _options;
    private readonly WordOperationResourceLease? _resourceLease;

    public WordDependencyGraphBuilder(WordDependencyGraphOptions? options = null)
    {
        _options = options ?? WordDependencyGraphOptions.Default;
        _options.Validate();
    }

    public WordDependencyGraphBuilder(
        WordDependencyGraphOptions? options,
        WordOperationResourceLease resourceLease
    )
    {
        ArgumentNullException.ThrowIfNull(resourceLease);
        _options = options ?? WordDependencyGraphOptions.Default;
        _resourceLease = resourceLease;
        _options.Validate();
    }

    public WordDependencyGraph Build(
        OpcPackageSnapshot package,
        WordSemanticDocument semanticDocument,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(semanticDocument);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureFingerprint(package.Fingerprint, semanticDocument.PackageFingerprint);
        var styles = (_resourceLease is null
            ? new WordStyleGraphBuilder()
            : new WordStyleGraphBuilder(null, _resourceLease)).Build(
            package,
            semanticDocument,
            cancellationToken
        );
        var numbering = (_resourceLease is null
            ? new WordNumberingGraphBuilder()
            : new WordNumberingGraphBuilder(null, _resourceLease)).Build(
            package,
            semanticDocument,
            styles,
            cancellationToken
        );
        var references = (_resourceLease is null
            ? new WordReferenceGraphBuilder()
            : new WordReferenceGraphBuilder(null, _resourceLease)).Build(
            package,
            semanticDocument,
            cancellationToken
        );
        var sections = (_resourceLease is null
            ? new WordSectionGraphBuilder()
            : new WordSectionGraphBuilder(null, _resourceLease)).Build(
            package,
            semanticDocument,
            cancellationToken
        );
        var charts = (_resourceLease is null
            ? new WordChartGraphBuilder()
            : new WordChartGraphBuilder(null, _resourceLease)).Build(
            package,
            cancellationToken
        );
        return Build(
            package,
            semanticDocument,
            styles,
            numbering,
            references,
            sections,
            charts,
            cancellationToken
        );
    }

    public WordDependencyGraph Build(
        OpcPackageSnapshot package,
        WordSemanticDocument semanticDocument,
        WordStyleGraph styles,
        WordNumberingGraph numbering,
        WordReferenceGraph references,
        WordSectionGraph sections,
        CancellationToken cancellationToken = default
    ) => Build(
        package,
        semanticDocument,
        styles,
        numbering,
        references,
        sections,
        (_resourceLease is null
            ? new WordChartGraphBuilder()
            : new WordChartGraphBuilder(null, _resourceLease)).Build(
            package,
            cancellationToken
        ),
        cancellationToken
    );

    public WordDependencyGraph Build(
        OpcPackageSnapshot package,
        WordSemanticDocument semanticDocument,
        WordStyleGraph styles,
        WordNumberingGraph numbering,
        WordReferenceGraph references,
        WordSectionGraph sections,
        WordChartGraph charts,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(semanticDocument);
        ArgumentNullException.ThrowIfNull(styles);
        ArgumentNullException.ThrowIfNull(numbering);
        ArgumentNullException.ThrowIfNull(references);
        ArgumentNullException.ThrowIfNull(sections);
        ArgumentNullException.ThrowIfNull(charts);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureFingerprint(
            package.Fingerprint,
            semanticDocument.PackageFingerprint,
            styles.PackageFingerprint,
            numbering.PackageFingerprint,
            references.PackageFingerprint,
            sections.PackageFingerprint,
            charts.PackageFingerprint
        );
        var contentControls = (_resourceLease is null
            ? new WordContentControlBindingGraphBuilder()
            : new WordContentControlBindingGraphBuilder(null, _resourceLease)).Build(
            package,
            semanticDocument,
            cancellationToken
        );
        return Build(
            package,
            semanticDocument,
            styles,
            numbering,
            references,
            sections,
            charts,
            contentControls,
            cancellationToken
        );
    }

    public WordDependencyGraph Build(
        OpcPackageSnapshot package,
        WordSemanticDocument semanticDocument,
        WordStyleGraph styles,
        WordNumberingGraph numbering,
        WordReferenceGraph references,
        WordSectionGraph sections,
        WordChartGraph charts,
        WordContentControlBindingGraph contentControls,
        CancellationToken cancellationToken = default
    )
    {
        var tables = (_resourceLease is null
            ? new WordTableGraphBuilder()
            : new WordTableGraphBuilder(null, _resourceLease)).Build(
            package,
            semanticDocument,
            cancellationToken
        );
        return Build(
            package,
            semanticDocument,
            styles,
            numbering,
            references,
            sections,
            charts,
            contentControls,
            tables,
            cancellationToken
        );
    }

    public WordDependencyGraph Build(
        OpcPackageSnapshot package,
        WordSemanticDocument semanticDocument,
        WordStyleGraph styles,
        WordNumberingGraph numbering,
        WordReferenceGraph references,
        WordSectionGraph sections,
        WordChartGraph charts,
        WordContentControlBindingGraph contentControls,
        WordTableGraph tables,
        CancellationToken cancellationToken = default
    ) => BuildCore(
        package,
        semanticDocument,
        styles,
        numbering,
        references,
        sections,
        charts,
        contentControls,
        tables,
        suppliedMailMerge: null,
        cancellationToken
    );

    public WordDependencyGraph Build(
        OpcPackageSnapshot package,
        WordSemanticDocument semanticDocument,
        WordStyleGraph styles,
        WordNumberingGraph numbering,
        WordReferenceGraph references,
        WordSectionGraph sections,
        WordChartGraph charts,
        WordContentControlBindingGraph contentControls,
        WordTableGraph tables,
        WordMailMergeGraph mailMerge,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(mailMerge);
        return BuildCore(
            package,
            semanticDocument,
            styles,
            numbering,
            references,
            sections,
            charts,
            contentControls,
            tables,
            mailMerge,
            cancellationToken
        );
    }

    private WordDependencyGraph BuildCore(
        OpcPackageSnapshot package,
        WordSemanticDocument semanticDocument,
        WordStyleGraph styles,
        WordNumberingGraph numbering,
        WordReferenceGraph references,
        WordSectionGraph sections,
        WordChartGraph charts,
        WordContentControlBindingGraph contentControls,
        WordTableGraph tables,
        WordMailMergeGraph? suppliedMailMerge,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(semanticDocument);
        ArgumentNullException.ThrowIfNull(styles);
        ArgumentNullException.ThrowIfNull(numbering);
        ArgumentNullException.ThrowIfNull(references);
        ArgumentNullException.ThrowIfNull(sections);
        ArgumentNullException.ThrowIfNull(charts);
        ArgumentNullException.ThrowIfNull(contentControls);
        ArgumentNullException.ThrowIfNull(tables);
        cancellationToken.ThrowIfCancellationRequested();
        var figures = (_resourceLease is null
            ? new WordFigureCaptionGraphBuilder()
            : new WordFigureCaptionGraphBuilder(null, _resourceLease)).Build(
            package,
            semanticDocument,
            references,
            styles,
            cancellationToken
        );
        var bibliography = (_resourceLease is null
            ? new WordBibliographyGraphBuilder()
            : new WordBibliographyGraphBuilder(null, _resourceLease)).Build(
            package,
            cancellationToken
        );
        var activeContent = (_resourceLease is null
            ? new WordActiveContentGraphBuilder()
            : new WordActiveContentGraphBuilder(null, _resourceLease)).Build(
            package,
            cancellationToken
        );
        var documentProperties = (_resourceLease is null
            ? new WordDocumentPropertyGraphBuilder()
            : new WordDocumentPropertyGraphBuilder(null, _resourceLease)).Build(
            package,
            cancellationToken
        );
        var settings = (_resourceLease is null
            ? new WordSettingsGraphBuilder()
            : new WordSettingsGraphBuilder(null, _resourceLease)).Build(
            package,
            semanticDocument,
            cancellationToken
        );
        var diagrams = (_resourceLease is null
            ? new WordDiagramGraphBuilder()
            : new WordDiagramGraphBuilder(null, _resourceLease)).Build(
            package,
            cancellationToken
        );
        var outline = (_resourceLease is null
            ? new WordOutlineGraphBuilder()
            : new WordOutlineGraphBuilder(null, _resourceLease)).Build(
            package,
            semanticDocument,
            styles,
            cancellationToken
        );
        var mailMerge = suppliedMailMerge ?? (_resourceLease is null
                ? new WordMailMergeGraphBuilder()
                : new WordMailMergeGraphBuilder(null, _resourceLease)).Build(
                package,
                semanticDocument,
                settings,
                references,
                cancellationToken
            );
        EnsureFingerprint(
            package.Fingerprint,
            semanticDocument.PackageFingerprint,
            styles.PackageFingerprint,
            numbering.PackageFingerprint,
            references.PackageFingerprint,
            sections.PackageFingerprint,
            charts.PackageFingerprint,
            figures.PackageFingerprint,
            contentControls.PackageFingerprint,
            tables.PackageFingerprint,
            bibliography.PackageFingerprint,
            activeContent.PackageFingerprint,
            documentProperties.PackageFingerprint,
            settings.PackageFingerprint,
            diagrams.PackageFingerprint,
            outline.PackageFingerprint,
            mailMerge.PackageFingerprint
        );

        var state = new BuildState(_options, _resourceLease);
        var reachableParts = PackageReachableParts(package, cancellationToken);
        var packageNodeId = state.AddNode(
            WordDependencyNodeKind.Package,
            package.Fingerprint,
            isResolved: true,
            isExternal: false,
            isPackageReachable: true
        );

        AddPackageDependencies(
            state,
            package,
            packageNodeId,
            reachableParts,
            cancellationToken
        );
        AddSemanticDependencies(
            state,
            package,
            semanticDocument,
            reachableParts,
            cancellationToken
        );
        AddStyleDependencies(
            state,
            package,
            semanticDocument,
            styles,
            packageNodeId,
            reachableParts,
            cancellationToken
        );
        AddNumberingDependencies(
            state,
            package,
            semanticDocument,
            styles,
            numbering,
            reachableParts,
            cancellationToken
        );
        var bibliographySourceNodes = AddBibliographyDependencies(
            state,
            package,
            bibliography,
            reachableParts,
            cancellationToken
        );
        var documentMetadataNodes = AddDocumentPropertyDependencies(
            state,
            package,
            documentProperties,
            settings,
            reachableParts,
            cancellationToken
        );
        AddReferenceDependencies(
            state,
            package,
            styles,
            references,
            bibliography,
            bibliographySourceNodes,
            documentProperties,
            documentMetadataNodes,
            reachableParts,
            cancellationToken
        );
        AddSectionDependencies(
            state,
            package,
            semanticDocument,
            sections,
            reachableParts,
            cancellationToken
        );
        AddChartDependencies(
            state,
            package,
            charts,
            reachableParts,
            cancellationToken
        );
        AddDiagramDependencies(
            state,
            package,
            diagrams,
            reachableParts,
            cancellationToken
        );
        AddFigureDependencies(
            state,
            package,
            semanticDocument,
            figures,
            reachableParts,
            cancellationToken
        );
        AddContentControlDependencies(
            state,
            package,
            contentControls,
            reachableParts,
            cancellationToken
        );
        AddTableDependencies(
            state,
            semanticDocument,
            tables,
            reachableParts,
            cancellationToken
        );
        AddActiveContentDependencies(
            state,
            package,
            activeContent,
            packageNodeId,
            reachableParts,
            cancellationToken
        );
        AddMailMergeDependencies(
            state,
            package,
            semanticDocument,
            mailMerge,
            reachableParts,
            cancellationToken
        );
        AddOutlineDependencies(
            state,
            semanticDocument,
            styles,
            outline,
            reachableParts,
            cancellationToken
        );

        var (nodes, edges, issues, resourceUsage) = state.Materialize();
        return new WordDependencyGraph(
            package.Fingerprint,
            semanticDocument.MainPartUri,
            nodes,
            edges,
            issues,
            new WordDependencyCoverage(
                PackageRelationships: true,
                SemanticContainment: true,
                Styles: true,
                Numbering: true,
                References: true,
                Sections: true,
                Charts: true,
                FiguresAndCaptions: true,
                ContentControlsAndCustomXml: true,
                TablesAndCellTopology: true,
                BibliographySources: true,
                ActiveContent: true,
                DocumentPropertiesAndVariables: true,
                SmartArtDiagrams: true,
                HeadingsAndOutline: true,
                MailMerge: true,
                ExplicitlyUnmodeledDomains
            ),
            resourceUsage,
            _resourceLease,
            cancellationToken,
            package.Diagnostics.Count,
            styles.Issues.Count,
            numbering.Issues.Count,
            references.Issues.Count,
            sections.UnboundStoryPartUris.Count,
            charts.Issues.Count,
            figures.Issues.Count,
            contentControls.Issues.Count,
            tables.Issues.Count,
            bibliography.Issues.Count,
            activeContent.Issues.Count,
            documentProperties.Issues.Count,
            settings.Issues.Count,
            diagrams.Issues.Count,
            outline.Issues.Count,
            mailMerge.Issues.Count
        );
    }

    private static void AddMailMergeDependencies(
        BuildState state,
        OpcPackageSnapshot package,
        WordSemanticDocument semanticDocument,
        WordMailMergeGraph mailMerge,
        IReadOnlySet<string> reachableParts,
        CancellationToken cancellationToken
    )
    {
        var nodesBySubjectId = new Dictionary<string, string>(StringComparer.Ordinal);
        string? configurationNodeId = null;
        string? dataSourceObjectNodeId = null;

        string RelationshipTargetNode(WordMailMergeRelationship relationship)
        {
            if (relationship.ResolvedTargetPartUri is { } resolvedPartUri)
            {
                return PartNode(state, package, reachableParts, resolvedPartUri);
            }
            if (relationship.IsExternal)
            {
                return state.AddNode(
                    WordDependencyNodeKind.ExternalTarget,
                    relationship.Target ?? $"mail-merge:{relationship.RelationshipId}",
                    isResolved: false,
                    isExternal: true,
                    isPackageReachable: false
                );
            }
            return state.AddNode(
                WordDependencyNodeKind.Part,
                relationship.Target
                    ?? $"{relationship.SourcePartUri}#{relationship.RelationshipId}",
                isResolved: false,
                isExternal: false,
                isPackageReachable: false,
                partUri: relationship.ResolvedTargetPartUri
            );
        }

        void AddRelationshipEdge(
            WordMailMergeRelationship? relationship,
            string sourceNodeId,
            WordDependencyEdgeKind kind
        )
        {
            if (relationship is null)
            {
                return;
            }
            var targetNodeId = RelationshipTargetNode(relationship);
            nodesBySubjectId[relationship.Id] = targetNodeId;
            state.AddEdge(
                kind,
                sourceNodeId,
                targetNodeId,
                relationship.IsResolved,
                relationship.IsExternal,
                qualifier: relationship.Role.ToString().ToLowerInvariant(),
                partUri: relationship.SourcePartUri,
                sourceElementOrdinal: relationship.SourceElementOrdinal,
                relationshipId: relationship.RelationshipId,
                relationshipType: relationship.RelationshipType
            );
        }

        if (mailMerge.Configuration is { } configuration)
        {
            cancellationToken.ThrowIfCancellationRequested();
            configurationNodeId = state.AddNode(
                WordDependencyNodeKind.MailMergeConfiguration,
                configuration.Id,
                isResolved: true,
                isExternal: false,
                isPackageReachable: reachableParts.Contains(configuration.SettingsPartUri),
                partUri: configuration.SettingsPartUri,
                sourceElementOrdinal: configuration.SourceElementOrdinal
            );
            nodesBySubjectId[configuration.Id] = configurationNodeId;
            var settingsPartNodeId = PartNode(
                state,
                package,
                reachableParts,
                configuration.SettingsPartUri
            );
            state.AddEdge(
                WordDependencyEdgeKind.DefinesMailMergeConfiguration,
                settingsPartNodeId,
                configurationNodeId,
                isResolved: true,
                isExternal: false,
                partUri: configuration.SettingsPartUri,
                sourceElementOrdinal: configuration.SourceElementOrdinal
            );
            AddRelationshipEdge(
                configuration.DataSourceRelationship,
                configurationNodeId,
                WordDependencyEdgeKind.MailMergeUsesDataSource
            );
            AddRelationshipEdge(
                configuration.HeaderSourceRelationship,
                configurationNodeId,
                WordDependencyEdgeKind.MailMergeUsesHeaderSource
            );

            if (configuration.DataSourceObject is { } dataSourceObject)
            {
                dataSourceObjectNodeId = state.AddNode(
                    WordDependencyNodeKind.MailMergeDataSourceObject,
                    dataSourceObject.Id,
                    isResolved: true,
                    isExternal: false,
                    isPackageReachable: reachableParts.Contains(configuration.SettingsPartUri),
                    partUri: configuration.SettingsPartUri,
                    sourceElementOrdinal: dataSourceObject.SourceElementOrdinal
                );
                nodesBySubjectId[dataSourceObject.Id] = dataSourceObjectNodeId;
                state.AddEdge(
                    WordDependencyEdgeKind.MailMergeDefinesDataSourceObject,
                    configurationNodeId,
                    dataSourceObjectNodeId,
                    isResolved: true,
                    isExternal: false,
                    partUri: configuration.SettingsPartUri,
                    sourceElementOrdinal: dataSourceObject.SourceElementOrdinal
                );
                AddRelationshipEdge(
                    dataSourceObject.SourceRelationship,
                    dataSourceObjectNodeId,
                    WordDependencyEdgeKind.MailMergeDataSourceUsesPart
                );
            }
        }

        foreach (var mapping in mailMerge.Mappings)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var mappingNodeId = state.AddNode(
                WordDependencyNodeKind.MailMergeFieldMapping,
                mapping.Id,
                isResolved: true,
                isExternal: false,
                isPackageReachable: mailMerge.Configuration is { } currentConfiguration
                    && reachableParts.Contains(currentConfiguration.SettingsPartUri),
                partUri: mailMerge.Configuration?.SettingsPartUri,
                sourceElementOrdinal: mapping.SourceElementOrdinal
            );
            nodesBySubjectId[mapping.Id] = mappingNodeId;
            if (dataSourceObjectNodeId is not null)
            {
                state.AddEdge(
                    WordDependencyEdgeKind.MailMergeDataSourceContainsMapping,
                    dataSourceObjectNodeId,
                    mappingNodeId,
                    isResolved: true,
                    isExternal: false,
                    qualifier: mapping.Position.ToString(CultureInfo.InvariantCulture),
                    partUri: mailMerge.Configuration?.SettingsPartUri,
                    sourceElementOrdinal: mapping.SourceElementOrdinal
                );
            }
        }

        string? recipientDataNodeId = null;
        if (mailMerge.RecipientDataPart is { } recipientDataPart)
        {
            cancellationToken.ThrowIfCancellationRequested();
            recipientDataNodeId = state.AddNode(
                WordDependencyNodeKind.MailMergeRecipientData,
                recipientDataPart.Id,
                isResolved: true,
                isExternal: false,
                isPackageReachable: recipientDataPart.IsPackageReachable,
                partUri: recipientDataPart.PartUri,
                sourceElementOrdinal: recipientDataPart.SourceElementOrdinal
            );
            nodesBySubjectId[recipientDataPart.Id] = recipientDataNodeId;
            var recipientRelationship =
                mailMerge.Configuration?.DataSourceObject?.RecipientDataRelationship;
            if (dataSourceObjectNodeId is not null && recipientRelationship is not null)
            {
                nodesBySubjectId[recipientRelationship.Id] = recipientDataNodeId;
                state.AddEdge(
                    WordDependencyEdgeKind.MailMergeDataSourceUsesRecipientData,
                    dataSourceObjectNodeId,
                    recipientDataNodeId,
                    recipientRelationship.IsResolved,
                    isExternal: false,
                    qualifier: recipientRelationship.Role.ToString().ToLowerInvariant(),
                    partUri: recipientRelationship.SourcePartUri,
                    sourceElementOrdinal: recipientRelationship.SourceElementOrdinal,
                    relationshipId: recipientRelationship.RelationshipId,
                    relationshipType: recipientRelationship.RelationshipType
                );
            }
        }

        foreach (var recipient in mailMerge.Recipients)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var recipientNodeId = state.AddNode(
                WordDependencyNodeKind.MailMergeRecipient,
                recipient.Id,
                isResolved: recipient.IdentityKind is not WordMailMergeRecipientIdentityKind.Missing
                    and not WordMailMergeRecipientIdentityKind.Ambiguous,
                isExternal: false,
                isPackageReachable: mailMerge.RecipientDataPart?.IsPackageReachable == true,
                partUri: recipient.PartUri,
                sourceElementOrdinal: recipient.SourceElementOrdinal
            );
            nodesBySubjectId[recipient.Id] = recipientNodeId;
            if (recipientDataNodeId is not null)
            {
                state.AddEdge(
                    WordDependencyEdgeKind.RecipientDataContainsRecipient,
                    recipientDataNodeId,
                    recipientNodeId,
                    isResolved: true,
                    isExternal: false,
                    qualifier: recipient.Sequence.ToString(CultureInfo.InvariantCulture),
                    partUri: recipient.PartUri,
                    sourceElementOrdinal: recipient.SourceElementOrdinal
                );
            }
        }

        foreach (var field in mailMerge.Fields)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WordSemanticNode? semanticNode = null;
            if (field.SemanticNodeId is { } semanticNodeId)
            {
                semanticDocument.TryGetNode(semanticNodeId, out semanticNode);
            }
            var fieldNodeId = state.AddNode(
                WordDependencyNodeKind.MailMergeField,
                field.Id,
                isResolved: field.IsComplete,
                isExternal: false,
                isPackageReachable: reachableParts.Contains(field.PartUri),
                partUri: field.PartUri,
                sourceElementOrdinal: field.SourceElementOrdinal,
                semanticNodeId: semanticNode?.Id,
                semanticKind: semanticNode?.Kind
            );
            nodesBySubjectId[field.Id] = fieldNodeId;
            nodesBySubjectId[field.ReferenceFieldId] = fieldNodeId;
            var ownerNodeId = configurationNodeId;
            if (ownerNodeId is null)
            {
                ownerNodeId = semanticNode is null
                    ? PartNode(state, package, reachableParts, field.PartUri)
                    : SemanticNode(
                        state,
                        semanticNode,
                        reachableParts.Contains(semanticNode.SourcePartUri)
                    );
            }
            state.AddEdge(
                WordDependencyEdgeKind.MailMergeContainsField,
                ownerNodeId,
                fieldNodeId,
                isResolved: field.IsComplete,
                isExternal: false,
                qualifier: field.FieldType.ToLowerInvariant(),
                partUri: field.PartUri,
                sourceElementOrdinal: field.SourceElementOrdinal
            );
            foreach (var mappingId in field.MappingIds)
            {
                var resolved = nodesBySubjectId.TryGetValue(mappingId, out var mappingNodeId);
                mappingNodeId ??= state.AddNode(
                    WordDependencyNodeKind.MailMergeFieldMapping,
                    mappingId,
                    isResolved: false,
                    isExternal: false,
                    isPackageReachable: false
                );
                state.AddEdge(
                    WordDependencyEdgeKind.MailMergeFieldUsesMapping,
                    fieldNodeId,
                    mappingNodeId,
                    resolved,
                    isExternal: false,
                    qualifier: field.BindingStatus.ToString().ToLowerInvariant(),
                    partUri: field.PartUri,
                    sourceElementOrdinal: field.SourceElementOrdinal
                );
            }
        }

        foreach (var issue in mailMerge.Issues)
        {
            cancellationToken.ThrowIfCancellationRequested();
            nodesBySubjectId.TryGetValue(issue.SubjectId ?? string.Empty, out var nodeId);
            state.AddIssue(
                "WDG062",
                issue.Severity switch
                {
                    WordMailMergeIssueSeverity.Error => WordDependencyIssueSeverity.Error,
                    WordMailMergeIssueSeverity.Warning => WordDependencyIssueSeverity.Warning,
                    _ => WordDependencyIssueSeverity.Info,
                },
                $"{issue.Code}: {issue.Message}",
                nodeId: nodeId,
                partUri: issue.PartUri,
                sourceElementOrdinal: issue.SourceElementOrdinal
            );
        }
    }

    private static void AddOutlineDependencies(
        BuildState state,
        WordSemanticDocument semanticDocument,
        WordStyleGraph styles,
        WordOutlineGraph outline,
        IReadOnlySet<string> reachableParts,
        CancellationToken cancellationToken
    )
    {
        var semanticById = semanticDocument.Nodes.ToDictionary(node => node.Id);
        var stylesReachable = styles.StylesPartUri is { } stylesPartUri
            && reachableParts.Contains(stylesPartUri);
        foreach (var paragraph in outline.Paragraphs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!semanticById.TryGetValue(paragraph.ParagraphNodeId, out var semantic))
            {
                throw new WordDependencyProjectionException(
                    "Outline graph refers to a missing semantic paragraph."
                );
            }
            var paragraphNodeId = SemanticNode(
                state,
                semantic,
                reachableParts.Contains(semantic.SourcePartUri)
            );
            if (paragraph.LevelSourceStyleId is { } styleId)
            {
                var resolved = styles.TryGetStyle(styleId, out var style)
                    && style is not null
                    && style.Type == WordStyleType.Paragraph;
                var styleNodeId = StyleNode(
                    state,
                    styles,
                    styleId,
                    resolved,
                    stylesReachable
                );
                var edgeId = state.AddEdge(
                    WordDependencyEdgeKind.OutlineLevelDerivedFromStyle,
                    paragraphNodeId,
                    styleNodeId,
                    resolved,
                    isExternal: false,
                    qualifier: paragraph.Status == WordOutlineResolutionStatus.Heading
                        ? $"level:{paragraph.Level}"
                        : "body_text",
                    partUri: semantic.SourcePartUri,
                    sourceElementOrdinal: semantic.SourceElementOrdinal
                );
                if (!resolved)
                {
                    state.AddIssue(
                        "WDG060",
                        WordDependencyIssueSeverity.Error,
                        "An outline level refers to a missing or incompatible paragraph style.",
                        nodeId: paragraphNodeId,
                        edgeId: edgeId,
                        partUri: semantic.SourcePartUri,
                        sourceElementOrdinal: semantic.SourceElementOrdinal
                    );
                }
            }

            if (paragraph.ParentHeadingParagraphNodeId is not { } parentId)
            {
                continue;
            }
            if (!semanticById.TryGetValue(parentId, out var parent))
            {
                throw new WordDependencyProjectionException(
                    "Outline hierarchy refers to a missing parent paragraph."
                );
            }
            var parentNodeId = SemanticNode(
                state,
                parent,
                reachableParts.Contains(parent.SourcePartUri)
            );
            state.AddEdge(
                WordDependencyEdgeKind.OutlineParent,
                parentNodeId,
                paragraphNodeId,
                isResolved: true,
                isExternal: false,
                qualifier: paragraph.Level?.ToString(CultureInfo.InvariantCulture),
                partUri: semantic.SourcePartUri,
                sourceElementOrdinal: semantic.SourceElementOrdinal
            );
        }

        foreach (var issue in outline.Issues)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? nodeId = null;
            WordSemanticNode? issueParagraph = null;
            if (
                issue.ParagraphNodeId is { } paragraphId
                && semanticById.TryGetValue(paragraphId, out var paragraph)
            )
            {
                issueParagraph = paragraph;
                nodeId = SemanticNode(
                    state,
                    paragraph,
                    reachableParts.Contains(paragraph.SourcePartUri)
                );
            }
            state.AddIssue(
                "WDG061",
                issue.Severity switch
                {
                    WordOutlineIssueSeverity.Error => WordDependencyIssueSeverity.Error,
                    WordOutlineIssueSeverity.Warning => WordDependencyIssueSeverity.Warning,
                    _ => WordDependencyIssueSeverity.Info,
                },
                $"{issue.Code}: {PublicOutlineIssueMessage(issue)}",
                nodeId: nodeId,
                partUri: issueParagraph?.SourcePartUri,
                sourceElementOrdinal: issueParagraph?.SourceElementOrdinal
            );
        }
    }

    private static string PublicOutlineIssueMessage(WordOutlineIssue issue) =>
        issue.Code switch
        {
            "OUTLINE_LEVEL_UNRESOLVED" =>
                "A paragraph outline level could not be resolved from valid direct, style, or document-default evidence.",
            _ => issue.Message,
        };

    private static DocumentMetadataDependencyNodes AddDocumentPropertyDependencies(
        BuildState state,
        OpcPackageSnapshot package,
        WordDocumentPropertyGraph documentProperties,
        WordSettingsGraph settings,
        IReadOnlySet<string> reachableParts,
        CancellationToken cancellationToken
    )
    {
        var propertyNodeIds = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in documentProperties.Properties)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var partNodeId = PartNode(
                state,
                package,
                reachableParts,
                property.PartUri
            );
            var resolved = property.IsStructurallyValid
                && property.IsUniquelyNamed
                && property.IsPackageReachable;
            var propertyNodeId = state.AddNode(
                WordDependencyNodeKind.DocumentProperty,
                property.Id,
                resolved,
                isExternal: false,
                property.IsPackageReachable,
                partUri: property.PartUri,
                sourceElementOrdinal: property.SourceElementOrdinal
            );
            propertyNodeIds.Add(property.Id, propertyNodeId);
            state.AddEdge(
                WordDependencyEdgeKind.DefinesDocumentProperty,
                partNodeId,
                propertyNodeId,
                resolved,
                isExternal: false,
                qualifier: string.Join(
                    ':',
                    property.Family.ToString().ToLowerInvariant(),
                    property.ValueKind.ToString().ToLowerInvariant()
                ),
                partUri: property.PartUri,
                sourceElementOrdinal: property.SourceElementOrdinal
            );
        }

        var variableNodeIds = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase
        );
        if (settings.SettingsPartUri is { } settingsPartUri)
        {
            var partNodeId = PartNode(
                state,
                package,
                reachableParts,
                settingsPartUri
            );
            var nameCounts = settings.DocumentVariables
                .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(item => item.Key, item => item.Count(), StringComparer.OrdinalIgnoreCase);
            foreach (var variable in settings.DocumentVariables)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var resolved = !string.IsNullOrWhiteSpace(variable.Name)
                    && nameCounts[variable.Name] == 1;
                var variableNodeId = state.AddNode(
                    WordDependencyNodeKind.DocumentVariable,
                    string.Join(
                        ':',
                        settingsPartUri,
                        variable.SourceElementOrdinal.ToString(
                            CultureInfo.InvariantCulture
                        ),
                        variable.Name
                    ),
                    resolved,
                    isExternal: false,
                    isPackageReachable: reachableParts.Contains(settingsPartUri),
                    partUri: settingsPartUri,
                    sourceElementOrdinal: variable.SourceElementOrdinal
                );
                state.AddEdge(
                    WordDependencyEdgeKind.DefinesDocumentVariable,
                    partNodeId,
                    variableNodeId,
                    resolved,
                    isExternal: false,
                    partUri: settingsPartUri,
                    sourceElementOrdinal: variable.SourceElementOrdinal
                );
                if (resolved)
                {
                    variableNodeIds.Add(variable.Name, variableNodeId);
                }
                else
                {
                    state.AddIssue(
                        "WDG070",
                        WordDependencyIssueSeverity.Warning,
                        "A document-variable name is missing or ambiguous.",
                        nodeId: variableNodeId,
                        partUri: settingsPartUri,
                        sourceElementOrdinal: variable.SourceElementOrdinal
                    );
                }
            }
        }

        foreach (var issue in documentProperties.Issues)
        {
            state.AddIssue(
                $"WDP:{issue.Code}",
                issue.Severity switch
                {
                    WordDocumentPropertyIssueSeverity.Error =>
                        WordDependencyIssueSeverity.Error,
                    WordDocumentPropertyIssueSeverity.Warning =>
                        WordDependencyIssueSeverity.Warning,
                    _ => WordDependencyIssueSeverity.Info,
                },
                issue.Message,
                nodeId: issue.PropertyId is not null
                    && propertyNodeIds.TryGetValue(
                        issue.PropertyId,
                        out var propertyNodeId
                    )
                        ? propertyNodeId
                        : null,
                partUri: issue.PartUri,
                sourceElementOrdinal: issue.SourceElementOrdinal
            );
        }
        return new DocumentMetadataDependencyNodes(
            propertyNodeIds,
            variableNodeIds
        );
    }

    private sealed record DocumentMetadataDependencyNodes(
        IReadOnlyDictionary<string, string> PropertiesById,
        IReadOnlyDictionary<string, string> VariablesByName
    );

    private static void AddActiveContentDependencies(
        BuildState state,
        OpcPackageSnapshot package,
        WordActiveContentGraph activeContent,
        string packageNodeId,
        IReadOnlySet<string> reachableParts,
        CancellationToken cancellationToken
    )
    {
        var payloadNodeIds = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var payload in activeContent.Payloads)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var partNodeId = PartNode(
                state,
                package,
                reachableParts,
                payload.PartUri
            );
            var payloadNodeId = state.AddNode(
                WordDependencyNodeKind.ActiveContentPayload,
                payload.Id,
                isResolved: true,
                isExternal: false,
                isPackageReachable: payload.IsPackageReachable,
                partUri: payload.PartUri
            );
            payloadNodeIds.Add(payload.Id, payloadNodeId);
            state.AddEdge(
                WordDependencyEdgeKind.DefinesActiveContentPayload,
                partNodeId,
                payloadNodeId,
                isResolved: true,
                isExternal: false,
                qualifier: payload.Kind.ToString().ToLowerInvariant(),
                partUri: payload.PartUri
            );
        }

        var relationshipsById = activeContent.Relationships
            .GroupBy(item => item.Id, StringComparer.Ordinal)
            .ToDictionary(item => item.Key, item => item.First(), StringComparer.Ordinal);
        var relationshipEdgeIds = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var relationship in activeContent.Relationships)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourceNodeId = relationship.SourcePartUri == "/"
                ? packageNodeId
                : PartNode(
                    state,
                    package,
                    reachableParts,
                    relationship.SourcePartUri
                );
            var targetNodeId = ActiveContentTargetNode(
                state,
                package,
                reachableParts,
                relationship,
                payloadNodeIds
            );
            var edgeId = state.AddEdge(
                WordDependencyEdgeKind.ActiveContentRelationshipTargetsPayload,
                sourceNodeId,
                targetNodeId,
                relationship.IsResolved,
                relationship.TargetMode == OpcRelationshipTargetMode.External,
                qualifier: relationship.Role.ToString().ToLowerInvariant(),
                partUri: relationship.SourcePartUri == "/"
                    ? null
                    : relationship.SourcePartUri,
                relationshipId: relationship.RelationshipId,
                relationshipType: relationship.RelationshipType
            );
            relationshipEdgeIds[relationship.Id] = edgeId;
        }

        var declarationNodeIds = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var declaration in activeContent.Declarations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var partNodeId = PartNode(
                state,
                package,
                reachableParts,
                declaration.SourcePartUri
            );
            var declarationNodeId = state.AddNode(
                WordDependencyNodeKind.ActiveContentDeclaration,
                declaration.Id,
                declaration.IsResolved,
                isExternal: false,
                isPackageReachable: reachableParts.Contains(declaration.SourcePartUri),
                partUri: declaration.SourcePartUri,
                sourceElementOrdinal: declaration.SourceElementOrdinal
            );
            declarationNodeIds.Add(declaration.Id, declarationNodeId);
            state.AddEdge(
                WordDependencyEdgeKind.DefinesActiveContentDeclaration,
                partNodeId,
                declarationNodeId,
                isResolved: true,
                isExternal: false,
                qualifier: declaration.Kind.ToString().ToLowerInvariant(),
                partUri: declaration.SourcePartUri,
                sourceElementOrdinal: declaration.SourceElementOrdinal
            );

            string targetNodeId;
            WordActiveContentRelationship? relationship = null;
            if (
                declaration.RelationshipNodeId is not null
                && relationshipsById.TryGetValue(
                    declaration.RelationshipNodeId,
                    out relationship
                )
            )
            {
                targetNodeId = ActiveContentTargetNode(
                    state,
                    package,
                    reachableParts,
                    relationship,
                    payloadNodeIds
                );
            }
            else
            {
                targetNodeId = state.AddNode(
                    WordDependencyNodeKind.ActiveContentPayload,
                    "unresolved:" + declaration.Id,
                    isResolved: false,
                    isExternal: false,
                    isPackageReachable: false
                );
            }
            state.AddEdge(
                WordDependencyEdgeKind.ActiveContentDeclarationUsesPayload,
                declarationNodeId,
                targetNodeId,
                relationship?.IsResolved == true,
                relationship?.TargetMode == OpcRelationshipTargetMode.External,
                qualifier: declaration.Kind.ToString().ToLowerInvariant(),
                partUri: declaration.SourcePartUri,
                sourceElementOrdinal: declaration.SourceElementOrdinal,
                relationshipId: declaration.RelationshipId,
                relationshipType: relationship?.RelationshipType
            );
        }

        var controlNodeIds = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var control in activeContent.Controls)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var owner = activeContent.Payloads.SingleOrDefault(payload =>
                payload.Kind == WordActiveContentPayloadKind.ActiveXXml
                && payload.PartUri == control.PartUri
            );
            if (
                owner is null
                || !payloadNodeIds.TryGetValue(owner.Id, out var ownerNodeId)
            )
            {
                throw new WordDependencyProjectionException(
                    "An ActiveX control has no source-linked XML persistence payload."
                );
            }
            var controlNodeId = state.AddNode(
                WordDependencyNodeKind.ActiveXControl,
                control.Id,
                control.IsResolved,
                isExternal: false,
                isPackageReachable: owner.IsPackageReachable,
                partUri: control.PartUri,
                sourceElementOrdinal: control.SourceElementOrdinal
            );
            controlNodeIds.Add(control.Id, controlNodeId);
            state.AddEdge(
                WordDependencyEdgeKind.DefinesActiveXControl,
                ownerNodeId,
                controlNodeId,
                isResolved: true,
                isExternal: false,
                qualifier: control.Persistence,
                partUri: control.PartUri,
                sourceElementOrdinal: control.SourceElementOrdinal
            );
            var binaryNodeId = control.BinaryPayloadId is not null
                && payloadNodeIds.TryGetValue(
                    control.BinaryPayloadId,
                    out var knownBinaryNodeId
                )
                    ? knownBinaryNodeId
                    : state.AddNode(
                        WordDependencyNodeKind.ActiveContentPayload,
                        "unresolved-binary:" + control.Id,
                        isResolved: false,
                        isExternal: false,
                        isPackageReachable: false
                    );
            state.AddEdge(
                WordDependencyEdgeKind.ActiveXControlUsesBinaryPayload,
                controlNodeId,
                binaryNodeId,
                control.IsResolved,
                isExternal: false,
                qualifier: "persistence_binary",
                partUri: control.PartUri,
                sourceElementOrdinal: control.SourceElementOrdinal,
                relationshipId: control.BinaryRelationshipId
            );
        }

        foreach (var issue in activeContent.Issues)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? nodeId = null;
            string? edgeId = null;
            if (issue.SubjectId is not null)
            {
                if (!payloadNodeIds.TryGetValue(issue.SubjectId, out nodeId)
                    && !declarationNodeIds.TryGetValue(issue.SubjectId, out nodeId)
                    && !controlNodeIds.TryGetValue(issue.SubjectId, out nodeId))
                {
                    relationshipEdgeIds.TryGetValue(issue.SubjectId, out edgeId);
                }
            }
            state.AddIssue(
                "WDG080_" + issue.Code,
                issue.Severity switch
                {
                    WordActiveContentIssueSeverity.Info => WordDependencyIssueSeverity.Info,
                    WordActiveContentIssueSeverity.Warning => WordDependencyIssueSeverity.Warning,
                    WordActiveContentIssueSeverity.Error => WordDependencyIssueSeverity.Error,
                    _ => throw new ArgumentOutOfRangeException(nameof(issue.Severity)),
                },
                $"The typed active-content graph emitted {issue.Code}.",
                nodeId,
                edgeId,
                issue.PartUri,
                issue.SourceElementOrdinal
            );
        }
        if (activeContent.IssuesTruncated)
        {
            state.AddIssue(
                "WDG080_ACTIVE_ISSUES_TRUNCATED",
                WordDependencyIssueSeverity.Warning,
                "The typed active-content graph truncated its diagnostic inventory."
            );
        }
    }

    private static string ActiveContentTargetNode(
        BuildState state,
        OpcPackageSnapshot package,
        IReadOnlySet<string> reachableParts,
        WordActiveContentRelationship relationship,
        IReadOnlyDictionary<string, string> payloadNodeIds
    )
    {
        if (
            relationship.PayloadId is not null
            && payloadNodeIds.TryGetValue(relationship.PayloadId, out var payloadNodeId)
        )
        {
            return payloadNodeId;
        }
        if (relationship.TargetMode == OpcRelationshipTargetMode.External)
        {
            return state.AddNode(
                WordDependencyNodeKind.ExternalTarget,
                relationship.Target,
                isResolved: false,
                isExternal: true,
                isPackageReachable: false
            );
        }
        if (relationship.TargetPartUri is not null)
        {
            return PartNode(
                state,
                package,
                reachableParts,
                relationship.TargetPartUri
            );
        }
        return state.AddNode(
            WordDependencyNodeKind.ActiveContentPayload,
            "unresolved-relationship:" + relationship.Id,
            isResolved: false,
            isExternal: false,
            isPackageReachable: false
        );
    }

    private static IReadOnlyDictionary<string, string> AddBibliographyDependencies(
        BuildState state,
        OpcPackageSnapshot package,
        WordBibliographyGraph bibliography,
        IReadOnlySet<string> reachableParts,
        CancellationToken cancellationToken
    )
    {
        var collectionNodes = new Dictionary<string, string>(StringComparer.Ordinal);
        var sourceNodes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var collection in bibliography.Collections)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var partNodeId = PartNode(
                state,
                package,
                reachableParts,
                collection.PartUri
            );
            var collectionNodeId = state.AddNode(
                WordDependencyNodeKind.BibliographyCollection,
                collection.Id,
                isResolved: true,
                isExternal: false,
                isPackageReachable: collection.IsPackageReachable,
                partUri: collection.PartUri,
                sourceElementOrdinal: collection.SourceElementOrdinal
            );
            collectionNodes.Add(collection.Id, collectionNodeId);
            state.AddEdge(
                WordDependencyEdgeKind.DefinesBibliographyCollection,
                partNodeId,
                collectionNodeId,
                isResolved: true,
                isExternal: false,
                qualifier: collection.NamespaceUri
                    == WordBibliographyGraphBuilder.TransitionalBibliographyNamespace
                        ? "openxml_2006"
                        : "word_2004_10",
                partUri: collection.PartUri,
                sourceElementOrdinal: collection.SourceElementOrdinal
            );
        }
        foreach (var source in bibliography.Sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourceNodeId = state.AddNode(
                WordDependencyNodeKind.BibliographySource,
                source.Id,
                isResolved: true,
                isExternal: false,
                isPackageReachable: reachableParts.Contains(source.PartUri),
                partUri: source.PartUri,
                sourceElementOrdinal: source.SourceElementOrdinal
            );
            sourceNodes.Add(source.Id, sourceNodeId);
            if (collectionNodes.TryGetValue(source.CollectionId, out var collectionNodeId))
            {
                state.AddEdge(
                    WordDependencyEdgeKind.BibliographyContainsSource,
                    collectionNodeId,
                    sourceNodeId,
                    isResolved: true,
                    isExternal: false,
                    qualifier: source.IsSourceTypeKnown
                        ? source.SourceType
                        : source.HasAmbiguousSourceType
                            ? "(ambiguous)"
                        : string.IsNullOrWhiteSpace(source.SourceType)
                            ? "(missing)"
                            : "(unknown)",
                    partUri: source.PartUri,
                    sourceElementOrdinal: source.SourceElementOrdinal
                );
            }
        }
        foreach (var issue in bibliography.Issues)
        {
            cancellationToken.ThrowIfCancellationRequested();
            state.AddIssue(
                "WDG070_" + issue.Code,
                issue.Severity switch
                {
                    WordBibliographyIssueSeverity.Info => WordDependencyIssueSeverity.Info,
                    WordBibliographyIssueSeverity.Warning => WordDependencyIssueSeverity.Warning,
                    WordBibliographyIssueSeverity.Error => WordDependencyIssueSeverity.Error,
                    _ => throw new ArgumentOutOfRangeException(nameof(issue.Severity)),
                },
                $"The typed bibliography graph emitted {issue.Code}.",
                nodeId: issue.SourceId is not null
                    && sourceNodes.TryGetValue(issue.SourceId, out var sourceNodeId)
                        ? sourceNodeId
                        : null,
                partUri: issue.PartUri,
                sourceElementOrdinal: issue.SourceElementOrdinal
            );
        }
        return sourceNodes;
    }

    private static void AddTableDependencies(
        BuildState state,
        WordSemanticDocument semanticDocument,
        WordTableGraph tables,
        IReadOnlySet<string> reachableParts,
        CancellationToken cancellationToken
    )
    {
        var semanticById = semanticDocument.Nodes.ToDictionary(node => node.Id);
        var tableById = tables.Tables.ToDictionary(table => table.Id, StringComparer.Ordinal);
        var cellById = tables.Cells.ToDictionary(cell => cell.Id, StringComparer.Ordinal);

        foreach (var table in tables.Tables.Where(table => table.ParentTableId is not null))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (
                !tableById.TryGetValue(table.ParentTableId!, out var parent)
                || !semanticById.TryGetValue(parent.SemanticNodeId, out var parentNode)
                || !semanticById.TryGetValue(table.SemanticNodeId, out var tableNode)
            )
            {
                throw new WordDependencyProjectionException(
                    "A nested table has no source-linked parent table."
                );
            }
            var parentNodeId = SemanticNode(
                state,
                parentNode,
                reachableParts.Contains(parent.PartUri)
            );
            var tableNodeId = SemanticNode(
                state,
                tableNode,
                reachableParts.Contains(table.PartUri)
            );
            state.AddEdge(
                WordDependencyEdgeKind.TableNestsTable,
                parentNodeId,
                tableNodeId,
                isResolved: true,
                isExternal: false,
                qualifier: table.Depth.ToString(CultureInfo.InvariantCulture),
                partUri: table.PartUri,
                sourceElementOrdinal: table.SourceElementOrdinal
            );
        }

        foreach (var merge in tables.VerticalMerges)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (
                !cellById.TryGetValue(merge.RootCellId, out var rootCell)
                || !semanticById.TryGetValue(rootCell.SemanticNodeId, out var rootNode)
            )
            {
                throw new WordDependencyProjectionException(
                    "A vertical table merge has no source-linked root cell."
                );
            }
            var rootNodeId = SemanticNode(
                state,
                rootNode,
                reachableParts.Contains(rootCell.PartUri)
            );
            foreach (var continuationCellId in merge.CellIds.Skip(1))
            {
                if (
                    !cellById.TryGetValue(continuationCellId, out var continuationCell)
                    || !semanticById.TryGetValue(
                        continuationCell.SemanticNodeId,
                        out var continuationNode
                    )
                )
                {
                    throw new WordDependencyProjectionException(
                        "A vertical table merge references a missing continuation cell."
                    );
                }
                var continuationNodeId = SemanticNode(
                    state,
                    continuationNode,
                    reachableParts.Contains(continuationCell.PartUri)
                );
                state.AddEdge(
                    WordDependencyEdgeKind.TableCellContinuesVerticalMerge,
                    rootNodeId,
                    continuationNodeId,
                    isResolved: merge.IsComplete,
                    isExternal: false,
                    qualifier: $"{merge.LogicalColumnStart}:{merge.GridSpan}",
                    partUri: continuationCell.PartUri,
                    sourceElementOrdinal: continuationCell.SourceElementOrdinal
                );
            }
        }

        var rowById = tables.Rows.ToDictionary(row => row.Id, StringComparer.Ordinal);
        foreach (var issue in tables.Issues)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SemanticNodeId? semanticId = null;
            if (issue.CellId is not null && cellById.TryGetValue(issue.CellId, out var cell))
            {
                semanticId = cell.SemanticNodeId;
            }
            else if (issue.RowId is not null && rowById.TryGetValue(issue.RowId, out var row))
            {
                semanticId = row.SemanticNodeId;
            }
            else if (issue.TableId is not null && tableById.TryGetValue(issue.TableId, out var table))
            {
                semanticId = table.SemanticNodeId;
            }
            string? nodeId = null;
            if (semanticId is not null && semanticById.TryGetValue(semanticId.Value, out var semanticNode))
            {
                nodeId = SemanticNode(
                    state,
                    semanticNode,
                    reachableParts.Contains(semanticNode.SourcePartUri)
                );
            }
            state.AddIssue(
                "WDG060_" + issue.Code,
                issue.Severity switch
                {
                    WordTableIssueSeverity.Info => WordDependencyIssueSeverity.Info,
                    WordTableIssueSeverity.Warning => WordDependencyIssueSeverity.Warning,
                    WordTableIssueSeverity.Error => WordDependencyIssueSeverity.Error,
                    _ => throw new ArgumentOutOfRangeException(nameof(issue.Severity)),
                },
                $"The typed table graph emitted {issue.Code}.",
                nodeId: nodeId,
                partUri: issue.PartUri,
                sourceElementOrdinal: issue.SourceElementOrdinal
            );
        }
    }

    private static void AddContentControlDependencies(
        BuildState state,
        OpcPackageSnapshot package,
        WordContentControlBindingGraph contentControls,
        IReadOnlySet<string> reachableParts,
        CancellationToken cancellationToken
    )
    {
        var controlNodeIds = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var control in contentControls.Controls)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var partNodeId = PartNode(
                state,
                package,
                reachableParts,
                control.PartUri
            );
            var controlNodeId = state.AddNode(
                WordDependencyNodeKind.ContentControl,
                control.Id,
                isResolved: true,
                isExternal: false,
                isPackageReachable: reachableParts.Contains(control.PartUri),
                partUri: control.PartUri,
                sourceElementOrdinal: control.SourceElementOrdinal,
                semanticNodeId: control.SemanticNodeId,
                semanticKind: WordSemanticNodeKind.ContentControl
            );
            controlNodeIds.Add(control.Id, controlNodeId);
            state.AddEdge(
                WordDependencyEdgeKind.DefinesContentControl,
                partNodeId,
                controlNodeId,
                isResolved: true,
                isExternal: false,
                qualifier: control.Type.ToString().ToLowerInvariant(),
                partUri: control.PartUri,
                sourceElementOrdinal: control.SourceElementOrdinal
            );
        }

        var storesById = contentControls.Stores.ToDictionary(
            store => store.Id,
            StringComparer.Ordinal
        );
        var storeNodeIds = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var store in contentControls.Stores)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var storeNodeId = state.AddNode(
                WordDependencyNodeKind.CustomXmlStore,
                store.Id,
                isResolved: store.Parsed,
                isExternal: false,
                isPackageReachable: reachableParts.Contains(store.PartUri),
                partUri: store.PartUri
            );
            storeNodeIds.Add(store.Id, storeNodeId);
            var partNodeId = PartNode(
                state,
                package,
                reachableParts,
                store.PartUri
            );
            state.AddEdge(
                WordDependencyEdgeKind.DefinesCustomXmlStore,
                partNodeId,
                storeNodeId,
                isResolved: store.Parsed,
                isExternal: false,
                qualifier: store.Kind.ToString().ToLowerInvariant(),
                partUri: store.PartUri
            );
        }

        var targetNodeIds = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var target in contentControls.Targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (
                !storeNodeIds.TryGetValue(target.StoreId, out var storeNodeId)
                || !storesById.TryGetValue(target.StoreId, out var store)
            )
            {
                throw new WordDependencyProjectionException(
                    "A custom-XML binding target has no owning store."
                );
            }
            var targetNodeId = state.AddNode(
                WordDependencyNodeKind.CustomXmlBindingTarget,
                target.Id,
                isResolved: true,
                isExternal: false,
                isPackageReachable: reachableParts.Contains(store.PartUri),
                partUri: store.PartUri,
                sourceElementOrdinal: target.SourceElementOrdinal
            );
            targetNodeIds.Add(target.Id, targetNodeId);
            state.AddEdge(
                WordDependencyEdgeKind.CustomXmlStoreContainsTarget,
                storeNodeId,
                targetNodeId,
                isResolved: true,
                isExternal: false,
                partUri: store.PartUri,
                sourceElementOrdinal: target.SourceElementOrdinal
            );
        }

        foreach (var binding in contentControls.Bindings)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!controlNodeIds.TryGetValue(binding.ControlId, out var controlNodeId))
            {
                throw new WordDependencyProjectionException(
                    "A content-control binding has no owning content control."
                );
            }

            string storeNodeId;
            var storeResolved = false;
            if (
                binding.StoreId is not null
                && storeNodeIds.TryGetValue(binding.StoreId, out var knownStoreNodeId)
                && storesById.TryGetValue(binding.StoreId, out var knownStore)
            )
            {
                storeNodeId = knownStoreNodeId;
                storeResolved = knownStore.Parsed;
            }
            else
            {
                storeNodeId = state.AddNode(
                    WordDependencyNodeKind.CustomXmlStore,
                    "unresolved:" + binding.Id,
                    isResolved: false,
                    isExternal: false,
                    isPackageReachable: false
                );
            }
            var storeEdgeId = state.AddEdge(
                WordDependencyEdgeKind.ContentControlUsesStore,
                controlNodeId,
                storeNodeId,
                isResolved: storeResolved,
                isExternal: false,
                qualifier: binding.Status.ToString().ToLowerInvariant(),
                partUri: binding.PartUri,
                sourceElementOrdinal: binding.SourceElementOrdinal
            );

            foreach (var targetId in binding.TargetIds)
            {
                if (!targetNodeIds.TryGetValue(targetId, out var targetNodeId))
                {
                    throw new WordDependencyProjectionException(
                        "A content-control binding references a missing custom-XML target."
                    );
                }
                state.AddEdge(
                    WordDependencyEdgeKind.ContentControlBindsTarget,
                    controlNodeId,
                    targetNodeId,
                    isResolved: true,
                    isExternal: false,
                    qualifier: binding.IsOffice2013RichTextBinding
                        ? "office2013-rich-text"
                        : "standard",
                    partUri: binding.PartUri,
                    sourceElementOrdinal: binding.SourceElementOrdinal
                );
            }

            if (binding.Status != WordBindingResolutionStatus.Resolved)
            {
                state.AddIssue(
                    "WDG050",
                    binding.Status == WordBindingResolutionStatus.XPathUnsupported
                        ? WordDependencyIssueSeverity.Warning
                        : WordDependencyIssueSeverity.Error,
                    "A content-control custom-XML binding is unresolved.",
                    nodeId: controlNodeId,
                    edgeId: storeEdgeId,
                    partUri: binding.PartUri,
                    sourceElementOrdinal: binding.SourceElementOrdinal
                );
            }
        }

        foreach (var repeatingSection in contentControls.RepeatingSections)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (
                !controlNodeIds.TryGetValue(
                    repeatingSection.ControlId,
                    out var containerNodeId
                )
            )
            {
                throw new WordDependencyProjectionException(
                    "A repeating section has no content-control node."
                );
            }
            foreach (var itemControlId in repeatingSection.ItemControlIds)
            {
                if (!controlNodeIds.TryGetValue(itemControlId, out var itemNodeId))
                {
                    throw new WordDependencyProjectionException(
                        "A repeating section references a missing item control."
                    );
                }
                state.AddEdge(
                    WordDependencyEdgeKind.RepeatingSectionContainsItem,
                    containerNodeId,
                    itemNodeId,
                    isResolved: true,
                    isExternal: false,
                    partUri: repeatingSection.PartUri,
                    sourceElementOrdinal: repeatingSection.SourceElementOrdinal
                );
            }
        }
    }

    private static void AddFigureDependencies(
        BuildState state,
        OpcPackageSnapshot package,
        WordSemanticDocument semanticDocument,
        WordFigureCaptionGraph figures,
        IReadOnlySet<string> reachableParts,
        CancellationToken cancellationToken
    )
    {
        var semanticById = semanticDocument.Nodes.ToDictionary(item => item.Id);
        var figureNodeIds = new Dictionary<string, string>(StringComparer.Ordinal);
        var captionNodeIds = new Dictionary<string, string>(StringComparer.Ordinal);
        var resourceNodeIds = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var figure in figures.Figures)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var reachable = reachableParts.Contains(figure.PartUri);
            var figureNodeId = state.AddNode(
                WordDependencyNodeKind.Figure,
                figure.Id,
                isResolved: true,
                isExternal: false,
                reachable,
                partUri: figure.PartUri,
                sourceElementOrdinal: figure.SourceElementOrdinal
            );
            figureNodeIds.Add(figure.Id, figureNodeId);

            foreach (var representation in figure.Representations)
            {
                if (!semanticById.TryGetValue(
                    representation.SemanticNodeId,
                    out var sourceSemantic
                ))
                {
                    throw new WordDependencyProjectionException(
                        "A figure representation has no source-linked drawing semantic node."
                    );
                }
                var semanticNodeId = SemanticNode(state, sourceSemantic, reachable);
                state.AddEdge(
                    WordDependencyEdgeKind.DefinesFigure,
                    semanticNodeId,
                    figureNodeId,
                    isResolved: true,
                    isExternal: false,
                    qualifier: representation.ObjectKind.ToString().ToLowerInvariant(),
                    partUri: representation.PartUri,
                    sourceElementOrdinal: representation.SourceElementOrdinal
                );
                var representationNodeId = state.AddNode(
                    WordDependencyNodeKind.FigureRepresentation,
                    representation.Id,
                    isResolved: true,
                    isExternal: false,
                    reachable,
                    partUri: representation.PartUri,
                    sourceElementOrdinal: representation.SourceElementOrdinal,
                    semanticNodeId: representation.SemanticNodeId,
                    semanticKind: WordSemanticNodeKind.Drawing
                );
                state.AddEdge(
                    WordDependencyEdgeKind.FigureHasRepresentation,
                    figureNodeId,
                    representationNodeId,
                    isResolved: true,
                    isExternal: false,
                    qualifier: representation.Kind.ToString().ToLowerInvariant(),
                    partUri: representation.PartUri,
                    sourceElementOrdinal: representation.SourceElementOrdinal
                );
                if (representation.ShapeModel is { } shapeModel)
                {
                    foreach (var shapeRoot in shapeModel.Roots)
                    {
                        AddFigureShapeDependency(
                            state,
                            representationNodeId,
                            shapeRoot,
                            reachable,
                            representation.PartUri,
                            parentIsRepresentation: true
                        );
                    }
                }
                foreach (var resource in representation.Resources)
                {
                    if (!resourceNodeIds.TryGetValue(resource.Id, out var resourceNodeId))
                    {
                        resourceNodeId = state.AddNode(
                            WordDependencyNodeKind.FigureResource,
                            resource.Id,
                            isResolved: true,
                            isExternal: resource.IsExternal,
                            reachable,
                            partUri: representation.PartUri,
                            sourceElementOrdinal: resource.SourceElementOrdinal
                        );
                        resourceNodeIds.Add(resource.Id, resourceNodeId);
                    }
                    state.AddEdge(
                        WordDependencyEdgeKind.FigureUsesResource,
                        representationNodeId,
                        resourceNodeId,
                        isResolved: true,
                        isExternal: resource.IsExternal,
                        qualifier: resource.Role.ToString().ToLowerInvariant(),
                        partUri: representation.PartUri,
                        sourceElementOrdinal: resource.SourceElementOrdinal,
                        relationshipId: resource.RelationshipId,
                        relationshipType: resource.RelationshipType
                    );

                    string targetNodeId;
                    if (resource.TargetPartUri is { } targetPartUri)
                    {
                        targetNodeId = PartNode(
                            state,
                            package,
                            reachableParts,
                            targetPartUri
                        );
                    }
                    else
                    {
                        targetNodeId = state.AddNode(
                            resource.IsExternal
                                ? WordDependencyNodeKind.ExternalTarget
                                : WordDependencyNodeKind.Part,
                            resource.Target ?? $"missing-figure-resource:{resource.Id}",
                            isResolved: false,
                            isExternal: resource.IsExternal,
                            isPackageReachable: false,
                            partUri: resource.TargetPartUri
                        );
                    }
                    state.AddEdge(
                        WordDependencyEdgeKind.FigureResourceTargetsPart,
                        resourceNodeId,
                        targetNodeId,
                        resource.IsResolved,
                        resource.IsExternal,
                        qualifier: resource.Role.ToString().ToLowerInvariant(),
                        partUri: representation.PartUri,
                        sourceElementOrdinal: resource.SourceElementOrdinal,
                        relationshipId: resource.RelationshipId,
                        relationshipType: resource.RelationshipType
                    );
                }
            }
        }

        foreach (var caption in figures.Captions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!semanticById.TryGetValue(caption.ParagraphNodeId, out var paragraph))
            {
                throw new WordDependencyProjectionException(
                    "A caption has no source-linked paragraph semantic node."
                );
            }
            var reachable = reachableParts.Contains(caption.PartUri);
            var paragraphNodeId = SemanticNode(state, paragraph, reachable);
            var captionNodeId = state.AddNode(
                WordDependencyNodeKind.Caption,
                caption.Id,
                isResolved: true,
                isExternal: false,
                reachable,
                partUri: caption.PartUri,
                sourceElementOrdinal: caption.SourceElementOrdinal,
                semanticNodeId: caption.ParagraphNodeId,
                semanticKind: WordSemanticNodeKind.Paragraph
            );
            captionNodeIds.Add(caption.Id, captionNodeId);
            state.AddEdge(
                WordDependencyEdgeKind.DefinesCaption,
                paragraphNodeId,
                captionNodeId,
                isResolved: true,
                isExternal: false,
                qualifier: caption.Kind.ToString().ToLowerInvariant(),
                partUri: caption.PartUri,
                sourceElementOrdinal: caption.SourceElementOrdinal
            );
        }

        foreach (var association in figures.Associations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (
                !figureNodeIds.TryGetValue(association.FigureId, out var figureNodeId)
                || !captionNodeIds.TryGetValue(association.CaptionId, out var captionNodeId)
            )
            {
                throw new WordDependencyProjectionException(
                    "A figure-caption association has a missing endpoint."
                );
            }
            state.AddEdge(
                WordDependencyEdgeKind.FigureCaptionAssociation,
                figureNodeId,
                captionNodeId,
                association.Status == WordFigureCaptionAssociationStatus.Selected,
                isExternal: false,
                qualifier: string.Join(
                    ':',
                    association.Status.ToString().ToLowerInvariant(),
                    association.Confidence.ToString().ToLowerInvariant(),
                    association.Direction.ToString().ToLowerInvariant(),
                    association.Score.ToString(CultureInfo.InvariantCulture)
                )
            );
        }

        foreach (var issue in figures.Issues)
        {
            var nodeId = issue.FigureId is not null
                && figureNodeIds.TryGetValue(issue.FigureId, out var figureNodeId)
                    ? figureNodeId
                    : issue.CaptionId is not null
                        && captionNodeIds.TryGetValue(issue.CaptionId, out var captionNodeId)
                            ? captionNodeId
                            : null;
            state.AddIssue(
                $"WDF:{issue.Code}",
                issue.Severity switch
                {
                    WordFigureIssueSeverity.Error => WordDependencyIssueSeverity.Error,
                    WordFigureIssueSeverity.Warning => WordDependencyIssueSeverity.Warning,
                    _ => WordDependencyIssueSeverity.Info,
                },
                issue.Message,
                nodeId,
                partUri: issue.PartUri,
                sourceElementOrdinal: issue.SourceElementOrdinal
            );
        }
    }

    private static void AddFigureShapeDependency(
        BuildState state,
        string parentNodeId,
        WordFigureShapeNodeDefinition shape,
        bool reachable,
        string partUri,
        bool parentIsRepresentation
    )
    {
        var shapeNodeId = state.AddNode(
            WordDependencyNodeKind.FigureShape,
            shape.Id,
            isResolved: true,
            isExternal: false,
            reachable,
            partUri,
            sourceElementOrdinal: shape.SourceElementOrdinal
        );
        state.AddEdge(
            parentIsRepresentation
                ? WordDependencyEdgeKind.FigureRepresentationContainsShape
                : WordDependencyEdgeKind.FigureShapeContainsShape,
            parentNodeId,
            shapeNodeId,
            isResolved: true,
            isExternal: false,
            qualifier: shape.Kind.ToString().ToLowerInvariant(),
            partUri,
            sourceElementOrdinal: shape.SourceElementOrdinal
        );
        foreach (var child in shape.Children)
        {
            AddFigureShapeDependency(
                state,
                shapeNodeId,
                child,
                reachable,
                partUri,
                parentIsRepresentation: false
            );
        }
    }

    private static void AddDiagramDependencies(
        BuildState state,
        OpcPackageSnapshot package,
        WordDiagramGraph diagrams,
        IReadOnlySet<string> reachableParts,
        CancellationToken cancellationToken
    )
    {
        var diagramNodeIds = new Dictionary<string, string>(StringComparer.Ordinal);
        var pointNodeIdsByModelId = new Dictionary<(string DiagramId, string ModelId), string>();
        var connectionEdgeIds = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var diagram in diagrams.Diagrams)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourcePartNodeId = PartNode(
                state,
                package,
                reachableParts,
                diagram.SourcePartUri
            );
            var diagramNodeId = state.AddNode(
                WordDependencyNodeKind.Diagram,
                diagram.Id,
                diagram.RequiredPartsResolved,
                isExternal: false,
                diagram.IsPackageReachable,
                partUri: diagram.SourcePartUri,
                sourceElementOrdinal: diagram.SourceElementOrdinal
            );
            diagramNodeIds.Add(diagram.Id, diagramNodeId);
            state.AddEdge(
                WordDependencyEdgeKind.DefinesDiagram,
                sourcePartNodeId,
                diagramNodeId,
                isResolved: true,
                isExternal: false,
                partUri: diagram.SourcePartUri,
                sourceElementOrdinal: diagram.SourceElementOrdinal
            );

            foreach (var reference in diagram.PartReferences)
            {
                string targetNodeId;
                if (reference.TargetPartUri is not null)
                {
                    targetNodeId = PartNode(
                        state,
                        package,
                        reachableParts,
                        reference.TargetPartUri
                    );
                }
                else if (reference.TargetMode == OpcRelationshipTargetMode.External)
                {
                    targetNodeId = state.AddNode(
                        WordDependencyNodeKind.ExternalTarget,
                        reference.Target,
                        isResolved: false,
                        isExternal: true,
                        isPackageReachable: false
                    );
                }
                else
                {
                    targetNodeId = state.AddNode(
                        WordDependencyNodeKind.Part,
                        reference.Target,
                        isResolved: false,
                        isExternal: false,
                        isPackageReachable: false
                    );
                }
                state.AddEdge(
                    WordDependencyEdgeKind.DiagramUsesPart,
                    diagramNodeId,
                    targetNodeId,
                    reference.IsResolved,
                    reference.TargetMode == OpcRelationshipTargetMode.External,
                    qualifier: reference.Kind.ToString().ToLowerInvariant(),
                    partUri: diagram.SourcePartUri,
                    sourceElementOrdinal: diagram.SourceElementOrdinal,
                    relationshipId: reference.RelationshipId,
                    relationshipType: reference.RelationshipType
                );
            }

            foreach (var point in diagram.Points)
            {
                var pointNodeId = state.AddNode(
                    WordDependencyNodeKind.DiagramPoint,
                    point.Id,
                    point.IsStructurallyValid,
                    isExternal: false,
                    diagram.IsPackageReachable,
                    partUri: point.PartUri,
                    sourceElementOrdinal: point.SourceElementOrdinal
                );
                if (point.IsModelIdUnique)
                {
                    pointNodeIdsByModelId.Add((diagram.Id, point.ModelId), pointNodeId);
                }
                state.AddEdge(
                    WordDependencyEdgeKind.DiagramContainsPoint,
                    diagramNodeId,
                    pointNodeId,
                    point.IsStructurallyValid,
                    isExternal: false,
                    qualifier: point.PointType,
                    partUri: point.PartUri,
                    sourceElementOrdinal: point.SourceElementOrdinal
                );
            }

            foreach (var connection in diagram.Connections)
            {
                if (!pointNodeIdsByModelId.TryGetValue(
                    (diagram.Id, connection.SourceModelId),
                    out var sourcePointNodeId
                ))
                {
                    sourcePointNodeId = state.AddNode(
                        WordDependencyNodeKind.DiagramPoint,
                        $"{connection.Id}:source",
                        isResolved: false,
                        isExternal: false,
                        diagram.IsPackageReachable,
                        partUri: connection.PartUri,
                        sourceElementOrdinal: connection.SourceElementOrdinal
                    );
                    state.AddEdge(
                        WordDependencyEdgeKind.DiagramContainsPoint,
                        diagramNodeId,
                        sourcePointNodeId,
                        isResolved: false,
                        isExternal: false,
                        qualifier: "unresolved_source",
                        partUri: connection.PartUri,
                        sourceElementOrdinal: connection.SourceElementOrdinal
                    );
                }
                if (!pointNodeIdsByModelId.TryGetValue(
                    (diagram.Id, connection.DestinationModelId),
                    out var destinationPointNodeId
                ))
                {
                    destinationPointNodeId = state.AddNode(
                        WordDependencyNodeKind.DiagramPoint,
                        $"{connection.Id}:destination",
                        isResolved: false,
                        isExternal: false,
                        diagram.IsPackageReachable,
                        partUri: connection.PartUri,
                        sourceElementOrdinal: connection.SourceElementOrdinal
                    );
                    state.AddEdge(
                        WordDependencyEdgeKind.DiagramContainsPoint,
                        diagramNodeId,
                        destinationPointNodeId,
                        isResolved: false,
                        isExternal: false,
                        qualifier: "unresolved_destination",
                        partUri: connection.PartUri,
                        sourceElementOrdinal: connection.SourceElementOrdinal
                    );
                }
                var edgeId = state.AddEdge(
                    WordDependencyEdgeKind.DiagramConnectsPoints,
                    sourcePointNodeId,
                    destinationPointNodeId,
                    connection.IsStructurallyValid,
                    isExternal: false,
                    qualifier: connection.ConnectionType,
                    partUri: connection.PartUri,
                    sourceElementOrdinal: connection.SourceElementOrdinal
                );
                connectionEdgeIds.Add(connection.Id, edgeId);
            }
        }

        foreach (var issue in diagrams.Issues)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var nodeId = issue.DiagramId is not null
                && diagramNodeIds.TryGetValue(issue.DiagramId, out var diagramNodeId)
                    ? diagramNodeId
                    : null;
            if (
                issue.DiagramId is not null
                && issue.PointId is not null
                && pointNodeIdsByModelId.TryGetValue(
                    (issue.DiagramId, issue.PointId),
                    out var pointNodeId
                )
            )
            {
                nodeId = pointNodeId;
            }
            var edgeId = issue.ConnectionId is not null
                && connectionEdgeIds.TryGetValue(issue.ConnectionId, out var connectionEdgeId)
                    ? connectionEdgeId
                    : null;
            state.AddIssue(
                $"DGM:{issue.Code}",
                issue.Severity switch
                {
                    WordDiagramIssueSeverity.Error => WordDependencyIssueSeverity.Error,
                    WordDiagramIssueSeverity.Warning => WordDependencyIssueSeverity.Warning,
                    _ => WordDependencyIssueSeverity.Info,
                },
                issue.Message,
                nodeId,
                edgeId,
                issue.PartUri,
                issue.SourceElementOrdinal
            );
        }
    }

    private static void AddChartDependencies(
        BuildState state,
        OpcPackageSnapshot package,
        WordChartGraph charts,
        IReadOnlySet<string> reachableParts,
        CancellationToken cancellationToken
    )
    {
        foreach (var chart in charts.Charts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var partNodeId = PartNode(
                state,
                package,
                reachableParts,
                chart.PartUri
            );
            var chartNodeId = state.AddNode(
                WordDependencyNodeKind.Chart,
                chart.Id,
                isResolved: true,
                isExternal: false,
                chart.IsPackageReachable,
                partUri: chart.PartUri,
                sourceElementOrdinal: chart.SourceElementOrdinal
            );
            state.AddEdge(
                WordDependencyEdgeKind.DefinesChart,
                partNodeId,
                chartNodeId,
                isResolved: true,
                isExternal: false,
                partUri: chart.PartUri,
                sourceElementOrdinal: chart.SourceElementOrdinal
            );
            foreach (var series in chart.Series)
            {
                var seriesNodeId = state.AddNode(
                    WordDependencyNodeKind.ChartSeries,
                    series.Id,
                    isResolved: true,
                    isExternal: false,
                    chart.IsPackageReachable,
                    partUri: chart.PartUri,
                    sourceElementOrdinal: series.SourceElementOrdinal
                );
                state.AddEdge(
                    WordDependencyEdgeKind.ChartContainsSeries,
                    chartNodeId,
                    seriesNodeId,
                    isResolved: true,
                    isExternal: false,
                    qualifier: series.ChartType,
                    partUri: chart.PartUri,
                    sourceElementOrdinal: series.SourceElementOrdinal
                );
            }
            foreach (var axis in chart.Axes)
            {
                var axisNodeId = state.AddNode(
                    WordDependencyNodeKind.ChartAxis,
                    $"{chart.Id}:{axis.AxisId}:{axis.SourceElementOrdinal}",
                    isResolved: true,
                    isExternal: false,
                    chart.IsPackageReachable,
                    partUri: chart.PartUri,
                    sourceElementOrdinal: axis.SourceElementOrdinal
                );
                state.AddEdge(
                    WordDependencyEdgeKind.ChartContainsAxis,
                    chartNodeId,
                    axisNodeId,
                    isResolved: true,
                    isExternal: false,
                    qualifier: axis.Kind,
                    partUri: chart.PartUri,
                    sourceElementOrdinal: axis.SourceElementOrdinal
                );
            }
            foreach (var related in chart.RelatedParts)
            {
                string targetNodeId;
                if (related.TargetPartUri is not null)
                {
                    targetNodeId = PartNode(
                        state,
                        package,
                        reachableParts,
                        related.TargetPartUri
                    );
                }
                else
                {
                    targetNodeId = state.AddNode(
                        WordDependencyNodeKind.ExternalTarget,
                        related.Target,
                        isResolved: false,
                        isExternal: related.TargetMode == OpcRelationshipTargetMode.External,
                        isPackageReachable: false
                    );
                }
                state.AddEdge(
                    WordDependencyEdgeKind.ChartUsesPart,
                    chartNodeId,
                    targetNodeId,
                    related.IsResolved,
                    related.TargetMode == OpcRelationshipTargetMode.External,
                    qualifier: related.Kind.ToString().ToLowerInvariant(),
                    partUri: chart.PartUri,
                    relationshipId: related.RelationshipId,
                    relationshipType: related.RelationshipType
                );
            }
        }
    }

    private static void AddPackageDependencies(
        BuildState state,
        OpcPackageSnapshot package,
        string packageNodeId,
        IReadOnlySet<string> reachableParts,
        CancellationToken cancellationToken
    )
    {
        foreach (
            var part in package.Parts.Values.OrderBy(part => part.Uri, StringComparer.Ordinal)
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            state.AddNode(
                WordDependencyNodeKind.Part,
                part.Uri,
                isResolved: true,
                isExternal: false,
                isPackageReachable: reachableParts.Contains(part.Uri),
                partUri: part.Uri
            );
        }

        foreach (
            var relationship in package.Relationships
                .OrderBy(item => item.SourcePartUri, StringComparer.Ordinal)
                .ThenBy(item => item.Id, StringComparer.Ordinal)
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourceNodeId = relationship.SourcePartUri == "/"
                ? packageNodeId
                : PartNode(
                    state,
                    package,
                    reachableParts,
                    relationship.SourcePartUri
                );
            string targetNodeId;
            var resolved = false;
            var external = relationship.TargetMode == OpcRelationshipTargetMode.External;
            if (
                relationship.TargetMode == OpcRelationshipTargetMode.Internal
                && relationship.ResolvedTargetPartUri is { } targetPartUri
            )
            {
                resolved = package.Parts.ContainsKey(targetPartUri);
                targetNodeId = PartNode(
                    state,
                    package,
                    reachableParts,
                    targetPartUri
                );
            }
            else if (external)
            {
                targetNodeId = state.AddNode(
                    WordDependencyNodeKind.ExternalTarget,
                    relationship.Target,
                    isResolved: false,
                    isExternal: true,
                    isPackageReachable: false
                );
            }
            else
            {
                var key = relationship.ResolvedTargetPartUri
                    ?? relationship.Target;
                targetNodeId = state.AddNode(
                    WordDependencyNodeKind.Part,
                    key,
                    isResolved: false,
                    isExternal: false,
                    isPackageReachable: false,
                    partUri: relationship.ResolvedTargetPartUri
                );
            }

            var edgeId = state.AddEdge(
                WordDependencyEdgeKind.PackageRelationship,
                sourceNodeId,
                targetNodeId,
                resolved,
                external,
                qualifier: relationship.TargetMode.ToString().ToLowerInvariant(),
                partUri: relationship.SourcePartUri == "/"
                    ? null
                    : relationship.SourcePartUri,
                relationshipId: relationship.Id,
                relationshipType: relationship.Type
            );
            if (
                relationship.TargetMode == OpcRelationshipTargetMode.Internal
                && !resolved
            )
            {
                state.AddIssue(
                    "WDG001",
                    WordDependencyIssueSeverity.Error,
                    "An internal package relationship does not resolve to an existing part.",
                    edgeId: edgeId,
                    partUri: relationship.SourcePartUri == "/"
                        ? null
                        : relationship.SourcePartUri
                );
            }
            else if (relationship.TargetMode == OpcRelationshipTargetMode.Invalid)
            {
                state.AddIssue(
                    "WDG002",
                    WordDependencyIssueSeverity.Error,
                    "A package relationship target is structurally invalid.",
                    edgeId: edgeId,
                    partUri: relationship.SourcePartUri == "/"
                        ? null
                        : relationship.SourcePartUri
                );
            }
        }

        foreach (
            var part in package.Parts.Values
                .Where(part => !reachableParts.Contains(part.Uri))
                .OrderBy(part => part.Uri, StringComparer.Ordinal)
        )
        {
            var nodeId = PartNode(state, package, reachableParts, part.Uri);
            state.AddIssue(
                "WDG003",
                WordDependencyIssueSeverity.Warning,
                "A package part is not reachable from a package-level relationship.",
                nodeId: nodeId,
                partUri: part.Uri
            );
        }
    }

    private static void AddSemanticDependencies(
        BuildState state,
        OpcPackageSnapshot package,
        WordSemanticDocument semanticDocument,
        IReadOnlySet<string> reachableParts,
        CancellationToken cancellationToken
    )
    {
        var semanticNodes = semanticDocument.Nodes.ToDictionary(node => node.Id);
        foreach (var node in semanticDocument.Nodes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var nodeId = SemanticNode(state, node, reachableParts.Contains(node.SourcePartUri));
            if (
                node.ParentId is { } parentId
                && semanticNodes.TryGetValue(parentId, out var parent)
            )
            {
                var parentNodeId = SemanticNode(
                    state,
                    parent,
                    reachableParts.Contains(parent.SourcePartUri)
                );
                state.AddEdge(
                    WordDependencyEdgeKind.SemanticContainment,
                    parentNodeId,
                    nodeId,
                    isResolved: true,
                    isExternal: false,
                    qualifier: node.Kind.ToString().ToLowerInvariant(),
                    partUri: node.SourcePartUri,
                    sourceElementOrdinal: node.SourceElementOrdinal
                );
                if (!string.Equals(
                    parent.SourcePartUri,
                    node.SourcePartUri,
                    StringComparison.Ordinal
                ))
                {
                    AddPartSemanticRootEdge(
                        state,
                        package,
                        reachableParts,
                        node,
                        nodeId
                    );
                }
            }
            else
            {
                AddPartSemanticRootEdge(
                    state,
                    package,
                    reachableParts,
                    node,
                    nodeId
                );
            }
        }
    }

    private static void AddPartSemanticRootEdge(
        BuildState state,
        OpcPackageSnapshot package,
        IReadOnlySet<string> reachableParts,
        WordSemanticNode node,
        string nodeId
    )
    {
        var partNodeId = PartNode(
            state,
            package,
            reachableParts,
            node.SourcePartUri
        );
        state.AddEdge(
            WordDependencyEdgeKind.PartContainsSemanticRoot,
            partNodeId,
            nodeId,
            isResolved: package.Parts.ContainsKey(node.SourcePartUri),
            isExternal: false,
            qualifier: node.Kind.ToString().ToLowerInvariant(),
            partUri: node.SourcePartUri,
            sourceElementOrdinal: node.SourceElementOrdinal
        );
    }

    private static void AddStyleDependencies(
        BuildState state,
        OpcPackageSnapshot package,
        WordSemanticDocument semanticDocument,
        WordStyleGraph styles,
        string packageNodeId,
        IReadOnlySet<string> reachableParts,
        CancellationToken cancellationToken
    )
    {
        var stylesReachable = styles.StylesPartUri is { } stylePartUri
            && reachableParts.Contains(stylePartUri);
        string? stylesPartNodeId = null;
        if (styles.StylesPartUri is { } stylesPartUri)
        {
            stylesPartNodeId = PartNode(
                state,
                package,
                reachableParts,
                stylesPartUri
            );
        }
        foreach (var style in styles.Styles.OrderBy(item => item.StyleId, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var styleNodeId = StyleNode(
                state,
                styles,
                style.StyleId,
                resolved: true,
                stylesReachable
            );
            if (stylesPartNodeId is not null)
            {
                state.AddEdge(
                    WordDependencyEdgeKind.DefinesStyle,
                    stylesPartNodeId,
                    styleNodeId,
                    isResolved: true,
                    isExternal: false,
                    qualifier: style.Type.ToString().ToLowerInvariant(),
                    partUri: styles.StylesPartUri,
                    sourceElementOrdinal: style.SourceElementOrdinal
                );
            }
            AddStyleReference(
                state,
                styles,
                styleNodeId,
                style.BasedOnStyleId,
                WordDependencyEdgeKind.StyleBasedOn,
                style.SourceElementOrdinal,
                stylesReachable
            );
            AddStyleReference(
                state,
                styles,
                styleNodeId,
                style.NextStyleId,
                WordDependencyEdgeKind.StyleNext,
                style.SourceElementOrdinal,
                stylesReachable
            );
            AddStyleReference(
                state,
                styles,
                styleNodeId,
                style.LinkedStyleId,
                WordDependencyEdgeKind.StyleLinked,
                style.SourceElementOrdinal,
                stylesReachable
            );
        }

        foreach (var (type, styleId) in styles.DefaultStyleIds.OrderBy(pair => pair.Key))
        {
            var target = StyleNode(
                state,
                styles,
                styleId,
                styles.TryGetStyle(styleId, out _),
                stylesReachable
            );
            state.AddEdge(
                WordDependencyEdgeKind.DefaultStyle,
                packageNodeId,
                target,
                isResolved: styles.TryGetStyle(styleId, out _),
                isExternal: false,
                qualifier: type.ToString().ToLowerInvariant(),
                partUri: styles.StylesPartUri
            );
        }

        foreach (var node in semanticDocument.Nodes)
        {
            if (
                node.Kind is not WordSemanticNodeKind.Paragraph
                    and not WordSemanticNodeKind.Run
                    and not WordSemanticNodeKind.Table
                || !node.Properties.TryGetValue("style_id", out var styleId)
                || string.IsNullOrWhiteSpace(styleId)
            )
            {
                continue;
            }
            var resolved = styles.TryGetStyle(styleId, out _);
            var target = StyleNode(
                state,
                styles,
                styleId,
                resolved,
                stylesReachable
            );
            var source = SemanticNode(
                state,
                node,
                reachableParts.Contains(node.SourcePartUri)
            );
            var edgeId = state.AddEdge(
                WordDependencyEdgeKind.UsesStyle,
                source,
                target,
                resolved,
                isExternal: false,
                qualifier: node.Kind.ToString().ToLowerInvariant(),
                partUri: node.SourcePartUri,
                sourceElementOrdinal: node.SourceElementOrdinal
            );
            if (!resolved)
            {
                state.AddIssue(
                    "WDG010",
                    WordDependencyIssueSeverity.Error,
                    "Word content refers to a missing style definition.",
                    edgeId: edgeId,
                    partUri: node.SourcePartUri,
                    sourceElementOrdinal: node.SourceElementOrdinal
                );
            }
        }
    }

    private static void AddStyleReference(
        BuildState state,
        WordStyleGraph styles,
        string sourceNodeId,
        string? targetStyleId,
        WordDependencyEdgeKind kind,
        int sourceElementOrdinal,
        bool stylesReachable
    )
    {
        if (string.IsNullOrWhiteSpace(targetStyleId))
        {
            return;
        }
        var resolved = styles.TryGetStyle(targetStyleId, out _);
        var targetNodeId = StyleNode(
            state,
            styles,
            targetStyleId,
            resolved,
            stylesReachable
        );
        var edgeId = state.AddEdge(
            kind,
            sourceNodeId,
            targetNodeId,
            resolved,
            isExternal: false,
            partUri: styles.StylesPartUri,
            sourceElementOrdinal: sourceElementOrdinal
        );
        if (!resolved)
        {
            state.AddIssue(
                "WDG011",
                WordDependencyIssueSeverity.Error,
                "A style dependency refers to a missing style definition.",
                edgeId: edgeId,
                partUri: styles.StylesPartUri,
                sourceElementOrdinal: sourceElementOrdinal
            );
        }
    }

    private static void AddNumberingDependencies(
        BuildState state,
        OpcPackageSnapshot package,
        WordSemanticDocument semanticDocument,
        WordStyleGraph styles,
        WordNumberingGraph numbering,
        IReadOnlySet<string> reachableParts,
        CancellationToken cancellationToken
    )
    {
        var numberingReachable = numbering.NumberingPartUri is { } numberingPartUriValue
            && reachableParts.Contains(numberingPartUriValue);
        var stylesReachable = styles.StylesPartUri is { } stylesPartUriValue
            && reachableParts.Contains(stylesPartUriValue);
        string? numberingPartNodeId = null;
        if (numbering.NumberingPartUri is { } numberingPartUri)
        {
            numberingPartNodeId = PartNode(
                state,
                package,
                reachableParts,
                numberingPartUri
            );
        }
        foreach (var definition in numbering.AbstractDefinitions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var abstractNodeId = AbstractNumberingNode(
                state,
                numbering,
                definition.AbstractNumberId,
                resolved: true,
                numberingReachable
            );
            if (numberingPartNodeId is not null)
            {
                state.AddEdge(
                    WordDependencyEdgeKind.DefinesAbstractNumbering,
                    numberingPartNodeId,
                    abstractNodeId,
                    isResolved: true,
                    isExternal: false,
                    partUri: numbering.NumberingPartUri,
                    sourceElementOrdinal: definition.SourceElementOrdinal
                );
            }
            AddAbstractStyleReference(
                state,
                styles,
                numbering,
                abstractNodeId,
                definition.NumberingStyleLinkId,
                "numbering_style_link",
                definition.SourceElementOrdinal,
                stylesReachable
            );
            AddAbstractStyleReference(
                state,
                styles,
                numbering,
                abstractNodeId,
                definition.StyleLinkId,
                "style_link",
                definition.SourceElementOrdinal,
                stylesReachable
            );
            foreach (var level in definition.Levels)
            {
                AddNumberingLevelDependencies(
                    state,
                    styles,
                    numbering,
                    abstractNodeId,
                    level,
                    $"abstract:{definition.AbstractNumberId}",
                    stylesReachable,
                    numberingReachable
                );
            }
        }

        foreach (var instance in numbering.Instances)
        {
            var instanceNodeId = NumberingInstanceNode(
                state,
                numbering,
                instance.NumberId,
                resolved: true,
                numberingReachable
            );
            if (numberingPartNodeId is not null)
            {
                state.AddEdge(
                    WordDependencyEdgeKind.DefinesNumberingInstance,
                    numberingPartNodeId,
                    instanceNodeId,
                    isResolved: true,
                    isExternal: false,
                    partUri: numbering.NumberingPartUri,
                    sourceElementOrdinal: instance.SourceElementOrdinal
                );
            }
            var abstractResolved = numbering.TryGetAbstractDefinition(
                instance.AbstractNumberId,
                out _
            );
            var abstractNodeId = AbstractNumberingNode(
                state,
                numbering,
                instance.AbstractNumberId,
                abstractResolved,
                numberingReachable
            );
            var edgeId = state.AddEdge(
                WordDependencyEdgeKind.NumberingInstanceUsesAbstract,
                instanceNodeId,
                abstractNodeId,
                abstractResolved,
                isExternal: false,
                partUri: numbering.NumberingPartUri,
                sourceElementOrdinal: instance.SourceElementOrdinal
            );
            if (!abstractResolved)
            {
                state.AddIssue(
                    "WDG020",
                    WordDependencyIssueSeverity.Error,
                    "A numbering instance refers to a missing abstract numbering definition.",
                    edgeId: edgeId,
                    partUri: numbering.NumberingPartUri,
                    sourceElementOrdinal: instance.SourceElementOrdinal
                );
            }
            foreach (var levelOverride in instance.LevelOverrides)
            {
                if (levelOverride.Level is { } level)
                {
                    AddNumberingLevelDependencies(
                        state,
                        styles,
                        numbering,
                        instanceNodeId,
                        level,
                        $"instance:{instance.NumberId}",
                        stylesReachable,
                        numberingReachable
                    );
                }
            }
        }

        foreach (var picture in numbering.PictureBullets)
        {
            var pictureNodeId = PictureBulletNode(
                state,
                numbering,
                picture.PictureBulletId,
                resolved: true,
                numberingReachable
            );
            if (numberingPartNodeId is not null)
            {
                state.AddEdge(
                    WordDependencyEdgeKind.DefinesPictureBullet,
                    numberingPartNodeId,
                    pictureNodeId,
                    isResolved: true,
                    isExternal: false,
                    partUri: numbering.NumberingPartUri,
                    sourceElementOrdinal: picture.SourceElementOrdinal
                );
            }
            foreach (var relationshipId in picture.RelationshipIds)
            {
                var relationship = package.RelationshipsFrom(
                        numbering.NumberingPartUri ?? semanticDocument.MainPartUri
                    )
                    .SingleOrDefault(item => item.Id == relationshipId);
                var external = relationship?.TargetMode
                    == OpcRelationshipTargetMode.External;
                var resolved = !external
                    && relationship?.ResolvedTargetPartUri is { } targetPart
                    && package.Parts.ContainsKey(targetPart);
                var targetNodeId = external
                    ? state.AddNode(
                        WordDependencyNodeKind.ExternalTarget,
                        relationship!.Target,
                        isResolved: false,
                        isExternal: true,
                        isPackageReachable: false
                    )
                    : relationship?.ResolvedTargetPartUri is { } targetUri
                        ? PartNode(state, package, reachableParts, targetUri)
                        : state.AddNode(
                        WordDependencyNodeKind.Part,
                        $"missing-picture-relationship:{relationshipId}",
                        isResolved: false,
                        isExternal: false,
                        isPackageReachable: false
                    );
                var edgeId = state.AddEdge(
                    WordDependencyEdgeKind.PictureBulletRelationship,
                    pictureNodeId,
                    targetNodeId,
                    resolved,
                    isExternal: external,
                    partUri: numbering.NumberingPartUri,
                    sourceElementOrdinal: picture.SourceElementOrdinal,
                    relationshipId: relationshipId,
                    relationshipType: relationship?.Type
                );
                if (!resolved)
                {
                    state.AddIssue(
                        "WDG021",
                        WordDependencyIssueSeverity.Error,
                        "A picture-bullet dependency does not resolve to an internal package part.",
                        edgeId: edgeId,
                        partUri: numbering.NumberingPartUri,
                        sourceElementOrdinal: picture.SourceElementOrdinal
                    );
                }
            }
        }

        foreach (var node in semanticDocument.Nodes.Where(item =>
            item.Kind == WordSemanticNodeKind.Paragraph
            && item.Properties.TryGetValue("numbering_id", out _)
        ))
        {
            var numberIdText = node.Properties["numbering_id"];
            var resolved = int.TryParse(
                    numberIdText,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var numberId
                )
                && numbering.TryGetInstance(numberId, out _);
            var targetNodeId = int.TryParse(
                numberIdText,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out numberId
            )
                ? NumberingInstanceNode(
                    state,
                    numbering,
                    numberId,
                    resolved,
                    numberingReachable
                )
                : state.AddNode(
                    WordDependencyNodeKind.NumberingInstance,
                    numberIdText,
                    isResolved: false,
                    isExternal: false,
                    isPackageReachable: numbering.NumberingPartUri is not null,
                    partUri: numbering.NumberingPartUri
                );
            var sourceNodeId = SemanticNode(
                state,
                node,
                reachableParts.Contains(node.SourcePartUri)
            );
            var edgeId = state.AddEdge(
                WordDependencyEdgeKind.UsesNumbering,
                sourceNodeId,
                targetNodeId,
                resolved,
                isExternal: false,
                qualifier: node.Properties.TryGetValue("numbering_level", out var level)
                    ? level
                    : null,
                partUri: node.SourcePartUri,
                sourceElementOrdinal: node.SourceElementOrdinal
            );
            if (!resolved)
            {
                state.AddIssue(
                    "WDG022",
                    WordDependencyIssueSeverity.Error,
                    "A paragraph refers to a missing or invalid numbering instance.",
                    edgeId: edgeId,
                    partUri: node.SourcePartUri,
                    sourceElementOrdinal: node.SourceElementOrdinal
                );
            }
        }

        foreach (var style in styles.Styles)
        {
            AddStyleNumberingReference(
                state,
                styles,
                numbering,
                style,
                style.ParagraphProperties,
                stylesReachable,
                numberingReachable
            );
        }
    }

    private static void AddAbstractStyleReference(
        BuildState state,
        WordStyleGraph styles,
        WordNumberingGraph numbering,
        string sourceNodeId,
        string? styleId,
        string qualifier,
        int sourceElementOrdinal,
        bool stylesReachable
    )
    {
        if (string.IsNullOrWhiteSpace(styleId))
        {
            return;
        }
        var resolved = styles.TryGetStyle(styleId, out _);
        var targetNodeId = StyleNode(
            state,
            styles,
            styleId,
            resolved,
            stylesReachable
        );
        var edgeId = state.AddEdge(
            WordDependencyEdgeKind.AbstractNumberingUsesStyle,
            sourceNodeId,
            targetNodeId,
            resolved,
            isExternal: false,
            qualifier: qualifier,
            partUri: numbering.NumberingPartUri,
            sourceElementOrdinal: sourceElementOrdinal
        );
        if (!resolved)
        {
            state.AddIssue(
                "WDG023",
                WordDependencyIssueSeverity.Error,
                "An abstract-numbering dependency refers to a missing style.",
                edgeId: edgeId,
                partUri: numbering.NumberingPartUri,
                sourceElementOrdinal: sourceElementOrdinal
            );
        }
    }

    private static void AddNumberingLevelDependencies(
        BuildState state,
        WordStyleGraph styles,
        WordNumberingGraph numbering,
        string sourceNodeId,
        WordNumberingLevelDefinition level,
        string ownerQualifier,
        bool stylesReachable,
        bool numberingReachable
    )
    {
        if (level.ParagraphStyleId is { Length: > 0 } styleId)
        {
            var resolved = styles.TryGetStyle(styleId, out _);
            var targetNodeId = StyleNode(
                state,
                styles,
                styleId,
                resolved,
                stylesReachable
            );
            var edgeId = state.AddEdge(
                WordDependencyEdgeKind.NumberingLevelUsesStyle,
                sourceNodeId,
                targetNodeId,
                resolved,
                isExternal: false,
                qualifier: $"{ownerQualifier}:level:{level.LevelIndex}",
                partUri: numbering.NumberingPartUri,
                sourceElementOrdinal: level.SourceElementOrdinal
            );
            if (!resolved)
            {
                state.AddIssue(
                    "WDG024",
                    WordDependencyIssueSeverity.Error,
                    "A numbering level refers to a missing paragraph style.",
                    edgeId: edgeId,
                    partUri: numbering.NumberingPartUri,
                    sourceElementOrdinal: level.SourceElementOrdinal
                );
            }
        }
        if (level.PictureBulletId is { } pictureBulletId)
        {
            var resolved = numbering.TryGetPictureBullet(pictureBulletId, out _);
            var targetNodeId = PictureBulletNode(
                state,
                numbering,
                pictureBulletId,
                resolved,
                numberingReachable
            );
            var edgeId = state.AddEdge(
                WordDependencyEdgeKind.NumberingUsesPictureBullet,
                sourceNodeId,
                targetNodeId,
                resolved,
                isExternal: false,
                qualifier: $"{ownerQualifier}:level:{level.LevelIndex}",
                partUri: numbering.NumberingPartUri,
                sourceElementOrdinal: level.SourceElementOrdinal
            );
            if (!resolved)
            {
                state.AddIssue(
                    "WDG025",
                    WordDependencyIssueSeverity.Error,
                    "A numbering level refers to a missing picture-bullet definition.",
                    edgeId: edgeId,
                    partUri: numbering.NumberingPartUri,
                    sourceElementOrdinal: level.SourceElementOrdinal
                );
            }
        }
    }

    private static void AddStyleNumberingReference(
        BuildState state,
        WordStyleGraph styles,
        WordNumberingGraph numbering,
        WordStyleDefinition style,
        WordStylePropertySet properties,
        bool stylesReachable,
        bool numberingReachable
    )
    {
        if (!properties.Values.TryGetValue("numbering_id", out var numberIdText))
        {
            return;
        }
        var parsed = int.TryParse(
            numberIdText,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var numberId
        );
        var resolved = parsed && numbering.TryGetInstance(numberId, out _);
        var sourceNodeId = state.AddNode(
            WordDependencyNodeKind.Style,
            style.StyleId,
            isResolved: true,
            isExternal: false,
            isPackageReachable: stylesReachable,
            partUri: styles.StylesPartUri,
            sourceElementOrdinal: style.SourceElementOrdinal
        );
        var targetNodeId = parsed
            ? NumberingInstanceNode(
                state,
                numbering,
                numberId,
                resolved,
                numberingReachable
            )
            : state.AddNode(
                WordDependencyNodeKind.NumberingInstance,
                numberIdText,
                isResolved: false,
                isExternal: false,
                isPackageReachable: numbering.NumberingPartUri is not null,
                partUri: numbering.NumberingPartUri
            );
        var edgeId = state.AddEdge(
            WordDependencyEdgeKind.StyleUsesNumbering,
            sourceNodeId,
            targetNodeId,
            resolved,
            isExternal: false,
            qualifier: properties.Values.TryGetValue("numbering_level", out var level)
                ? level
                : null,
            partUri: styles.StylesPartUri,
            sourceElementOrdinal: style.SourceElementOrdinal
        );
        if (!resolved)
        {
            state.AddIssue(
                "WDG026",
                WordDependencyIssueSeverity.Error,
                "A style refers to a missing or invalid numbering instance.",
                edgeId: edgeId,
                partUri: styles.StylesPartUri,
                sourceElementOrdinal: style.SourceElementOrdinal
            );
        }
    }

    private static void AddReferenceDependencies(
        BuildState state,
        OpcPackageSnapshot package,
        WordStyleGraph styles,
        WordReferenceGraph references,
        WordBibliographyGraph bibliography,
        IReadOnlyDictionary<string, string> bibliographySourceNodes,
        WordDocumentPropertyGraph documentProperties,
        DocumentMetadataDependencyNodes documentMetadataNodes,
        IReadOnlySet<string> reachableParts,
        CancellationToken cancellationToken
    )
    {
        var stylesReachable = styles.StylesPartUri is { } stylesPartUri
            && reachableParts.Contains(stylesPartUri);
        var storyNodes = new Dictionary<string, string>(StringComparer.Ordinal);
        var bookmarkNodes = new Dictionary<string, string>(StringComparer.Ordinal);
        var fieldNodes = new Dictionary<string, string>(StringComparer.Ordinal);
        var fieldsById = references.Fields.ToDictionary(
            field => field.Id,
            StringComparer.Ordinal
        );
        foreach (var story in references.Stories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var storyNodeId = state.AddNode(
                WordDependencyNodeKind.Story,
                story.Id,
                isResolved: true,
                isExternal: false,
                isPackageReachable: reachableParts.Contains(story.PartUri),
                partUri: story.PartUri,
                sourceElementOrdinal: story.RootElementOrdinal
            );
            storyNodes[story.Id] = storyNodeId;
            var partNodeId = PartNode(state, package, reachableParts, story.PartUri);
            state.AddEdge(
                WordDependencyEdgeKind.DefinesStory,
                partNodeId,
                storyNodeId,
                isResolved: package.Parts.ContainsKey(story.PartUri),
                isExternal: false,
                qualifier: story.Kind.ToString().ToLowerInvariant(),
                partUri: story.PartUri,
                sourceElementOrdinal: story.RootElementOrdinal
            );
        }
        foreach (var bookmark in references.Bookmarks)
        {
            var bookmarkNodeId = state.AddNode(
                WordDependencyNodeKind.Bookmark,
                bookmark.Id,
                isResolved: bookmark.IsComplete,
                isExternal: false,
                isPackageReachable: reachableParts.Contains(bookmark.PartUri),
                partUri: bookmark.PartUri,
                sourceElementOrdinal: bookmark.StartElementOrdinal,
                semanticNodeId: bookmark.StartNodeId
            );
            bookmarkNodes[bookmark.Id] = bookmarkNodeId;
            if (storyNodes.TryGetValue(bookmark.StoryId, out var storyNodeId))
            {
                state.AddEdge(
                    WordDependencyEdgeKind.StoryContainsBookmark,
                    storyNodeId,
                    bookmarkNodeId,
                    bookmark.IsComplete,
                    isExternal: false,
                    qualifier: bookmark.Status.ToString().ToLowerInvariant(),
                    partUri: bookmark.PartUri,
                    sourceElementOrdinal: bookmark.StartElementOrdinal
                );
            }
        }
        foreach (var field in references.Fields)
        {
            var fieldNodeId = state.AddNode(
                WordDependencyNodeKind.Field,
                field.Id,
                isResolved: field.Status == WordFieldStatus.Complete
                    && field.InstructionParseComplete,
                isExternal: field.RequiresExternalAccess,
                isPackageReachable: reachableParts.Contains(field.PartUri),
                partUri: field.PartUri,
                sourceElementOrdinal: field.StartElementOrdinal,
                semanticNodeId: field.StartNodeId
            );
            fieldNodes[field.Id] = fieldNodeId;
            if (storyNodes.TryGetValue(field.StoryId, out var storyNodeId))
            {
                state.AddEdge(
                    WordDependencyEdgeKind.StoryContainsField,
                    storyNodeId,
                    fieldNodeId,
                    field.Status == WordFieldStatus.Complete,
                    field.RequiresExternalAccess,
                    qualifier: field.FieldType,
                    partUri: field.PartUri,
                    sourceElementOrdinal: field.StartElementOrdinal
                );
            }
        }
        foreach (var field in references.Fields.Where(item => item.ParentFieldId is not null))
        {
            if (
                fieldNodes.TryGetValue(field.ParentFieldId!, out var parentNodeId)
                && fieldNodes.TryGetValue(field.Id, out var childNodeId)
            )
            {
                state.AddEdge(
                    WordDependencyEdgeKind.FieldContainsField,
                    parentNodeId,
                    childNodeId,
                    isResolved: true,
                    isExternal: false,
                    qualifier: field.FieldType,
                    partUri: field.PartUri,
                    sourceElementOrdinal: field.StartElementOrdinal
                );
            }
        }
        foreach (var edge in references.Edges)
        {
            if (!fieldNodes.TryGetValue(edge.SourceFieldId, out var sourceNodeId))
            {
                continue;
            }
            if (
                edge.TargetKind == WordReferenceTargetKind.IndexEntry
                && fieldsById.TryGetValue(edge.SourceFieldId, out var indexEntry)
                && string.Equals(
                    indexEntry.FieldType,
                    "XE",
                    StringComparison.OrdinalIgnoreCase
                )
                && !indexEntry.IsInDeletedContent
                && TryIndexEntryType(indexEntry.Tokens, out var entryType)
            )
            {
                var indexTargets = references.Fields
                    .Where(field =>
                        !field.IsInDeletedContent
                        && field.Status == WordFieldStatus.Complete
                        && field.InstructionParseComplete
                        && string.Equals(
                            field.FieldType,
                            "INDEX",
                            StringComparison.OrdinalIgnoreCase
                        )
                        && TryIndexEntryType(field.Tokens, out var indexType)
                        && string.Equals(
                            entryType,
                            indexType,
                            StringComparison.OrdinalIgnoreCase
                        )
                        && fieldNodes.ContainsKey(field.Id)
                    )
                    .ToArray();
                if (indexTargets.Length > 0)
                {
                    foreach (var indexTarget in indexTargets)
                    {
                        state.AddEdge(
                            WordDependencyEdgeKind.FieldReference,
                            sourceNodeId,
                            fieldNodes[indexTarget.Id],
                            isResolved: true,
                            isExternal: false,
                            qualifier: $"{edge.Kind}:{edge.TargetKind}:typed_native_index",
                            relationshipId: edge.Id
                        );
                    }
                    continue;
                }
            }
            if (
                edge.TargetKind == WordReferenceTargetKind.IndexEntry
                && fieldsById.TryGetValue(edge.SourceFieldId, out var authorityEntry)
                && string.Equals(
                    authorityEntry.FieldType,
                    "TA",
                    StringComparison.OrdinalIgnoreCase
                )
                && !authorityEntry.IsInDeletedContent
                && TryAuthorityCategory(
                    authorityEntry.Tokens,
                    defaultCategory: 1,
                    minimum: 1,
                    maximum: 16,
                    out var authorityCategory
                )
            )
            {
                var tableTargets = references.Fields
                    .Where(field =>
                        !field.IsInDeletedContent
                        && field.Status == WordFieldStatus.Complete
                        && field.InstructionParseComplete
                        && string.Equals(
                            field.FieldType,
                            "TOA",
                            StringComparison.OrdinalIgnoreCase
                        )
                        && TryAuthorityCategory(
                            field.Tokens,
                            defaultCategory: 1,
                            minimum: 0,
                            maximum: 16,
                            out var tableCategory
                        )
                        && (tableCategory == 0 || tableCategory == authorityCategory)
                        && fieldNodes.ContainsKey(field.Id)
                    )
                    .ToArray();
                if (tableTargets.Length > 0)
                {
                    foreach (var tableTarget in tableTargets)
                    {
                        state.AddEdge(
                            WordDependencyEdgeKind.FieldReference,
                            sourceNodeId,
                            fieldNodes[tableTarget.Id],
                            isResolved: true,
                            isExternal: false,
                            qualifier: $"{edge.Kind}:{edge.TargetKind}:category={authorityCategory}",
                            relationshipId: edge.Id
                        );
                    }
                    continue;
                }
            }
            string targetNodeId;
            var resolved = edge.IsResolved;
            if (
                edge.TargetKind == WordReferenceTargetKind.Bookmark
                && edge.ResolvedBookmarkId is { } bookmarkId
                && bookmarkNodes.TryGetValue(bookmarkId, out var resolvedBookmarkNodeId)
            )
            {
                targetNodeId = resolvedBookmarkNodeId;
            }
            else if (edge.TargetKind == WordReferenceTargetKind.Style)
            {
                var styleResolved = styles.TryGetStyle(edge.TargetKey, out _);
                targetNodeId = StyleNode(
                    state,
                    styles,
                    edge.TargetKey,
                    styleResolved,
                    stylesReachable
                );
            }
            else if (
                edge.TargetKind == WordReferenceTargetKind.Citation
                && bibliography.TryResolveCitationTag(edge.TargetKey, out var source)
                && source is not null
                && bibliographySourceNodes.TryGetValue(source.Id, out var bibliographyNodeId)
            )
            {
                targetNodeId = bibliographyNodeId;
                resolved = true;
            }
            else if (
                edge.TargetKind == WordReferenceTargetKind.DocumentProperty
                && documentProperties.TryResolveFieldProperty(
                    edge.TargetKey,
                    out var property
                )
                && property is not null
                && documentMetadataNodes.PropertiesById.TryGetValue(
                    property.Id,
                    out var propertyNodeId
                )
            )
            {
                targetNodeId = propertyNodeId;
                resolved = true;
            }
            else if (
                edge.TargetKind == WordReferenceTargetKind.DocumentVariable
                && edge.Kind == WordReferenceEdgeKind.Reads
                && documentMetadataNodes.VariablesByName.TryGetValue(
                    edge.TargetKey,
                    out var variableNodeId
                )
            )
            {
                targetNodeId = variableNodeId;
                resolved = true;
            }
            else
            {
                targetNodeId = state.AddNode(
                    edge.TargetKind == WordReferenceTargetKind.Bookmark
                        ? WordDependencyNodeKind.Bookmark
                        : WordDependencyNodeKind.ReferenceTarget,
                    $"{edge.TargetKind}:{edge.TargetKey}",
                    edge.IsResolved,
                    edge.IsExternal,
                    isPackageReachable: false
                );
            }
            var dependencyEdgeId = state.AddEdge(
                WordDependencyEdgeKind.FieldReference,
                sourceNodeId,
                targetNodeId,
                resolved,
                edge.IsExternal,
                qualifier: $"{edge.Kind}:{edge.TargetKind}",
                relationshipId: edge.Id
            );
            if (!resolved && !edge.IsExternal)
            {
                state.AddIssue(
                    "WDG030",
                    WordDependencyIssueSeverity.Warning,
                    "A field dependency could not be resolved inside the document graph.",
                    edgeId: dependencyEdgeId
                );
            }
        }
    }

    private static bool TryAuthorityCategory(
        IReadOnlyList<WordFieldToken> tokens,
        int defaultCategory,
        int minimum,
        int maximum,
        out int category
    )
    {
        var indexes = tokens
            .Select((token, index) => (token, index))
            .Where(item =>
                item.token.Kind == WordFieldTokenKind.Switch
                && string.Equals(
                    item.token.Value,
                    "\\c",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            .Select(item => item.index)
            .ToArray();
        if (indexes.Length == 0)
        {
            category = defaultCategory;
            return true;
        }
        if (
            indexes.Length != 1
            || indexes[0] + 1 >= tokens.Count
            || tokens[indexes[0] + 1].Kind == WordFieldTokenKind.Switch
            || !int.TryParse(
                tokens[indexes[0] + 1].Value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out category
            )
            || category < minimum
            || category > maximum
        )
        {
            category = default;
            return false;
        }
        return true;
    }

    private static bool TryIndexEntryType(
        IReadOnlyList<WordFieldToken> tokens,
        out string entryType
    )
    {
        var indexes = tokens
            .Select((token, index) => (token, index))
            .Where(item =>
                item.token.Kind == WordFieldTokenKind.Switch
                && string.Equals(item.token.Value, "\\f", StringComparison.OrdinalIgnoreCase)
            )
            .Select(item => item.index)
            .ToArray();
        if (indexes.Length == 0)
        {
            entryType = "i";
            return true;
        }
        if (
            indexes.Length != 1
            || indexes[0] + 1 >= tokens.Count
            || tokens[indexes[0] + 1].Kind == WordFieldTokenKind.Switch
            || string.IsNullOrWhiteSpace(tokens[indexes[0] + 1].Value)
        )
        {
            entryType = "";
            return false;
        }
        entryType = tokens[indexes[0] + 1].Value;
        return true;
    }

    private static void AddSectionDependencies(
        BuildState state,
        OpcPackageSnapshot package,
        WordSemanticDocument semanticDocument,
        WordSectionGraph sections,
        IReadOnlySet<string> reachableParts,
        CancellationToken cancellationToken
    )
    {
        var mainPartNodeId = PartNode(
            state,
            package,
            reachableParts,
            semanticDocument.MainPartUri
        );
        foreach (var section in sections.Sections)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sectionNodeId = state.AddNode(
                WordDependencyNodeKind.Section,
                section.Ordinal.ToString(CultureInfo.InvariantCulture),
                isResolved: true,
                isExternal: false,
                isPackageReachable: reachableParts.Contains(semanticDocument.MainPartUri),
                partUri: semanticDocument.MainPartUri,
                semanticNodeId: section.NodeId,
                semanticKind: WordSemanticNodeKind.Section
            );
            state.AddEdge(
                WordDependencyEdgeKind.DefinesSection,
                mainPartNodeId,
                sectionNodeId,
                isResolved: true,
                isExternal: false,
                qualifier: section.IsImplicit ? "implicit" : "explicit",
                partUri: semanticDocument.MainPartUri
            );
            foreach (var binding in section.Bindings.Where(item => item.PartUri is not null))
            {
                var targetPartUri = binding.PartUri!;
                var resolved = package.Parts.ContainsKey(targetPartUri);
                var targetNodeId = PartNode(
                    state,
                    package,
                    reachableParts,
                    targetPartUri
                );
                var edgeId = state.AddEdge(
                    WordDependencyEdgeKind.SectionBindsStory,
                    sectionNodeId,
                    targetNodeId,
                    resolved,
                    isExternal: false,
                    qualifier: $"{binding.Kind}:{binding.Variant}:{binding.Origin}",
                    partUri: semanticDocument.MainPartUri,
                    relationshipId: binding.RelationshipId
                );
                if (!resolved)
                {
                    state.AddIssue(
                        "WDG040",
                        WordDependencyIssueSeverity.Error,
                        "A section header/footer binding does not resolve to an existing part.",
                        edgeId: edgeId,
                        partUri: semanticDocument.MainPartUri
                    );
                }
            }
        }
        foreach (var partUri in sections.UnboundStoryPartUris)
        {
            var nodeId = PartNode(state, package, reachableParts, partUri);
            state.AddIssue(
                "WDG041",
                WordDependencyIssueSeverity.Warning,
                "A header or footer part is not bound to any effective document section.",
                nodeId: nodeId,
                partUri: partUri
            );
        }
    }

    private static IReadOnlySet<string> PackageReachableParts(
        OpcPackageSnapshot package,
        CancellationToken cancellationToken
    )
    {
        var reachable = new HashSet<string>(StringComparer.Ordinal);
        var pendingSources = new Queue<string>();
        pendingSources.Enqueue("/");
        var visitedSources = new HashSet<string>(StringComparer.Ordinal) { "/" };
        while (pendingSources.TryDequeue(out var sourcePartUri))
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var relationship in package.RelationshipsFrom(sourcePartUri))
            {
                if (
                    relationship.TargetMode != OpcRelationshipTargetMode.Internal
                    || relationship.ResolvedTargetPartUri is not { } targetPartUri
                    || !package.Parts.ContainsKey(targetPartUri)
                )
                {
                    continue;
                }
                reachable.Add(targetPartUri);
                if (visitedSources.Add(targetPartUri))
                {
                    pendingSources.Enqueue(targetPartUri);
                }
            }
        }
        return reachable;
    }

    private static string PartNode(
        BuildState state,
        OpcPackageSnapshot package,
        IReadOnlySet<string> reachableParts,
        string partUri
    ) => state.AddNode(
        WordDependencyNodeKind.Part,
        partUri,
        package.Parts.ContainsKey(partUri),
        isExternal: false,
        reachableParts.Contains(partUri),
        partUri: partUri
    );

    private static string SemanticNode(
        BuildState state,
        WordSemanticNode node,
        bool isPackageReachable
    ) => state.AddNode(
        WordDependencyNodeKind.SemanticNode,
        node.Id.Value,
        isResolved: true,
        isExternal: false,
        isPackageReachable,
        partUri: node.SourcePartUri,
        sourceElementOrdinal: node.SourceElementOrdinal,
        semanticNodeId: node.Id,
        semanticKind: node.Kind
    );

    private static string StyleNode(
        BuildState state,
        WordStyleGraph styles,
        string styleId,
        bool resolved,
        bool isPackageReachable
    ) => state.AddNode(
        WordDependencyNodeKind.Style,
        styleId,
        resolved,
        isExternal: false,
        isPackageReachable,
        partUri: styles.StylesPartUri
    );

    private static string AbstractNumberingNode(
        BuildState state,
        WordNumberingGraph numbering,
        int abstractNumberId,
        bool resolved,
        bool isPackageReachable
    ) => state.AddNode(
        WordDependencyNodeKind.AbstractNumbering,
        abstractNumberId.ToString(CultureInfo.InvariantCulture),
        resolved,
        isExternal: false,
        isPackageReachable,
        partUri: numbering.NumberingPartUri
    );

    private static string NumberingInstanceNode(
        BuildState state,
        WordNumberingGraph numbering,
        int numberId,
        bool resolved,
        bool isPackageReachable
    ) => state.AddNode(
        WordDependencyNodeKind.NumberingInstance,
        numberId.ToString(CultureInfo.InvariantCulture),
        resolved,
        isExternal: false,
        isPackageReachable,
        partUri: numbering.NumberingPartUri
    );

    private static string PictureBulletNode(
        BuildState state,
        WordNumberingGraph numbering,
        int pictureBulletId,
        bool resolved,
        bool isPackageReachable
    ) => state.AddNode(
        WordDependencyNodeKind.PictureBullet,
        pictureBulletId.ToString(CultureInfo.InvariantCulture),
        resolved,
        isExternal: false,
        isPackageReachable,
        partUri: numbering.NumberingPartUri
    );

    private static void EnsureFingerprint(string expected, params string[] actual)
    {
        if (actual.Any(value => !string.Equals(expected, value, StringComparison.Ordinal)))
        {
            throw new WordDependencyProjectionException(
                "Dependency inputs do not belong to the same package fingerprint."
            );
        }
    }

    private sealed class BuildState
    {
        private const long BaseAccountedBytes = 4_096;
        private const long NodeFixedAccountedBytes = 320;
        private const long EdgeFixedAccountedBytes = 352;
        private const long IssueFixedAccountedBytes = 192;
        private const string AccountingModel = "dependency_graph_accounted_v1";

        private readonly WordDependencyGraphOptions _options;
        private readonly WordOperationResourceLease? _resourceLease;
        private readonly Dictionary<NodeKey, NodeDraft> _nodes = new();
        private readonly HashSet<string> _nodeIds = new(StringComparer.Ordinal);
        private readonly Dictionary<string, EdgeDraft> _edges = new(StringComparer.Ordinal);
        private readonly List<WordDependencyIssue> _issues = [];
        private long _accountedBytes = BaseAccountedBytes;

        public BuildState(
            WordDependencyGraphOptions options,
            WordOperationResourceLease? resourceLease
        )
        {
            _options = options;
            _resourceLease = resourceLease;
            _resourceLease?.Charge(
                WordOperationResourceStage.DependencyGraph,
                BaseAccountedBytes
            );
        }

        public string AddNode(
            WordDependencyNodeKind kind,
            string key,
            bool isResolved,
            bool isExternal,
            bool isPackageReachable,
            string? partUri = null,
            int? sourceElementOrdinal = null,
            SemanticNodeId? semanticNodeId = null,
            WordSemanticNodeKind? semanticKind = null
        )
        {
            if (key.Length > _options.MaxKeyCharacters)
            {
                throw new WordDependencyLimitException(
                    $"A dependency key exceeds {_options.MaxKeyCharacters} characters."
                );
            }
            EnsureMetadataLength(partUri);
            EnsureMetadataLength(semanticNodeId?.Value);
            var nodeKey = new NodeKey(kind, key);
            if (_nodes.TryGetValue(nodeKey, out var existing))
            {
                ChargeOptionalMetadata(existing.PartUri, partUri);
                ChargeOptionalMetadata(existing.SemanticNodeId?.Value, semanticNodeId?.Value);
                existing.IsResolved |= isResolved;
                existing.IsExternal |= isExternal;
                existing.IsPackageReachable |= isPackageReachable;
                existing.PartUri ??= partUri;
                existing.SourceElementOrdinal ??= sourceElementOrdinal;
                existing.SemanticNodeId ??= semanticNodeId;
                existing.SemanticKind ??= semanticKind;
                return existing.Id;
            }
            if (_nodes.Count >= _options.MaxNodes)
            {
                throw new WordDependencyLimitException(
                    $"Dependency graph exceeds the {_options.MaxNodes}-node limit."
                );
            }
            var id = StableId("wddn_", kind.ToString(), key);
            Charge(
                NodeFixedAccountedBytes
                    + AccountedStringBytes(id)
                    + AccountedStringBytes(key)
                    + AccountedStringBytes(partUri)
                    + AccountedStringBytes(semanticNodeId?.Value)
            );
            if (!_nodeIds.Add(id))
            {
                throw new WordDependencyProjectionException(
                    "A stable dependency-node ID collision was detected."
                );
            }
            _nodes[nodeKey] = new NodeDraft(
                id,
                kind,
                key,
                isResolved,
                isExternal,
                isPackageReachable,
                partUri,
                sourceElementOrdinal,
                semanticNodeId,
                semanticKind
            );
            return id;
        }

        public string AddEdge(
            WordDependencyEdgeKind kind,
            string sourceNodeId,
            string targetNodeId,
            bool isResolved,
            bool isExternal,
            string? qualifier = null,
            string? partUri = null,
            int? sourceElementOrdinal = null,
            string? relationshipId = null,
            string? relationshipType = null
        )
        {
            EnsureMetadataLength(qualifier);
            EnsureMetadataLength(partUri);
            EnsureMetadataLength(relationshipId);
            EnsureMetadataLength(relationshipType);
            var id = StableId(
                "wdde_",
                kind.ToString(),
                sourceNodeId,
                targetNodeId,
                qualifier ?? "",
                partUri ?? "",
                sourceElementOrdinal?.ToString(CultureInfo.InvariantCulture) ?? "",
                relationshipId ?? "",
                relationshipType ?? ""
            );
            if (_edges.TryGetValue(id, out var existing))
            {
                if (
                    existing.Kind != kind
                    || existing.SourceNodeId != sourceNodeId
                    || existing.TargetNodeId != targetNodeId
                    || existing.Qualifier != qualifier
                    || existing.PartUri != partUri
                    || existing.SourceElementOrdinal != sourceElementOrdinal
                    || existing.RelationshipId != relationshipId
                    || existing.RelationshipType != relationshipType
                )
                {
                    throw new WordDependencyProjectionException(
                        "A stable dependency-edge ID collision was detected."
                    );
                }
                existing.IsResolved |= isResolved;
                existing.IsExternal |= isExternal;
                return id;
            }
            if (_edges.Count >= _options.MaxEdges)
            {
                throw new WordDependencyLimitException(
                    $"Dependency graph exceeds the {_options.MaxEdges}-edge limit."
                );
            }
            Charge(
                EdgeFixedAccountedBytes
                    + AccountedStringBytes(id)
                    + AccountedStringBytes(sourceNodeId)
                    + AccountedStringBytes(targetNodeId)
                    + AccountedStringBytes(qualifier)
                    + AccountedStringBytes(partUri)
                    + AccountedStringBytes(relationshipId)
                    + AccountedStringBytes(relationshipType)
            );
            _edges[id] = new EdgeDraft(
                id,
                kind,
                sourceNodeId,
                targetNodeId,
                isResolved,
                isExternal,
                qualifier,
                partUri,
                sourceElementOrdinal,
                relationshipId,
                relationshipType
            );
            return id;
        }

        public void AddIssue(
            string code,
            WordDependencyIssueSeverity severity,
            string message,
            string? nodeId = null,
            string? edgeId = null,
            string? partUri = null,
            int? sourceElementOrdinal = null
        )
        {
            EnsureMetadataLength(code);
            EnsureMetadataLength(message);
            EnsureMetadataLength(nodeId);
            EnsureMetadataLength(edgeId);
            EnsureMetadataLength(partUri);
            if (_issues.Count >= _options.MaxIssues)
            {
                throw new WordDependencyLimitException(
                    $"Dependency graph exceeds the {_options.MaxIssues}-issue limit."
                );
            }
            Charge(
                IssueFixedAccountedBytes
                    + AccountedStringBytes(code)
                    + AccountedStringBytes(message)
                    + AccountedStringBytes(nodeId)
                    + AccountedStringBytes(edgeId)
                    + AccountedStringBytes(partUri)
            );
            _issues.Add(
                new WordDependencyIssue(
                    code,
                    severity,
                    message,
                    nodeId,
                    edgeId,
                    partUri,
                    sourceElementOrdinal
                )
            );
        }

        public (
            IReadOnlyList<WordDependencyNode> Nodes,
            IReadOnlyList<WordDependencyEdge> Edges,
            IReadOnlyList<WordDependencyIssue> Issues,
            WordDependencyResourceUsage ResourceUsage
        ) Materialize()
        {
            var nodes = _nodes.Values
                .OrderBy(node => node.Kind)
                .ThenBy(node => node.Key, StringComparer.Ordinal)
                .Select(node => node.ToRecord())
                .ToArray();
            var edges = _edges.Values
                .OrderBy(edge => edge.Kind)
                .ThenBy(edge => edge.SourceNodeId, StringComparer.Ordinal)
                .ThenBy(edge => edge.TargetNodeId, StringComparer.Ordinal)
                .ThenBy(edge => edge.Id, StringComparer.Ordinal)
                .Select(edge => edge.ToRecord())
                .ToArray();
            var adjacencyIndexBytes = checked(
                ((long)nodes.Length + 1L) * 2L * sizeof(int)
                    + (long)edges.Length * 2L * sizeof(int)
            );
            return (
                nodes,
                edges,
                _issues.OrderByDescending(issue => issue.Severity)
                    .ThenBy(issue => issue.Code, StringComparer.Ordinal)
                    .ThenBy(issue => issue.EdgeId, StringComparer.Ordinal)
                    .ThenBy(issue => issue.NodeId, StringComparer.Ordinal)
                    .ToArray(),
                new WordDependencyResourceUsage(
                    AccountingModel,
                    _accountedBytes,
                    _options.MaxAccountedBytes,
                    nodes.Length,
                    edges.Length,
                    _issues.Count,
                    adjacencyIndexBytes
                )
            );
        }

        private void ChargeOptionalMetadata(string? existing, string? candidate)
        {
            if (existing is null && candidate is not null)
            {
                Charge(AccountedStringBytes(candidate));
            }
        }

        private void EnsureMetadataLength(string? value)
        {
            if (value is not null && value.Length > _options.MaxMetadataCharacters)
            {
                throw new WordDependencyLimitException(
                    $"A dependency metadata value exceeds {_options.MaxMetadataCharacters} characters."
                );
            }
        }

        private void Charge(long bytes)
        {
            if (bytes < 0 || _accountedBytes > _options.MaxAccountedBytes - bytes)
            {
                throw new WordDependencyLimitException(
                    $"Dependency graph exceeds the {_options.MaxAccountedBytes}-byte accounted budget."
                );
            }
            _resourceLease?.Charge(
                WordOperationResourceStage.DependencyGraph,
                bytes
            );
            _accountedBytes += bytes;
        }

        private static long AccountedStringBytes(string? value)
        {
            if (value is null)
            {
                return 0;
            }
            var unaligned = checked(24L + (long)value.Length * sizeof(char));
            return checked((unaligned + 7L) & ~7L);
        }

        private sealed class NodeDraft(
            string id,
            WordDependencyNodeKind kind,
            string key,
            bool isResolved,
            bool isExternal,
            bool isPackageReachable,
            string? partUri,
            int? sourceElementOrdinal,
            SemanticNodeId? semanticNodeId,
            WordSemanticNodeKind? semanticKind
        )
        {
            public string Id { get; } = id;
            public WordDependencyNodeKind Kind { get; } = kind;
            public string Key { get; } = key;
            public bool IsResolved { get; set; } = isResolved;
            public bool IsExternal { get; set; } = isExternal;
            public bool IsPackageReachable { get; set; } = isPackageReachable;
            public string? PartUri { get; set; } = partUri;
            public int? SourceElementOrdinal { get; set; } = sourceElementOrdinal;
            public SemanticNodeId? SemanticNodeId { get; set; } = semanticNodeId;
            public WordSemanticNodeKind? SemanticKind { get; set; } = semanticKind;

            public WordDependencyNode ToRecord() => new(
                Id,
                Kind,
                Key,
                IsResolved,
                IsExternal,
                IsPackageReachable,
                PartUri,
                SourceElementOrdinal,
                SemanticNodeId,
                SemanticKind
            );
        }

        private sealed class EdgeDraft(
            string id,
            WordDependencyEdgeKind kind,
            string sourceNodeId,
            string targetNodeId,
            bool isResolved,
            bool isExternal,
            string? qualifier,
            string? partUri,
            int? sourceElementOrdinal,
            string? relationshipId,
            string? relationshipType
        )
        {
            public string Id { get; } = id;
            public WordDependencyEdgeKind Kind { get; } = kind;
            public string SourceNodeId { get; } = sourceNodeId;
            public string TargetNodeId { get; } = targetNodeId;
            public bool IsResolved { get; set; } = isResolved;
            public bool IsExternal { get; set; } = isExternal;
            public string? Qualifier { get; } = qualifier;
            public string? PartUri { get; } = partUri;
            public int? SourceElementOrdinal { get; } = sourceElementOrdinal;
            public string? RelationshipId { get; } = relationshipId;
            public string? RelationshipType { get; } = relationshipType;

            public WordDependencyEdge ToRecord() => new(
                Id,
                Kind,
                SourceNodeId,
                TargetNodeId,
                IsResolved,
                IsExternal,
                Qualifier,
                PartUri,
                SourceElementOrdinal,
                RelationshipId,
                RelationshipType
            );
        }

        private readonly record struct NodeKey(WordDependencyNodeKind Kind, string Key);
    }

    private static string StableId(string prefix, params string[] values)
    {
        var material = string.Join('\u001f', values);
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        var encoded = Convert.ToBase64String(digest.AsSpan(0, 15))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        return prefix + encoded;
    }
}

public class WordDependencyProjectionException : IOException
{
    public WordDependencyProjectionException(string message)
        : base(message) { }
}

public sealed class WordDependencyLimitException : WordDependencyProjectionException
{
    public WordDependencyLimitException(string message)
        : base(message) { }
}
