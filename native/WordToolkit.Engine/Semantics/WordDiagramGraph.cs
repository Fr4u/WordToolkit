using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Resources;
using WordToolkit.Engine.Xml;

namespace WordToolkit.Engine.Semantics;

public enum WordDiagramPartKind
{
    Data,
    Layout,
    Colors,
    QuickStyle,
    PersistedDrawing,
}

public enum WordDiagramIssueSeverity
{
    Info,
    Warning,
    Error,
}

public sealed record WordDiagramIssue(
    string Code,
    WordDiagramIssueSeverity Severity,
    string Message,
    string? DiagramId = null,
    string? PartUri = null,
    int? SourceElementOrdinal = null,
    string? RelationshipId = null,
    string? PointId = null,
    string? ConnectionId = null
);

public sealed record WordDiagramPart(
    WordDiagramPartKind Kind,
    string PartUri,
    string ContentType,
    bool IsPackageReachable,
    string SourceSha256
);

public sealed record WordDiagramPartReference(
    WordDiagramPartKind Kind,
    string RelationshipId,
    string RelationshipType,
    string Target,
    OpcRelationshipTargetMode TargetMode,
    string? TargetPartUri,
    bool IsResolved
);

public sealed record WordDiagramPoint(
    string Id,
    string DiagramId,
    string ModelId,
    string? PointType,
    bool IsModelIdUnique,
    bool IsStructurallyValid,
    bool HasText,
    int TextCharacterCount,
    bool? IsPlaceholder,
    string? LayoutTypeId,
    string? QuickStyleTypeId,
    string? ColorStyleTypeId,
    string? PresentationAssociationId,
    string? PresentationName,
    string? PresentationStyleLabel,
    string PartUri,
    int SourceElementOrdinal
);

public sealed record WordDiagramConnection(
    string Id,
    string DiagramId,
    string ModelId,
    string SourceModelId,
    string DestinationModelId,
    string? ConnectionType,
    long? SourceOrder,
    long? DestinationOrder,
    bool IsModelIdUnique,
    bool SourceResolved,
    bool DestinationResolved,
    bool IsStructurallyValid,
    string PartUri,
    int SourceElementOrdinal
);

public sealed class WordDiagramDefinition
{
    internal WordDiagramDefinition(
        string id,
        string sourcePartUri,
        int sourceElementOrdinal,
        bool isPackageReachable,
        IReadOnlyList<WordDiagramPartReference> partReferences,
        IReadOnlyList<WordDiagramPoint> points,
        IReadOnlyList<WordDiagramConnection> connections,
        string? layoutUniqueId,
        string? layoutMinimumVersion,
        string? quickStyleUniqueId,
        string? quickStyleMinimumVersion,
        string? colorsUniqueId,
        string? colorsMinimumVersion,
        int persistedDrawingPartCount
    )
    {
        Id = id;
        SourcePartUri = sourcePartUri;
        SourceElementOrdinal = sourceElementOrdinal;
        IsPackageReachable = isPackageReachable;
        PartReferences = new ReadOnlyCollection<WordDiagramPartReference>(
            partReferences.ToArray()
        );
        Points = new ReadOnlyCollection<WordDiagramPoint>(points.ToArray());
        Connections = new ReadOnlyCollection<WordDiagramConnection>(
            connections.ToArray()
        );
        LayoutUniqueId = layoutUniqueId;
        LayoutMinimumVersion = layoutMinimumVersion;
        QuickStyleUniqueId = quickStyleUniqueId;
        QuickStyleMinimumVersion = quickStyleMinimumVersion;
        ColorsUniqueId = colorsUniqueId;
        ColorsMinimumVersion = colorsMinimumVersion;
        PersistedDrawingPartCount = persistedDrawingPartCount;
    }

    public string Id { get; }

    public string SourcePartUri { get; }

    public int SourceElementOrdinal { get; }

    public bool IsPackageReachable { get; }

    public IReadOnlyList<WordDiagramPartReference> PartReferences { get; }

    public IReadOnlyList<WordDiagramPoint> Points { get; }

    public IReadOnlyList<WordDiagramConnection> Connections { get; }

    public string? LayoutUniqueId { get; }

    public string? LayoutMinimumVersion { get; }

    public string? QuickStyleUniqueId { get; }

    public string? QuickStyleMinimumVersion { get; }

    public string? ColorsUniqueId { get; }

    public string? ColorsMinimumVersion { get; }

    public int PersistedDrawingPartCount { get; }

    public bool RequiredPartsResolved => PartReferences.Any(item =>
        item.Kind == WordDiagramPartKind.Data && item.IsResolved
    ) && PartReferences.Any(item =>
        item.Kind == WordDiagramPartKind.Layout && item.IsResolved
    );
}

public sealed class WordDiagramGraph
{
    private readonly IReadOnlyDictionary<string, WordDiagramDefinition> _diagramsById;

    internal WordDiagramGraph(
        string packageFingerprint,
        IReadOnlyList<WordDiagramPart> parts,
        IReadOnlyList<WordDiagramDefinition> diagrams,
        IReadOnlyList<WordDiagramIssue> issues,
        bool issuesTruncated
    )
    {
        PackageFingerprint = packageFingerprint;
        Parts = new ReadOnlyCollection<WordDiagramPart>(parts.ToArray());
        Diagrams = new ReadOnlyCollection<WordDiagramDefinition>(diagrams.ToArray());
        Issues = new ReadOnlyCollection<WordDiagramIssue>(issues.ToArray());
        IssuesTruncated = issuesTruncated;
        _diagramsById = new ReadOnlyDictionary<string, WordDiagramDefinition>(
            diagrams.ToDictionary(item => item.Id, StringComparer.Ordinal)
        );
    }

    public string PackageFingerprint { get; }

    public IReadOnlyList<WordDiagramPart> Parts { get; }

    public IReadOnlyList<WordDiagramDefinition> Diagrams { get; }

    public IReadOnlyList<WordDiagramIssue> Issues { get; }

    public bool IssuesTruncated { get; }

    public bool TryGetDiagram(string id, out WordDiagramDefinition? diagram)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return _diagramsById.TryGetValue(id, out diagram);
    }
}

public sealed record WordDiagramGraphOptions
{
    public static WordDiagramGraphOptions Default { get; } = new();

    public int MaxSourceParts { get; init; } = 10_000;

    public int MaxDiagramParts { get; init; } = 4_096;

    public int MaxDiagrams { get; init; } = 10_000;

    public int MaxPartBytes { get; init; } = 32 * 1024 * 1024;

    public int MaxTotalSourceXmlBytes { get; init; } = 128 * 1024 * 1024;

    public int MaxElementsPerPart { get; init; } = 500_000;

    public int MaxPointsPerDiagram { get; init; } = 100_000;

    public int MaxConnectionsPerDiagram { get; init; } = 200_000;

    public int MaxIdentifierCharacters { get; init; } = 4_096;

    public int MaxTotalTextCharacters { get; init; } = 16 * 1024 * 1024;

    public int MaxIssues { get; init; } = 10_000;

    internal void Validate()
    {
        if (MaxSourceParts <= 0) throw new ArgumentOutOfRangeException(nameof(MaxSourceParts));
        if (MaxDiagramParts <= 0) throw new ArgumentOutOfRangeException(nameof(MaxDiagramParts));
        if (MaxDiagrams <= 0) throw new ArgumentOutOfRangeException(nameof(MaxDiagrams));
        if (MaxPartBytes <= 0) throw new ArgumentOutOfRangeException(nameof(MaxPartBytes));
        if (MaxTotalSourceXmlBytes < MaxPartBytes) throw new ArgumentOutOfRangeException(nameof(MaxTotalSourceXmlBytes));
        if (MaxElementsPerPart <= 0) throw new ArgumentOutOfRangeException(nameof(MaxElementsPerPart));
        if (MaxPointsPerDiagram <= 0) throw new ArgumentOutOfRangeException(nameof(MaxPointsPerDiagram));
        if (MaxConnectionsPerDiagram <= 0) throw new ArgumentOutOfRangeException(nameof(MaxConnectionsPerDiagram));
        if (MaxIdentifierCharacters <= 0) throw new ArgumentOutOfRangeException(nameof(MaxIdentifierCharacters));
        if (MaxTotalTextCharacters <= 0) throw new ArgumentOutOfRangeException(nameof(MaxTotalTextCharacters));
        if (MaxIssues <= 0) throw new ArgumentOutOfRangeException(nameof(MaxIssues));
    }
}

public sealed class WordDiagramGraphBuilder
{
    private const string DiagramNamespace =
        "http://schemas.openxmlformats.org/drawingml/2006/diagram";
    private const string StrictDiagramNamespace =
        "http://purl.oclc.org/ooxml/drawingml/diagram";
    private const string DrawingNamespace =
        "http://schemas.openxmlformats.org/drawingml/2006/main";
    private const string StrictDrawingNamespace =
        "http://purl.oclc.org/ooxml/drawingml/main";
    private const string RelationshipNamespace =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string StrictRelationshipNamespace =
        "http://purl.oclc.org/ooxml/officeDocument/relationships";
    private const string OfficeDrawingDiagramNamespace =
        "http://schemas.microsoft.com/office/drawing/2008/diagram";

    private const string DataRelationship =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/diagramData";
    private const string LayoutRelationship =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/diagramLayout";
    private const string ColorsRelationship =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/diagramColors";
    private const string QuickStyleRelationship =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/diagramQuickStyle";
    private const string StrictDataRelationship =
        "http://purl.oclc.org/ooxml/officeDocument/relationships/diagramData";
    private const string StrictLayoutRelationship =
        "http://purl.oclc.org/ooxml/officeDocument/relationships/diagramLayout";
    private const string StrictColorsRelationship =
        "http://purl.oclc.org/ooxml/officeDocument/relationships/diagramColors";
    private const string StrictQuickStyleRelationship =
        "http://purl.oclc.org/ooxml/officeDocument/relationships/diagramQuickStyle";
    private const string PersistedDrawingRelationship =
        "http://schemas.microsoft.com/office/2007/relationships/diagramDrawing";

    private const string DataContentType =
        "application/vnd.openxmlformats-officedocument.drawingml.diagramData+xml";
    private const string LayoutContentType =
        "application/vnd.openxmlformats-officedocument.drawingml.diagramLayout+xml";
    private const string ColorsContentType =
        "application/vnd.openxmlformats-officedocument.drawingml.diagramColors+xml";
    private const string QuickStyleContentType =
        "application/vnd.openxmlformats-officedocument.drawingml.diagramStyle+xml";
    private const string PersistedDrawingContentType =
        "application/vnd.ms-office.drawingml.diagramDrawing+xml";

    private static readonly IReadOnlySet<string> DataRelationshipTypes =
        new HashSet<string>(StringComparer.Ordinal)
        {
            DataRelationship,
            StrictDataRelationship,
        };
    private static readonly IReadOnlySet<string> LayoutRelationshipTypes =
        new HashSet<string>(StringComparer.Ordinal)
        {
            LayoutRelationship,
            StrictLayoutRelationship,
        };
    private static readonly IReadOnlySet<string> ColorsRelationshipTypes =
        new HashSet<string>(StringComparer.Ordinal)
        {
            ColorsRelationship,
            StrictColorsRelationship,
        };
    private static readonly IReadOnlySet<string> QuickStyleRelationshipTypes =
        new HashSet<string>(StringComparer.Ordinal)
        {
            QuickStyleRelationship,
            StrictQuickStyleRelationship,
        };
    private static readonly IReadOnlySet<string> PersistedDrawingRelationshipTypes =
        new HashSet<string>(StringComparer.Ordinal)
        {
            PersistedDrawingRelationship,
        };
    private static readonly byte[] RelIdsUtf8 = Encoding.UTF8.GetBytes("relIds");
    private static readonly byte[] RelIdsUtf16LittleEndian = Encoding.Unicode.GetBytes(
        "relIds"
    );
    private static readonly byte[] RelIdsUtf16BigEndian = Encoding.BigEndianUnicode.GetBytes(
        "relIds"
    );
    private static readonly byte[] RelIdsUtf32LittleEndian = new UTF32Encoding(
        bigEndian: false,
        byteOrderMark: false
    ).GetBytes("relIds");
    private static readonly byte[] RelIdsUtf32BigEndian = new UTF32Encoding(
        bigEndian: true,
        byteOrderMark: false
    ).GetBytes("relIds");

    private readonly WordDiagramGraphOptions _options;
    private readonly WordOperationResourceLease? _resourceLease;
    private long _totalTextCharacters;

    public WordDiagramGraphBuilder(WordDiagramGraphOptions? options = null)
    {
        _options = options ?? WordDiagramGraphOptions.Default;
        _options.Validate();
    }

    public WordDiagramGraphBuilder(
        WordDiagramGraphOptions? options,
        WordOperationResourceLease resourceLease
    )
    {
        ArgumentNullException.ThrowIfNull(resourceLease);
        _options = options ?? WordDiagramGraphOptions.Default;
        _resourceLease = resourceLease;
        _options.Validate();
    }

    public WordDiagramGraph Build(
        OpcPackageSnapshot package,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(package);
        cancellationToken.ThrowIfCancellationRequested();
        _totalTextCharacters = 0;
        WordOperationResourceAccounting.ChargeProjectionBase(
            _resourceLease,
            WordOperationResourceStage.Diagrams
        );
        WordOperationResourceAccounting.ChargeItems(
            _resourceLease,
            WordOperationResourceStage.Diagrams,
            checked(package.Parts.Count + package.Relationships.Count),
            128
        );

        var issues = new IssueState(_options.MaxIssues);
        var reachable = PackageReachableParts(package, cancellationToken);
        var parts = BuildParts(package, reachable, cancellationToken);
        var references = FindDiagramReferenceElements(
            package,
            cancellationToken
        );
        if (references.Count > _options.MaxDiagrams)
        {
            throw new WordDiagramLimitException(
                $"Package contains {references.Count} diagram references; limit is {_options.MaxDiagrams}."
            );
        }

        var diagrams = new List<WordDiagramDefinition>(references.Count);
        foreach (var reference in references)
        {
            cancellationToken.ThrowIfCancellationRequested();
            diagrams.Add(ParseDiagram(
                package,
                reference,
                reachable,
                issues,
                cancellationToken
            ));
        }

        var referencedPartUris = diagrams.SelectMany(item => item.PartReferences)
            .Where(item => item.IsResolved && item.TargetPartUri is not null)
            .Select(item => item.TargetPartUri!)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var part in parts.Where(item =>
            !referencedPartUris.Contains(item.PartUri)
        ))
        {
            issues.Add(new WordDiagramIssue(
                "DGM_PART_UNREFERENCED",
                WordDiagramIssueSeverity.Info,
                "A typed SmartArt part is not referenced by a resolved dgm:relIds role.",
                PartUri: part.PartUri
            ));
        }

        return new WordDiagramGraph(
            package.Fingerprint,
            parts,
            diagrams,
            issues.Issues,
            issues.Truncated
        );
    }

    private IReadOnlyList<WordDiagramPart> BuildParts(
        OpcPackageSnapshot package,
        IReadOnlySet<string> reachable,
        CancellationToken cancellationToken
    )
    {
        var parts = package.Parts.Values
            .Select(part => (Part: part, Kind: PartKind(part.ContentType)))
            .Where(item => item.Kind is not null)
            .OrderBy(item => item.Part.Uri, StringComparer.Ordinal)
            .ToArray();
        if (parts.Length > _options.MaxDiagramParts)
        {
            throw new WordDiagramLimitException(
                $"Package contains {parts.Length} typed diagram parts; limit is {_options.MaxDiagramParts}."
            );
        }
        var result = new List<WordDiagramPart>(parts.Length);
        foreach (var item in parts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            result.Add(new WordDiagramPart(
                item.Kind!.Value,
                item.Part.Uri,
                item.Part.ContentType ?? string.Empty,
                reachable.Contains(item.Part.Uri),
                Convert.ToHexString(SHA256.HashData(item.Part.Entry.Content.Span))
                    .ToLowerInvariant()
            ));
        }
        return result;
    }

    private IReadOnlyList<ReferenceElement> FindDiagramReferenceElements(
        OpcPackageSnapshot package,
        CancellationToken cancellationToken
    )
    {
        var candidates = package.Parts.Values
            .Where(part => IsXmlContentType(part.ContentType))
            .Where(part => ContainsRelIdsToken(part.Entry.Content))
            .OrderBy(part => part.Uri, StringComparer.Ordinal)
            .ToArray();
        if (candidates.Length > _options.MaxSourceParts)
        {
            throw new WordDiagramLimitException(
                $"Package contains {candidates.Length} candidate diagram source parts; limit is {_options.MaxSourceParts}."
            );
        }
        var totalBytes = candidates.Sum(item => (long)item.Entry.Content.Length);
        if (totalBytes > _options.MaxTotalSourceXmlBytes)
        {
            throw new WordDiagramLimitException(
                "Candidate diagram source XML exceeds the aggregate byte limit."
            );
        }

        var result = new List<ReferenceElement>();
        foreach (var part in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = ParseXmlPart(part, cancellationToken);
            foreach (var element in source.ParsedDocument.Descendants().Where(item =>
                item.Name.LocalName == "relIds"
                && IsDiagramNamespace(item.Name.NamespaceName)
            ))
            {
                result.Add(new ReferenceElement(
                    part.Uri,
                    source.GetElementOrdinal(element),
                    RelationshipAttribute(element, "dm"),
                    RelationshipAttribute(element, "lo"),
                    RelationshipAttribute(element, "qs"),
                    RelationshipAttribute(element, "cs")
                ));
            }
        }
        return result;
    }

    private WordDiagramDefinition ParseDiagram(
        OpcPackageSnapshot package,
        ReferenceElement source,
        IReadOnlySet<string> reachable,
        IssueState issues,
        CancellationToken cancellationToken
    )
    {
        var diagramId = StableId(
            "wdd_",
            package.Fingerprint,
            source.SourcePartUri,
            source.SourceElementOrdinal.ToString(CultureInfo.InvariantCulture)
        );
        var references = new List<WordDiagramPartReference>(4);
        ResolvePartReference(
            package,
            diagramId,
            source,
            WordDiagramPartKind.Data,
            source.DataRelationshipId,
            required: true,
            references,
            issues
        );
        ResolvePartReference(
            package,
            diagramId,
            source,
            WordDiagramPartKind.Layout,
            source.LayoutRelationshipId,
            required: true,
            references,
            issues
        );
        ResolvePartReference(
            package,
            diagramId,
            source,
            WordDiagramPartKind.QuickStyle,
            source.QuickStyleRelationshipId,
            required: false,
            references,
            issues
        );
        ResolvePartReference(
            package,
            diagramId,
            source,
            WordDiagramPartKind.Colors,
            source.ColorsRelationshipId,
            required: false,
            references,
            issues
        );

        var dataReference = references.SingleOrDefault(item =>
            item.Kind == WordDiagramPartKind.Data && item.IsResolved
        );
        var points = Array.Empty<WordDiagramPoint>();
        var connections = Array.Empty<WordDiagramConnection>();
        if (
            dataReference?.TargetPartUri is { } dataPartUri
            && package.Parts.TryGetValue(dataPartUri, out var dataPart)
        )
        {
            var data = ParseDataPart(
                diagramId,
                dataPart,
                issues,
                cancellationToken
            );
            points = data.Points;
            connections = data.Connections;
            foreach (var relationshipId in data.PersistedDrawingRelationshipIds)
            {
                ResolvePartReference(
                    package,
                    diagramId,
                    source,
                    WordDiagramPartKind.PersistedDrawing,
                    relationshipId,
                    required: false,
                    references,
                    issues
                );
            }
        }
        var persistedDrawingCount = references.Count(item =>
            item.Kind == WordDiagramPartKind.PersistedDrawing && item.IsResolved
        );

        var layout = ReadDefinitionIdentity(
            package,
            references,
            WordDiagramPartKind.Layout,
            "layoutDef",
            cancellationToken
        );
        var quickStyle = ReadDefinitionIdentity(
            package,
            references,
            WordDiagramPartKind.QuickStyle,
            "styleDef",
            cancellationToken
        );
        var colors = ReadDefinitionIdentity(
            package,
            references,
            WordDiagramPartKind.Colors,
            "colorsDef",
            cancellationToken
        );

        return new WordDiagramDefinition(
            diagramId,
            source.SourcePartUri,
            source.SourceElementOrdinal,
            reachable.Contains(source.SourcePartUri),
            references,
            points,
            connections,
            layout.UniqueId,
            layout.MinimumVersion,
            quickStyle.UniqueId,
            quickStyle.MinimumVersion,
            colors.UniqueId,
            colors.MinimumVersion,
            persistedDrawingCount
        );
    }

    private void ResolvePartReference(
        OpcPackageSnapshot package,
        string diagramId,
        ReferenceElement source,
        WordDiagramPartKind kind,
        string? relationshipId,
        bool required,
        List<WordDiagramPartReference> result,
        IssueState issues
    )
    {
        if (string.IsNullOrWhiteSpace(relationshipId))
        {
            if (required)
            {
                issues.Add(new WordDiagramIssue(
                    "DGM_REQUIRED_RELATIONSHIP_MISSING",
                    WordDiagramIssueSeverity.Error,
                    $"SmartArt reference omits the required {kind.ToString().ToLowerInvariant()} relationship ID.",
                    diagramId,
                    source.SourcePartUri,
                    source.SourceElementOrdinal
                ));
            }
            return;
        }
        EnsureIdentifierLimit(relationshipId);
        var matches = package.RelationshipsFrom(source.SourcePartUri)
            .Where(item => item.Id == relationshipId)
            .ToArray();
        if (matches.Length != 1)
        {
            issues.Add(new WordDiagramIssue(
                "DGM_RELATIONSHIP_ID_AMBIGUOUS",
                WordDiagramIssueSeverity.Error,
                "SmartArt relationship ID is missing or ambiguous in its source part.",
                diagramId,
                source.SourcePartUri,
                source.SourceElementOrdinal,
                relationshipId
            ));
            result.Add(new WordDiagramPartReference(
                kind,
                relationshipId,
                string.Empty,
                string.Empty,
                OpcRelationshipTargetMode.Internal,
                null,
                false
            ));
            return;
        }
        var relationship = matches[0];
        var expectedRelationship = RelationshipTypes(kind);
        var expectedContentType = ContentType(kind);
        var resolved = relationship.TargetMode == OpcRelationshipTargetMode.Internal
            && expectedRelationship.Contains(relationship.Type)
            && relationship.ResolvedTargetPartUri is not null
            && package.Parts.TryGetValue(
                relationship.ResolvedTargetPartUri,
                out var target
            )
            && string.Equals(
                target.ContentType,
                expectedContentType,
                StringComparison.OrdinalIgnoreCase
            );
        if (!resolved)
        {
            issues.Add(new WordDiagramIssue(
                "DGM_RELATIONSHIP_UNRESOLVED",
                WordDiagramIssueSeverity.Error,
                $"SmartArt {kind.ToString().ToLowerInvariant()} relationship does not resolve internally with the exact expected type and content type.",
                diagramId,
                source.SourcePartUri,
                source.SourceElementOrdinal,
                relationshipId
            ));
        }
        result.Add(new WordDiagramPartReference(
            kind,
            relationship.Id,
            relationship.Type,
            relationship.Target,
            relationship.TargetMode,
            relationship.ResolvedTargetPartUri,
            resolved
        ));
    }

    private DataPartProjection ParseDataPart(
        string diagramId,
        OpcPart part,
        IssueState issues,
        CancellationToken cancellationToken
    )
    {
        var source = ParseXmlPart(part, cancellationToken);
        var root = source.ParsedDocument.Root;
        if (
            root is null
            || root.Name.LocalName != "dataModel"
            || !IsDiagramNamespace(root.Name.NamespaceName)
        )
        {
            throw new WordDiagramProjectionException(
                $"Diagram data part '{part.Uri}' does not have a dgm:dataModel root."
            );
        }
        var dgm = root.Name.Namespace;
        var pointLists = root.Elements(dgm + "ptLst").ToArray();
        var connectionLists = root.Elements(dgm + "cxnLst").ToArray();
        AddCardinalityIssue(
            issues,
            diagramId,
            part.Uri,
            "DGM_POINT_LIST_CARDINALITY",
            "SmartArt data model must contain exactly one point list.",
            pointLists.Length
        );
        AddCardinalityIssue(
            issues,
            diagramId,
            part.Uri,
            "DGM_CONNECTION_LIST_CARDINALITY",
            "SmartArt data model must contain exactly one connection list.",
            connectionLists.Length
        );
        AddCardinalityIssue(
            issues,
            diagramId,
            part.Uri,
            "DGM_BACKGROUND_CARDINALITY",
            "SmartArt data model must contain exactly one background formatting element.",
            root.Elements(dgm + "bg").Count()
        );
        AddCardinalityIssue(
            issues,
            diagramId,
            part.Uri,
            "DGM_WHOLE_CARDINALITY",
            "SmartArt data model must contain exactly one whole-diagram formatting element.",
            root.Elements(dgm + "whole").Count()
        );
        var pointElements = pointLists
            .SelectMany(item => item.Elements(dgm + "pt"))
            .ToArray();
        var connectionElements = connectionLists
            .SelectMany(item => item.Elements(dgm + "cxn"))
            .ToArray();
        if (pointElements.Length > _options.MaxPointsPerDiagram)
        {
            throw new WordDiagramLimitException(
                $"Diagram '{diagramId}' exceeds {_options.MaxPointsPerDiagram} points."
            );
        }
        if (connectionElements.Length > _options.MaxConnectionsPerDiagram)
        {
            throw new WordDiagramLimitException(
                $"Diagram '{diagramId}' exceeds {_options.MaxConnectionsPerDiagram} connections."
            );
        }

        var modelIdCounts = pointElements.Select(item => item.Attribute("modelId")?.Value ?? string.Empty)
            .GroupBy(item => item, StringComparer.Ordinal)
            .ToDictionary(item => item.Key, item => item.Count(), StringComparer.Ordinal);
        var points = new List<WordDiagramPoint>(pointElements.Length);
        foreach (var element in pointElements)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ordinal = source.GetElementOrdinal(element);
            var modelId = element.Attribute("modelId")?.Value ?? string.Empty;
            EnsureIdentifierLimit(modelId);
            var unique = modelId.Length > 0 && modelIdCounts[modelId] == 1;
            if (!unique)
            {
                issues.Add(new WordDiagramIssue(
                    "DGM_POINT_MODEL_ID_INVALID",
                    WordDiagramIssueSeverity.Error,
                    "SmartArt point modelId is missing or duplicated.",
                    diagramId,
                    part.Uri,
                    ordinal,
                    PointId: modelId.Length == 0 ? null : modelId
                ));
            }
            var textCharacters = CountDrawingTextCharacters(element);
            ChargeTextCharacters(textCharacters);
            var propertySets = element.Elements(dgm + "prSet").ToArray();
            var structurallyValid = unique && propertySets.Length <= 1;
            if (propertySets.Length > 1)
            {
                issues.Add(new WordDiagramIssue(
                    "DGM_POINT_PROPERTY_SET_CARDINALITY",
                    WordDiagramIssueSeverity.Error,
                    "SmartArt point contains more than one property set.",
                    diagramId,
                    part.Uri,
                    ordinal,
                    PointId: modelId.Length == 0 ? null : modelId
                ));
            }
            var propertySet = propertySets.FirstOrDefault();
            var placeholderAttribute = propertySet?.Attribute("phldr");
            var placeholder = ParseBoolean(placeholderAttribute?.Value);
            if (placeholderAttribute is not null && placeholder is null)
            {
                structurallyValid = false;
                issues.Add(new WordDiagramIssue(
                    "DGM_POINT_PLACEHOLDER_INVALID",
                    WordDiagramIssueSeverity.Error,
                    "SmartArt point placeholder flag has an invalid lexical value.",
                    diagramId,
                    part.Uri,
                    ordinal,
                    PointId: modelId.Length == 0 ? null : modelId
                ));
            }
            var pointId = StableId(
                "wdpt_",
                diagramId,
                ordinal.ToString(CultureInfo.InvariantCulture),
                modelId
            );
            points.Add(new WordDiagramPoint(
                pointId,
                diagramId,
                modelId,
                BoundedIdentifier(element.Attribute("type")?.Value),
                unique,
                structurallyValid,
                textCharacters > 0,
                textCharacters,
                placeholder,
                PropertyIdentifier(propertySet, "loTypeId"),
                PropertyIdentifier(propertySet, "qsTypeId"),
                PropertyIdentifier(propertySet, "csTypeId"),
                PropertyIdentifier(propertySet, "presAssocID"),
                PropertyIdentifier(propertySet, "presName"),
                PropertyIdentifier(propertySet, "presStyleLbl"),
                part.Uri,
                ordinal
            ));
        }

        var pointIds = points.Where(item => item.IsModelIdUnique)
            .Select(item => item.ModelId)
            .ToHashSet(StringComparer.Ordinal);
        var connectionModelIdCounts = connectionElements
            .Select(item => item.Attribute("modelId")?.Value ?? string.Empty)
            .GroupBy(item => item, StringComparer.Ordinal)
            .ToDictionary(item => item.Key, item => item.Count(), StringComparer.Ordinal);
        var connections = new List<WordDiagramConnection>(connectionElements.Length);
        foreach (var element in connectionElements)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ordinal = source.GetElementOrdinal(element);
            var modelId = element.Attribute("modelId")?.Value ?? string.Empty;
            var sourceId = element.Attribute("srcId")?.Value ?? string.Empty;
            var destinationId = element.Attribute("destId")?.Value ?? string.Empty;
            EnsureIdentifierLimit(modelId);
            EnsureIdentifierLimit(sourceId);
            EnsureIdentifierLimit(destinationId);
            var unique = modelId.Length > 0 && connectionModelIdCounts[modelId] == 1;
            var sourceResolved = pointIds.Contains(sourceId);
            var destinationResolved = pointIds.Contains(destinationId);
            var sourceOrderAttribute = element.Attribute("srcOrd");
            var destinationOrderAttribute = element.Attribute("destOrd");
            var sourceOrder = ParseNonNegativeInteger(sourceOrderAttribute?.Value);
            var destinationOrder = ParseNonNegativeInteger(
                destinationOrderAttribute?.Value
            );
            var ordersValid =
                (sourceOrderAttribute is null || sourceOrder is not null)
                && (destinationOrderAttribute is null || destinationOrder is not null);
            var valid = unique && sourceResolved && destinationResolved && ordersValid;
            var connectionId = StableId(
                "wdcn_",
                diagramId,
                ordinal.ToString(CultureInfo.InvariantCulture),
                modelId
            );
            if (!unique)
            {
                issues.Add(new WordDiagramIssue(
                    "DGM_CONNECTION_MODEL_ID_INVALID",
                    WordDiagramIssueSeverity.Error,
                    "SmartArt connection modelId is missing or duplicated.",
                    diagramId,
                    part.Uri,
                    ordinal,
                    ConnectionId: connectionId
                ));
            }
            if (!sourceResolved || !destinationResolved)
            {
                issues.Add(new WordDiagramIssue(
                    "DGM_CONNECTION_ENDPOINT_UNRESOLVED",
                    WordDiagramIssueSeverity.Error,
                    "SmartArt connection references a missing or ambiguous point endpoint.",
                    diagramId,
                    part.Uri,
                    ordinal,
                    ConnectionId: connectionId
                ));
            }
            if (!ordersValid)
            {
                issues.Add(new WordDiagramIssue(
                    "DGM_CONNECTION_ORDER_INVALID",
                    WordDiagramIssueSeverity.Error,
                    "SmartArt connection order has an invalid non-negative integer value.",
                    diagramId,
                    part.Uri,
                    ordinal,
                    ConnectionId: connectionId
                ));
            }
            connections.Add(new WordDiagramConnection(
                connectionId,
                diagramId,
                modelId,
                sourceId,
                destinationId,
                BoundedIdentifier(element.Attribute("type")?.Value),
                sourceOrder,
                destinationOrder,
                unique,
                sourceResolved,
                destinationResolved,
                valid,
                part.Uri,
                ordinal
            ));
        }
        var persistedDrawingReferences = new List<string>();
        foreach (var extension in root.Descendants().Where(item =>
            item.Name.LocalName == "dataModelExt"
            && item.Name.NamespaceName == OfficeDrawingDiagramNamespace
        ))
        {
            var ordinal = source.GetElementOrdinal(extension);
            var relationshipId = extension.Attribute("relId")?.Value;
            if (string.IsNullOrWhiteSpace(relationshipId))
            {
                issues.Add(new WordDiagramIssue(
                    "DGM_PERSISTED_DRAWING_RELATIONSHIP_MISSING",
                    WordDiagramIssueSeverity.Error,
                    "SmartArt dataModelExt omits its persisted drawing relationship ID.",
                    diagramId,
                    part.Uri,
                    ordinal
                ));
                continue;
            }
            EnsureIdentifierLimit(relationshipId);
            if (persistedDrawingReferences.Contains(relationshipId, StringComparer.Ordinal))
            {
                issues.Add(new WordDiagramIssue(
                    "DGM_PERSISTED_DRAWING_RELATIONSHIP_DUPLICATE",
                    WordDiagramIssueSeverity.Error,
                    "SmartArt dataModelExt repeats a persisted drawing relationship ID.",
                    diagramId,
                    part.Uri,
                    ordinal,
                    relationshipId
                ));
                continue;
            }
            persistedDrawingReferences.Add(relationshipId);
        }
        return new DataPartProjection(
            points.ToArray(),
            connections.ToArray(),
            persistedDrawingReferences.ToArray()
        );
    }

    private DefinitionIdentity ReadDefinitionIdentity(
        OpcPackageSnapshot package,
        IReadOnlyList<WordDiagramPartReference> references,
        WordDiagramPartKind kind,
        string expectedRoot,
        CancellationToken cancellationToken
    )
    {
        var reference = references.SingleOrDefault(item =>
            item.Kind == kind && item.IsResolved
        );
        if (
            reference?.TargetPartUri is not { } partUri
            || !package.Parts.TryGetValue(partUri, out var part)
        )
        {
            return new DefinitionIdentity(null, null);
        }
        var source = ParseXmlPart(part, cancellationToken);
        var root = source.ParsedDocument.Root;
        if (
            root is null
            || root.Name.LocalName != expectedRoot
            || !IsDiagramNamespace(root.Name.NamespaceName)
        )
        {
            throw new WordDiagramProjectionException(
                $"Diagram {kind.ToString().ToLowerInvariant()} part '{partUri}' has an invalid root element."
            );
        }
        return new DefinitionIdentity(
            BoundedIdentifier(root.Attribute("uniqueId")?.Value),
            BoundedIdentifier(root.Attribute("minVer")?.Value)
        );
    }

    private LosslessXmlDocument ParseXmlPart(
        OpcPart part,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var options = new LosslessXmlOptions
            {
                MaxSourceBytes = _options.MaxPartBytes,
                MaxXmlCharacters = _options.MaxPartBytes,
                MaxXmlElements = _options.MaxElementsPerPart,
                MaxXmlDepth = 256,
                MaxTextCharacters = _options.MaxPartBytes,
            };
            return _resourceLease is null
                ? LosslessXmlDocument.Parse(
                    part.Entry.Content,
                    options,
                    cancellationToken
                )
                : LosslessXmlDocument.Parse(
                    part.Entry.Content,
                    options,
                    _resourceLease,
                    WordOperationResourceStage.Diagrams,
                    cancellationToken
                );
        }
        catch (LosslessXmlLimitException exception)
        {
            throw new WordDiagramLimitException(
                "Diagram XML exceeds a bounded XML limit: " + exception.Message
            );
        }
        catch (LosslessXmlException exception)
        {
            throw new WordDiagramProjectionException(
                $"Diagram XML part '{part.Uri}' is unsafe or malformed.",
                exception
            );
        }
    }

    private void ChargeTextCharacters(int characters)
    {
        _totalTextCharacters = checked(_totalTextCharacters + characters);
        if (_totalTextCharacters > _options.MaxTotalTextCharacters)
        {
            throw new WordDiagramLimitException(
                "Diagram point text exceeds the aggregate character limit."
            );
        }
        if (characters > 0)
        {
            WordOperationResourceAccounting.ChargeItems(
                _resourceLease,
                WordOperationResourceStage.Diagrams,
                characters,
                2
            );
        }
    }

    private int CountDrawingTextCharacters(XElement point)
    {
        var result = 0;
        foreach (var text in point.Descendants().Where(item =>
            item.Name.LocalName == "t"
            && item.Name.NamespaceName is DrawingNamespace or StrictDrawingNamespace
        ))
        {
            result = checked(result + text.Value.Length);
        }
        return result;
    }

    private string? PropertyIdentifier(XElement? propertySet, string name) =>
        BoundedIdentifier(propertySet?.Attribute(name)?.Value);

    private string? BoundedIdentifier(string? value)
    {
        if (value is null)
        {
            return null;
        }
        EnsureIdentifierLimit(value);
        return value;
    }

    private void EnsureIdentifierLimit(string value)
    {
        if (value.Length > _options.MaxIdentifierCharacters)
        {
            throw new WordDiagramLimitException(
                "Diagram identifier exceeds the character limit."
            );
        }
    }

    private static void AddCardinalityIssue(
        IssueState issues,
        string diagramId,
        string partUri,
        string code,
        string message,
        int count
    )
    {
        if (count == 1)
        {
            return;
        }
        issues.Add(new WordDiagramIssue(
            code,
            WordDiagramIssueSeverity.Error,
            message + $" Found {count.ToString(CultureInfo.InvariantCulture)}.",
            diagramId,
            partUri
        ));
    }

    private static IReadOnlySet<string> PackageReachableParts(
        OpcPackageSnapshot package,
        CancellationToken cancellationToken
    )
    {
        var reachable = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        queue.Enqueue("/");
        while (queue.TryDequeue(out var source))
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var relationship in package.RelationshipsFrom(source))
            {
                if (
                    relationship.TargetMode == OpcRelationshipTargetMode.Internal
                    && relationship.ResolvedTargetPartUri is not null
                    && package.Parts.ContainsKey(relationship.ResolvedTargetPartUri)
                    && reachable.Add(relationship.ResolvedTargetPartUri)
                )
                {
                    queue.Enqueue(relationship.ResolvedTargetPartUri);
                }
            }
        }
        return reachable;
    }

    private static bool ContainsRelIdsToken(ReadOnlyMemory<byte> content)
    {
        ReadOnlySpan<byte> bytes = content.Span;
        return bytes.IndexOf(RelIdsUtf8) >= 0
            || bytes.IndexOf(RelIdsUtf16LittleEndian) >= 0
            || bytes.IndexOf(RelIdsUtf16BigEndian) >= 0
            || bytes.IndexOf(RelIdsUtf32LittleEndian) >= 0
            || bytes.IndexOf(RelIdsUtf32BigEndian) >= 0;
    }

    private static bool IsXmlContentType(string? contentType) =>
        contentType is not null
        && (
            contentType.EndsWith("+xml", StringComparison.OrdinalIgnoreCase)
            || contentType.Equals("application/xml", StringComparison.OrdinalIgnoreCase)
            || contentType.Equals("text/xml", StringComparison.OrdinalIgnoreCase)
        );

    private static WordDiagramPartKind? PartKind(string? contentType) => contentType switch
    {
        not null when contentType.Equals(DataContentType, StringComparison.OrdinalIgnoreCase) => WordDiagramPartKind.Data,
        not null when contentType.Equals(LayoutContentType, StringComparison.OrdinalIgnoreCase) => WordDiagramPartKind.Layout,
        not null when contentType.Equals(ColorsContentType, StringComparison.OrdinalIgnoreCase) => WordDiagramPartKind.Colors,
        not null when contentType.Equals(QuickStyleContentType, StringComparison.OrdinalIgnoreCase) => WordDiagramPartKind.QuickStyle,
        not null when contentType.Equals(PersistedDrawingContentType, StringComparison.OrdinalIgnoreCase) => WordDiagramPartKind.PersistedDrawing,
        _ => null,
    };

    private static string ContentType(WordDiagramPartKind kind) => kind switch
    {
        WordDiagramPartKind.Data => DataContentType,
        WordDiagramPartKind.Layout => LayoutContentType,
        WordDiagramPartKind.Colors => ColorsContentType,
        WordDiagramPartKind.QuickStyle => QuickStyleContentType,
        WordDiagramPartKind.PersistedDrawing => PersistedDrawingContentType,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static IReadOnlySet<string> RelationshipTypes(WordDiagramPartKind kind) =>
        kind switch
        {
            WordDiagramPartKind.Data => DataRelationshipTypes,
            WordDiagramPartKind.Layout => LayoutRelationshipTypes,
            WordDiagramPartKind.Colors => ColorsRelationshipTypes,
            WordDiagramPartKind.QuickStyle => QuickStyleRelationshipTypes,
            WordDiagramPartKind.PersistedDrawing => PersistedDrawingRelationshipTypes,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    private static string? RelationshipAttribute(XElement element, string localName) =>
        element.Attributes().FirstOrDefault(attribute =>
            attribute.Name.LocalName == localName
            && attribute.Name.NamespaceName is RelationshipNamespace
                or StrictRelationshipNamespace
        )?.Value;

    private static bool IsDiagramNamespace(string value) =>
        value is DiagramNamespace or StrictDiagramNamespace;

    private static bool? ParseBoolean(string? value) => value switch
    {
        null => null,
        "1" or "true" or "on" => true,
        "0" or "false" or "off" => false,
        _ => null,
    };

    private static long? ParseNonNegativeInteger(string? value) =>
        uint.TryParse(
            value,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var parsed
        )
            ? parsed
            : null;

    private static string StableId(string prefix, params string[] components)
    {
        var payload = Encoding.UTF8.GetBytes(string.Join('\u001f', components));
        var hash = SHA256.HashData(payload);
        return prefix + Convert.ToHexString(hash.AsSpan(0, 12)).ToLowerInvariant();
    }

    private sealed record ReferenceElement(
        string SourcePartUri,
        int SourceElementOrdinal,
        string? DataRelationshipId,
        string? LayoutRelationshipId,
        string? QuickStyleRelationshipId,
        string? ColorsRelationshipId
    );

    private sealed record DefinitionIdentity(
        string? UniqueId,
        string? MinimumVersion
    );

    private sealed record DataPartProjection(
        WordDiagramPoint[] Points,
        WordDiagramConnection[] Connections,
        string[] PersistedDrawingRelationshipIds
    );

    private sealed class IssueState
    {
        private readonly int _maximum;
        private readonly List<WordDiagramIssue> _issues = new();

        public IssueState(int maximum) => _maximum = maximum;

        public IReadOnlyList<WordDiagramIssue> Issues => _issues;

        public bool Truncated { get; private set; }

        public void Add(WordDiagramIssue issue)
        {
            if (_issues.Count < _maximum)
            {
                _issues.Add(issue);
            }
            else
            {
                Truncated = true;
            }
        }
    }
}

public sealed class WordDiagramLimitException : IOException
{
    public WordDiagramLimitException(string message)
        : base(message) { }
}

public sealed class WordDiagramProjectionException : IOException
{
    public WordDiagramProjectionException(string message)
        : base(message) { }

    public WordDiagramProjectionException(string message, Exception innerException)
        : base(message, innerException) { }
}
