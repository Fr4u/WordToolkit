using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Xml;

namespace WordToolkit.Engine.Semantics;

public enum WordChartIssueSeverity
{
    Info,
    Warning,
    Error,
}

public enum WordChartDataSourceKind
{
    None,
    StringReference,
    NumberReference,
    MultiLevelStringReference,
    StringLiteral,
    NumberLiteral,
    RichText,
    Unknown,
}

public enum WordChartRelatedPartKind
{
    EmbeddedPackage,
    Image,
    Style,
    ColorStyle,
    ChartDrawing,
    ThemeOverride,
    Hyperlink,
    Other,
}

public sealed record WordChartIssue(
    string Code,
    WordChartIssueSeverity Severity,
    string Message,
    string? ChartPartUri = null,
    int? SourceElementOrdinal = null,
    string? RelationshipId = null,
    string? SeriesId = null
);

public sealed record WordChartReference(
    string SourcePartUri,
    string RelationshipId,
    string RelationshipType,
    string Target,
    OpcRelationshipTargetMode TargetMode,
    string? TargetPartUri,
    bool IsResolved
);

public sealed record WordChartRelatedPart(
    string RelationshipId,
    string RelationshipType,
    WordChartRelatedPartKind Kind,
    OpcRelationshipTargetMode TargetMode,
    string Target,
    string? TargetPartUri,
    string? TargetContentType,
    bool IsResolved
);

public sealed class WordChartDataSourceDefinition
{
    internal WordChartDataSourceDefinition(
        string role,
        WordChartDataSourceKind kind,
        string? formula,
        string? formatCode,
        long? declaredPointCount,
        int actualPointCount,
        int distinctPointIndexCount,
        long? maximumPointIndex,
        int sourceElementOrdinal,
        bool cachePresent,
        int cacheLevelCount,
        bool hasDuplicatePointIndexes,
        bool declaredCountMatches,
        IReadOnlyList<string> unmodeledElements
    )
    {
        Role = role;
        Kind = kind;
        Formula = formula;
        FormatCode = formatCode;
        DeclaredPointCount = declaredPointCount;
        ActualPointCount = actualPointCount;
        DistinctPointIndexCount = distinctPointIndexCount;
        MaximumPointIndex = maximumPointIndex;
        SourceElementOrdinal = sourceElementOrdinal;
        CachePresent = cachePresent;
        CacheLevelCount = cacheLevelCount;
        HasDuplicatePointIndexes = hasDuplicatePointIndexes;
        DeclaredCountMatches = declaredCountMatches;
        UnmodeledElements = new ReadOnlyCollection<string>(
            unmodeledElements.Distinct(StringComparer.Ordinal).Order().ToArray()
        );
    }

    public string Role { get; }

    public WordChartDataSourceKind Kind { get; }

    public string? Formula { get; }

    public string? FormatCode { get; }

    public long? DeclaredPointCount { get; }

    public int ActualPointCount { get; }

    public int DistinctPointIndexCount { get; }

    public long? MaximumPointIndex { get; }

    public int SourceElementOrdinal { get; }

    public bool CachePresent { get; }

    public int CacheLevelCount { get; }

    public IReadOnlyList<string> UnmodeledElements { get; }

    public bool HasDuplicatePointIndexes { get; }

    public bool DeclaredCountMatches { get; }
}

public sealed class WordChartSeriesDefinition
{
    internal WordChartSeriesDefinition(
        string id,
        string chartType,
        long? index,
        long? order,
        int sourceElementOrdinal,
        IReadOnlyList<WordChartDataSourceDefinition> dataSources,
        IReadOnlyList<string> unmodeledElements
    )
    {
        Id = id;
        ChartType = chartType;
        Index = index;
        Order = order;
        SourceElementOrdinal = sourceElementOrdinal;
        DataSources = new ReadOnlyCollection<WordChartDataSourceDefinition>(
            dataSources.ToArray()
        );
        UnmodeledElements = new ReadOnlyCollection<string>(
            unmodeledElements.Distinct(StringComparer.Ordinal).Order().ToArray()
        );
    }

    public string Id { get; }

    public string ChartType { get; }

    public long? Index { get; }

    public long? Order { get; }

    public int SourceElementOrdinal { get; }

    public IReadOnlyList<WordChartDataSourceDefinition> DataSources { get; }

    public IReadOnlyList<string> UnmodeledElements { get; }
}

public sealed record WordChartPlotDefinition(
    string Type,
    string? Grouping,
    string? BarDirection,
    bool? VaryColors,
    int SourceElementOrdinal,
    IReadOnlyList<string> SeriesIds,
    IReadOnlyList<long> AxisIds,
    IReadOnlyList<string> UnmodeledElements
);

public sealed record WordChartAxisDefinition(
    long AxisId,
    string Kind,
    string? Position,
    long? CrossAxisId,
    bool? Deleted,
    int SourceElementOrdinal,
    IReadOnlyList<string> UnmodeledElements
);

public sealed record WordChartExternalDataDefinition(
    string? RelationshipId,
    bool? AutoUpdate,
    OpcRelationshipTargetMode? TargetMode,
    string? TargetPartUri,
    string? TargetContentType,
    bool IsResolved,
    int SourceElementOrdinal
);

public sealed class WordChartDefinition
{
    internal WordChartDefinition(
        string id,
        string partUri,
        string contentType,
        bool isPackageReachable,
        int incomingReferenceCount,
        string namespaceUri,
        int sourceElementOrdinal,
        bool hasTitle,
        string? titleText,
        bool titleTextTruncated,
        bool? autoTitleDeleted,
        bool? plotVisibleOnly,
        string? displayBlanksAs,
        IReadOnlyList<WordChartPlotDefinition> plots,
        IReadOnlyList<WordChartSeriesDefinition> series,
        IReadOnlyList<WordChartAxisDefinition> axes,
        IReadOnlyList<WordChartExternalDataDefinition> externalData,
        IReadOnlyList<WordChartRelatedPart> relatedParts,
        IReadOnlyList<string> unmodeledRootElements,
        IReadOnlyList<string> unmodeledPlotAreaElements
    )
    {
        Id = id;
        PartUri = partUri;
        ContentType = contentType;
        IsPackageReachable = isPackageReachable;
        IncomingReferenceCount = incomingReferenceCount;
        NamespaceUri = namespaceUri;
        SourceElementOrdinal = sourceElementOrdinal;
        HasTitle = hasTitle;
        TitleText = titleText;
        TitleTextTruncated = titleTextTruncated;
        AutoTitleDeleted = autoTitleDeleted;
        PlotVisibleOnly = plotVisibleOnly;
        DisplayBlanksAs = displayBlanksAs;
        Plots = new ReadOnlyCollection<WordChartPlotDefinition>(plots.ToArray());
        Series = new ReadOnlyCollection<WordChartSeriesDefinition>(series.ToArray());
        Axes = new ReadOnlyCollection<WordChartAxisDefinition>(axes.ToArray());
        ExternalData = new ReadOnlyCollection<WordChartExternalDataDefinition>(
            externalData.ToArray()
        );
        RelatedParts = new ReadOnlyCollection<WordChartRelatedPart>(
            relatedParts.ToArray()
        );
        UnmodeledRootElements = new ReadOnlyCollection<string>(
            unmodeledRootElements.Distinct(StringComparer.Ordinal).Order().ToArray()
        );
        UnmodeledPlotAreaElements = new ReadOnlyCollection<string>(
            unmodeledPlotAreaElements.Distinct(StringComparer.Ordinal).Order().ToArray()
        );
    }

    public string Id { get; }

    public string PartUri { get; }

    public string ContentType { get; }

    public bool IsPackageReachable { get; }

    public int IncomingReferenceCount { get; }

    public string NamespaceUri { get; }

    public int SourceElementOrdinal { get; }

    public bool HasTitle { get; }

    public string? TitleText { get; }

    public bool TitleTextTruncated { get; }

    public bool? AutoTitleDeleted { get; }

    public bool? PlotVisibleOnly { get; }

    public string? DisplayBlanksAs { get; }

    public IReadOnlyList<WordChartPlotDefinition> Plots { get; }

    public IReadOnlyList<WordChartSeriesDefinition> Series { get; }

    public IReadOnlyList<WordChartAxisDefinition> Axes { get; }

    public IReadOnlyList<WordChartExternalDataDefinition> ExternalData { get; }

    public IReadOnlyList<WordChartRelatedPart> RelatedParts { get; }

    public IReadOnlyList<string> UnmodeledRootElements { get; }

    public IReadOnlyList<string> UnmodeledPlotAreaElements { get; }
}

public sealed class WordChartGraph
{
    private readonly IReadOnlyDictionary<string, WordChartDefinition> _chartsById;

    internal WordChartGraph(
        string packageFingerprint,
        IReadOnlyList<WordChartReference> references,
        IReadOnlyList<WordChartDefinition> charts,
        IReadOnlyList<WordChartIssue> issues,
        bool issuesTruncated,
        IReadOnlyList<string> unsupportedExtendedChartPartUris
    )
    {
        PackageFingerprint = packageFingerprint;
        References = new ReadOnlyCollection<WordChartReference>(references.ToArray());
        Charts = new ReadOnlyCollection<WordChartDefinition>(charts.ToArray());
        Issues = new ReadOnlyCollection<WordChartIssue>(issues.ToArray());
        IssuesTruncated = issuesTruncated;
        UnsupportedExtendedChartPartUris = new ReadOnlyCollection<string>(
            unsupportedExtendedChartPartUris.Distinct(StringComparer.Ordinal).Order().ToArray()
        );
        _chartsById = new ReadOnlyDictionary<string, WordChartDefinition>(
            charts.ToDictionary(chart => chart.Id, StringComparer.Ordinal)
        );
    }

    public string PackageFingerprint { get; }

    public IReadOnlyList<WordChartReference> References { get; }

    public IReadOnlyList<WordChartDefinition> Charts { get; }

    public IReadOnlyList<WordChartIssue> Issues { get; }

    public bool IssuesTruncated { get; }

    public IReadOnlyList<string> UnsupportedExtendedChartPartUris { get; }

    public bool TryGetChart(string id, out WordChartDefinition? chart)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return _chartsById.TryGetValue(id, out chart);
    }
}

public sealed record WordChartGraphOptions
{
    public static WordChartGraphOptions Default { get; } = new();

    public int MaxChartParts { get; init; } = 1_024;

    public int MaxChartPartBytes { get; init; } = 64 * 1024 * 1024;

    public int MaxElementsPerChart { get; init; } = 500_000;

    public int MaxSeriesPerChart { get; init; } = 16_384;

    public int MaxDataSourcesPerSeries { get; init; } = 16;

    public int MaxCachedPointsPerDataSource { get; init; } = 1_000_000;

    public int MaxFormulaCharacters { get; init; } = 32_768;

    public int MaxTitleCharacters { get; init; } = 8_192;

    public int MaxIssues { get; init; } = 10_000;

    internal void Validate()
    {
        if (MaxChartParts <= 0) throw new ArgumentOutOfRangeException(nameof(MaxChartParts));
        if (MaxChartPartBytes <= 0) throw new ArgumentOutOfRangeException(nameof(MaxChartPartBytes));
        if (MaxElementsPerChart <= 0) throw new ArgumentOutOfRangeException(nameof(MaxElementsPerChart));
        if (MaxSeriesPerChart <= 0) throw new ArgumentOutOfRangeException(nameof(MaxSeriesPerChart));
        if (MaxDataSourcesPerSeries <= 0) throw new ArgumentOutOfRangeException(nameof(MaxDataSourcesPerSeries));
        if (MaxCachedPointsPerDataSource <= 0) throw new ArgumentOutOfRangeException(nameof(MaxCachedPointsPerDataSource));
        if (MaxFormulaCharacters <= 0) throw new ArgumentOutOfRangeException(nameof(MaxFormulaCharacters));
        if (MaxTitleCharacters <= 0) throw new ArgumentOutOfRangeException(nameof(MaxTitleCharacters));
        if (MaxIssues <= 0) throw new ArgumentOutOfRangeException(nameof(MaxIssues));
    }
}

public sealed class WordChartGraphBuilder
{
    private const string ChartTransitionalNamespace =
        "http://schemas.openxmlformats.org/drawingml/2006/chart";
    private const string ChartStrictNamespace =
        "http://purl.oclc.org/ooxml/drawingml/chart";
    private const string DrawingTransitionalNamespace =
        "http://schemas.openxmlformats.org/drawingml/2006/main";
    private const string DrawingStrictNamespace =
        "http://purl.oclc.org/ooxml/drawingml/main";
    private const string RelationshipTransitionalNamespace =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string RelationshipStrictNamespace =
        "http://purl.oclc.org/ooxml/officeDocument/relationships";
    private const string ChartRelationship =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart";
    private const string StrictChartRelationship =
        "http://purl.oclc.org/ooxml/officeDocument/relationships/chart";
    private const string ChartContentType =
        "application/vnd.openxmlformats-officedocument.drawingml.chart+xml";
    private const string ExtendedChartContentType =
        "application/vnd.ms-office.chartex+xml";

    private static readonly HashSet<string> PlotNames = new(StringComparer.Ordinal)
    {
        "area3DChart", "areaChart", "bar3DChart", "barChart", "bubbleChart",
        "doughnutChart", "line3DChart", "lineChart", "ofPieChart", "pie3DChart",
        "pieChart", "radarChart", "scatterChart", "stockChart", "surface3DChart",
        "surfaceChart",
    };

    private static readonly HashSet<string> AxisNames = new(StringComparer.Ordinal)
    {
        "catAx", "dateAx", "serAx", "valAx",
    };

    private static readonly HashSet<string> DataSourceRoles = new(StringComparer.Ordinal)
    {
        "tx", "cat", "val", "xVal", "yVal", "bubbleSize",
    };

    private static readonly HashSet<string> KnownRootChildren = new(StringComparer.Ordinal)
    {
        "date1904", "lang", "roundedCorners", "style", "clrMapOvr", "pivotSource",
        "protection", "chart", "spPr", "txPr", "externalData", "printSettings",
        "userShapes", "extLst", "AlternateContent",
    };

    private readonly WordChartGraphOptions _options;

    public WordChartGraphBuilder(WordChartGraphOptions? options = null)
    {
        _options = options ?? WordChartGraphOptions.Default;
        _options.Validate();
    }

    public WordChartGraph Build(
        OpcPackageSnapshot package,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(package);
        cancellationToken.ThrowIfCancellationRequested();
        var issueState = new IssueState(_options.MaxIssues);
        var references = BuildReferences(package, issueState, cancellationToken);
        var chartParts = package.Parts.Values
            .Where(part => string.Equals(
                part.ContentType,
                ChartContentType,
                StringComparison.OrdinalIgnoreCase
            ))
            .OrderBy(part => part.Uri, StringComparer.Ordinal)
            .ToArray();
        if (chartParts.Length > _options.MaxChartParts)
        {
            throw new WordChartLimitException(
                $"Package contains {chartParts.Length} chart parts; limit is {_options.MaxChartParts}."
            );
        }

        var reachable = PackageReachableParts(package, cancellationToken);
        var charts = new List<WordChartDefinition>(chartParts.Length);
        foreach (var part in chartParts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var incoming = references.Count(reference =>
                string.Equals(reference.TargetPartUri, part.Uri, StringComparison.Ordinal)
            );
            if (incoming == 0)
            {
                issueState.Add(new WordChartIssue(
                    "CHART_PART_UNREFERENCED",
                    WordChartIssueSeverity.Warning,
                    "Chart part has no resolved chart relationship.",
                    part.Uri
                ));
            }
            try
            {
                charts.Add(ParseChart(
                    package,
                    part,
                    reachable.Contains(part.Uri),
                    incoming,
                    issueState,
                    cancellationToken
                ));
            }
            catch (InvalidOperationException exception)
            {
                throw new WordChartProjectionException(
                    $"Chart part '{part.Uri}' contains a missing, duplicate, or structurally ambiguous singleton element.",
                    exception
                );
            }
        }

        var extended = package.Parts.Values
            .Where(part => string.Equals(
                part.ContentType,
                ExtendedChartContentType,
                StringComparison.OrdinalIgnoreCase
            ))
            .Select(part => part.Uri)
            .Order(StringComparer.Ordinal)
            .ToArray();
        foreach (var partUri in extended)
        {
            issueState.Add(new WordChartIssue(
                "CHART_EXTENDED_UNMODELED",
                WordChartIssueSeverity.Info,
                "Office 2016 extended chart part is preserved but not projected into the classic chart model.",
                partUri
            ));
        }

        return new WordChartGraph(
            package.Fingerprint,
            references,
            charts,
            issueState.Issues,
            issueState.Truncated,
            extended
        );
    }

    private IReadOnlyList<WordChartReference> BuildReferences(
        OpcPackageSnapshot package,
        IssueState issues,
        CancellationToken cancellationToken
    )
    {
        var result = new List<WordChartReference>();
        foreach (
            var relationship in package.Relationships
                .Where(item => IsChartRelationship(item.Type))
                .OrderBy(item => item.SourcePartUri, StringComparer.Ordinal)
                .ThenBy(item => item.Id, StringComparer.Ordinal)
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            var resolved = relationship.TargetMode == OpcRelationshipTargetMode.Internal
                && relationship.ResolvedTargetPartUri is not null
                && package.Parts.TryGetValue(
                    relationship.ResolvedTargetPartUri,
                    out var target
                )
                && string.Equals(
                    target.ContentType,
                    ChartContentType,
                    StringComparison.OrdinalIgnoreCase
                );
            if (!resolved)
            {
                issues.Add(new WordChartIssue(
                    "CHART_RELATIONSHIP_UNRESOLVED",
                    WordChartIssueSeverity.Error,
                    "Chart relationship does not resolve internally to a classic chart part.",
                    relationship.ResolvedTargetPartUri,
                    RelationshipId: relationship.Id
                ));
            }
            result.Add(new WordChartReference(
                relationship.SourcePartUri,
                relationship.Id,
                relationship.Type,
                relationship.Target,
                relationship.TargetMode,
                relationship.ResolvedTargetPartUri,
                resolved
            ));
        }
        return result;
    }

    private WordChartDefinition ParseChart(
        OpcPackageSnapshot package,
        OpcPart part,
        bool isReachable,
        int incomingReferenceCount,
        IssueState issues,
        CancellationToken cancellationToken
    )
    {
        var source = ParseChartPart(part, cancellationToken);
        var root = source.ParsedDocument.Root;
        if (
            root is null
            || root.Name.LocalName != "chartSpace"
            || !IsChartNamespace(root.Name.NamespaceName)
        )
        {
            throw new WordChartProjectionException(
                $"Chart part '{part.Uri}' does not have a c:chartSpace root element."
            );
        }
        var c = root.Name.Namespace;
        var chart = root.Elements(c + "chart").SingleOrDefault();
        if (chart is null)
        {
            issues.Add(new WordChartIssue(
                "CHART_BODY_MISSING",
                WordChartIssueSeverity.Error,
                "Chart space has no classic c:chart element.",
                part.Uri,
                source.GetElementOrdinal(root)
            ));
        }

        var plots = new List<WordChartPlotDefinition>();
        var allSeries = new List<WordChartSeriesDefinition>();
        var axes = new List<WordChartAxisDefinition>();
        var unmodeledPlot = new List<string>();
        var plotArea = chart?.Elements(c + "plotArea").SingleOrDefault();
        if (chart is not null && plotArea is null)
        {
            issues.Add(new WordChartIssue(
                "CHART_PLOT_AREA_MISSING",
                WordChartIssueSeverity.Error,
                "Classic chart has no c:plotArea element.",
                part.Uri,
                source.GetElementOrdinal(chart)
            ));
        }
        if (plotArea is not null)
        {
            foreach (var child in plotArea.Elements())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (child.Name.Namespace == c && PlotNames.Contains(child.Name.LocalName))
                {
                    var series = ParseSeries(
                        part.Uri,
                        child,
                        c,
                        source,
                        issues,
                        cancellationToken
                    );
                    if (allSeries.Count + series.Count > _options.MaxSeriesPerChart)
                    {
                        throw new WordChartLimitException(
                            $"Chart '{part.Uri}' exceeds {_options.MaxSeriesPerChart} series."
                        );
                    }
                    allSeries.AddRange(series);
                    plots.Add(new WordChartPlotDefinition(
                        child.Name.LocalName,
                        ChildValue(child, c + "grouping"),
                        ChildValue(child, c + "barDir"),
                        ParseBoolean(ChildValue(child, c + "varyColors")),
                        source.GetElementOrdinal(child),
                        series.Select(item => item.Id).ToArray(),
                        child.Elements(c + "axId")
                            .Select(element => ParseLongValue(element, "plot axis ID", part.Uri))
                            .ToArray(),
                        FindUnknownChildren(
                            child,
                            new HashSet<string>(StringComparer.Ordinal)
                            {
                                "axId", "barDir", "dLbls", "dropLines", "extLst", "firstSliceAng",
                                "gapDepth", "gapWidth", "grouping", "hiLowLines", "marker", "overlap",
                                "radarStyle", "scatterStyle", "ser", "serLines", "smooth", "upDownBars",
                                "varyColors", "wireframe",
                            }
                        )
                    ));
                }
                else if (child.Name.Namespace == c && AxisNames.Contains(child.Name.LocalName))
                {
                    axes.Add(ParseAxis(part.Uri, child, c, source));
                }
                else if (child.Name != c + "layout" && child.Name != c + "dTable")
                {
                    unmodeledPlot.Add(QualifiedName(child.Name));
                }
            }
        }

        ValidateSeries(part.Uri, allSeries, issues);
        ValidateAxes(part.Uri, plots, axes, issues);
        var title = chart?.Elements(c + "title").SingleOrDefault();
        var titleText = title is null ? null : ExtractChartTitleText(title, c);
        var titleTruncated = titleText?.Length > _options.MaxTitleCharacters;
        if (titleTruncated)
        {
            titleText = titleText![.._options.MaxTitleCharacters];
        }
        var external = root.Elements(c + "externalData")
            .Select(element => ParseExternalData(package, part.Uri, element, c, source, issues))
            .ToArray();
        if (external.Length > 1)
        {
            issues.Add(new WordChartIssue(
                "CHART_EXTERNAL_DATA_MULTIPLE",
                WordChartIssueSeverity.Error,
                "Chart space contains multiple c:externalData elements.",
                part.Uri
            ));
        }

        return new WordChartDefinition(
            StableId("wdch_", package.Fingerprint, part.Uri),
            part.Uri,
            part.ContentType ?? ChartContentType,
            isReachable,
            incomingReferenceCount,
            root.Name.NamespaceName,
            source.GetElementOrdinal(root),
            title is not null,
            titleText,
            titleTruncated,
            ParseBoolean(chart is null ? null : ChildValue(chart, c + "autoTitleDeleted")),
            ParseBoolean(chart is null ? null : ChildValue(chart, c + "plotVisOnly")),
            chart is null ? null : ChildValue(chart, c + "dispBlanksAs"),
            plots,
            allSeries,
            axes,
            external,
            BuildRelatedParts(package, part.Uri),
            FindUnknownChildren(root, KnownRootChildren),
            unmodeledPlot
        );
    }

    private IReadOnlyList<WordChartSeriesDefinition> ParseSeries(
        string partUri,
        XElement plot,
        XNamespace c,
        LosslessXmlDocument source,
        IssueState issues,
        CancellationToken cancellationToken
    )
    {
        var result = new List<WordChartSeriesDefinition>();
        foreach (var element in plot.Elements(c + "ser"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (result.Count >= _options.MaxSeriesPerChart)
            {
                throw new WordChartLimitException(
                    $"Chart plot in '{partUri}' exceeds {_options.MaxSeriesPerChart} series."
                );
            }
            var ordinal = source.GetElementOrdinal(element);
            var id = StableId("wdcs_", partUri, ordinal.ToString(CultureInfo.InvariantCulture));
            var dataSources = new List<WordChartDataSourceDefinition>();
            foreach (var child in element.Elements().Where(child =>
                child.Name.Namespace == c
                && DataSourceRoles.Contains(child.Name.LocalName)
            ))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (dataSources.Count >= _options.MaxDataSourcesPerSeries)
                {
                    throw new WordChartLimitException(
                        $"Chart series '{id}' exceeds {_options.MaxDataSourcesPerSeries} data sources."
                    );
                }
                dataSources.Add(ParseDataSource(
                    partUri,
                    id,
                    child,
                    c,
                    source,
                    issues,
                    cancellationToken
                ));
            }
            result.Add(new WordChartSeriesDefinition(
                id,
                plot.Name.LocalName,
                TryChildUnsignedLong(element, c + "idx"),
                TryChildUnsignedLong(element, c + "order"),
                ordinal,
                dataSources,
                FindUnknownChildren(
                    element,
                    new HashSet<string>(DataSourceRoles, StringComparer.Ordinal)
                    {
                        "idx", "order", "spPr", "invertIfNegative", "pictureOptions", "dPt",
                        "dLbls", "trendline", "errBars", "marker", "smooth", "shape", "extLst",
                    }
                )
            ));
        }
        return result;
    }

    private WordChartDataSourceDefinition ParseDataSource(
        string partUri,
        string seriesId,
        XElement roleElement,
        XNamespace c,
        LosslessXmlDocument source,
        IssueState issues,
        CancellationToken cancellationToken
    )
    {
        var sourceElement = roleElement.Elements().FirstOrDefault();
        var kind = sourceElement is null
            ? WordChartDataSourceKind.None
            : sourceElement.Name.LocalName switch
            {
                "strRef" => WordChartDataSourceKind.StringReference,
                "numRef" => WordChartDataSourceKind.NumberReference,
                "multiLvlStrRef" => WordChartDataSourceKind.MultiLevelStringReference,
                "strLit" => WordChartDataSourceKind.StringLiteral,
                "numLit" => WordChartDataSourceKind.NumberLiteral,
                "rich" => WordChartDataSourceKind.RichText,
                "v" when roleElement.Name.LocalName == "tx" => WordChartDataSourceKind.StringLiteral,
                _ => WordChartDataSourceKind.Unknown,
            };
        var formula = sourceElement?.Descendants(c + "f").SingleOrDefault()?.Value;
        if (formula?.Length > _options.MaxFormulaCharacters)
        {
            throw new WordChartLimitException(
                $"Chart data formula in '{partUri}' exceeds {_options.MaxFormulaCharacters} characters."
            );
        }
        var cache = sourceElement is not null
            && (sourceElement.Name == c + "strLit" || sourceElement.Name == c + "numLit")
                ? sourceElement
                : sourceElement?.Elements().Where(child =>
                    child.Name == c + "strCache"
                    || child.Name == c + "numCache"
                    || child.Name == c + "multiLvlStrCache"
                ).SingleOrDefault();
        var levels = cache?.Name == c + "multiLvlStrCache"
            ? cache.Elements(c + "lvl").ToArray()
            : cache is null
                ? Array.Empty<XElement>()
                : [cache];
        var pointCount = 0;
        var allIndexes = new HashSet<long>();
        var duplicateIndexes = false;
        foreach (var level in levels)
        {
            var levelIndexes = new HashSet<long>();
            foreach (var point in level.Elements(c + "pt"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (pointCount >= _options.MaxCachedPointsPerDataSource)
                {
                    throw new WordChartLimitException(
                        $"Chart data cache in '{partUri}' exceeds {_options.MaxCachedPointsPerDataSource} point entries."
                    );
                }
                pointCount++;
                if (TryParseUnsignedLong(point.Attribute("idx")?.Value) is { } index)
                {
                    duplicateIndexes |= !levelIndexes.Add(index);
                    allIndexes.Add(index);
                }
            }
        }
        var declared = TryParseUnsignedLong(
            cache?.Elements(c + "ptCount").SingleOrDefault()?.Attribute("val")?.Value
        );
        var declaredCountMatches = declared is null || declared == allIndexes.Count;
        if (!declaredCountMatches)
        {
            issues.Add(new WordChartIssue(
                "CHART_CACHE_COUNT_MISMATCH",
                WordChartIssueSeverity.Warning,
                $"Data source '{roleElement.Name.LocalName}' declares {declared} logical points but contains {allIndexes.Count} distinct point indexes across {levels.Length} cache level(s).",
                partUri,
                source.GetElementOrdinal(roleElement),
                SeriesId: seriesId
            ));
        }
        if (duplicateIndexes)
        {
            issues.Add(new WordChartIssue(
                "CHART_CACHE_DUPLICATE_INDEX",
                WordChartIssueSeverity.Warning,
                $"Data source '{roleElement.Name.LocalName}' contains duplicate point indexes.",
                partUri,
                source.GetElementOrdinal(roleElement),
                SeriesId: seriesId
            ));
        }
        return new WordChartDataSourceDefinition(
            roleElement.Name.LocalName,
            kind,
            formula,
            cache?.Elements(c + "formatCode").SingleOrDefault()?.Value,
            declared,
            pointCount,
            allIndexes.Count,
            allIndexes.Count == 0 ? null : allIndexes.Max(),
            source.GetElementOrdinal(roleElement),
            cache is not null,
            levels.Length,
            duplicateIndexes,
            declaredCountMatches,
            FindUnknownDataSourceElements(roleElement, sourceElement, c)
        );
    }

    private static WordChartAxisDefinition ParseAxis(
        string partUri,
        XElement element,
        XNamespace c,
        LosslessXmlDocument source
    ) => new(
        ParseLongValue(
            element.Elements(c + "axId").Single(),
            "axis ID",
            partUri
        ),
        element.Name.LocalName,
        ChildValue(element, c + "axPos"),
        TryChildUnsignedLong(element, c + "crossAx"),
        ParseBoolean(ChildValue(element, c + "delete")),
        source.GetElementOrdinal(element),
        FindUnknownChildren(
            element,
            new HashSet<string>(StringComparer.Ordinal)
            {
                "axId", "scaling", "delete", "axPos", "majorGridlines", "minorGridlines",
                "title", "numFmt", "majorTickMark", "minorTickMark", "tickLblPos", "spPr",
                "txPr", "crossAx", "crosses", "crossesAt", "auto", "lblAlgn", "lblOffset",
                "tickLblSkip", "tickMarkSkip", "noMultiLvlLbl", "crossBetween", "majorUnit",
                "minorUnit", "dispUnits", "extLst",
            }
        )
    );

    private WordChartExternalDataDefinition ParseExternalData(
        OpcPackageSnapshot package,
        string chartPartUri,
        XElement element,
        XNamespace c,
        LosslessXmlDocument source,
        IssueState issues
    )
    {
        var relationshipId = RelationshipAttribute(element, "id");
        var relationship = relationshipId is null
            ? null
            : package.RelationshipsFrom(chartPartUri).SingleOrDefault(item =>
                string.Equals(item.Id, relationshipId, StringComparison.Ordinal)
            );
        var resolved = relationship?.TargetMode == OpcRelationshipTargetMode.Internal
            && relationship.ResolvedTargetPartUri is not null
            && package.Parts.ContainsKey(relationship.ResolvedTargetPartUri);
        if (!resolved)
        {
            issues.Add(new WordChartIssue(
                "CHART_EXTERNAL_DATA_UNRESOLVED",
                WordChartIssueSeverity.Warning,
                "c:externalData does not resolve internally to an existing package part.",
                chartPartUri,
                source.GetElementOrdinal(element),
                relationshipId
            ));
        }
        string? contentType = null;
        if (
            relationship?.ResolvedTargetPartUri is not null
            && package.Parts.TryGetValue(relationship.ResolvedTargetPartUri, out var target)
        )
        {
            contentType = target.ContentType;
        }
        return new WordChartExternalDataDefinition(
            relationshipId,
            ParseBoolean(ChildValue(element, c + "autoUpdate")),
            relationship?.TargetMode,
            relationship?.ResolvedTargetPartUri,
            contentType,
            resolved,
            source.GetElementOrdinal(element)
        );
    }

    private static IReadOnlyList<WordChartRelatedPart> BuildRelatedParts(
        OpcPackageSnapshot package,
        string chartPartUri
    ) => package.RelationshipsFrom(chartPartUri)
        .OrderBy(relationship => relationship.Id, StringComparer.Ordinal)
        .Select(relationship =>
        {
            package.Parts.TryGetValue(
                relationship.ResolvedTargetPartUri ?? string.Empty,
                out var target
            );
            return new WordChartRelatedPart(
                relationship.Id,
                relationship.Type,
                ClassifyRelatedPart(relationship.Type),
                relationship.TargetMode,
                relationship.Target,
                relationship.ResolvedTargetPartUri,
                target?.ContentType,
                relationship.TargetMode == OpcRelationshipTargetMode.External
                    || target is not null
            );
        }).ToArray();

    private static void ValidateSeries(
        string partUri,
        IReadOnlyList<WordChartSeriesDefinition> series,
        IssueState issues
    )
    {
        foreach (
            var duplicate in series.Where(item => item.Index is not null)
                .GroupBy(item => (item.ChartType, item.Index))
                .Where(group => group.Count() > 1)
        )
        {
            issues.Add(new WordChartIssue(
                "CHART_SERIES_INDEX_DUPLICATE",
                WordChartIssueSeverity.Warning,
                $"Plot '{duplicate.Key.ChartType}' contains duplicate series index {duplicate.Key.Index}.",
                partUri
            ));
        }
        foreach (
            var duplicate in series.Where(item => item.Order is not null)
                .GroupBy(item => (item.ChartType, item.Order))
                .Where(group => group.Count() > 1)
        )
        {
            issues.Add(new WordChartIssue(
                "CHART_SERIES_ORDER_DUPLICATE",
                WordChartIssueSeverity.Warning,
                $"Plot '{duplicate.Key.ChartType}' contains duplicate series order {duplicate.Key.Order}.",
                partUri
            ));
        }
    }

    private static void ValidateAxes(
        string partUri,
        IReadOnlyList<WordChartPlotDefinition> plots,
        IReadOnlyList<WordChartAxisDefinition> axes,
        IssueState issues
    )
    {
        var ids = axes.Select(axis => axis.AxisId).ToHashSet();
        foreach (var duplicate in axes.GroupBy(axis => axis.AxisId).Where(group => group.Count() > 1))
        {
            issues.Add(new WordChartIssue(
                "CHART_AXIS_ID_DUPLICATE",
                WordChartIssueSeverity.Error,
                $"Chart defines axis ID {duplicate.Key} more than once.",
                partUri
            ));
        }
        foreach (var plot in plots)
        {
            foreach (var id in plot.AxisIds.Where(id => !ids.Contains(id)))
            {
                issues.Add(new WordChartIssue(
                    "CHART_AXIS_REFERENCE_UNRESOLVED",
                    WordChartIssueSeverity.Error,
                    $"Plot '{plot.Type}' references missing axis ID {id}.",
                    partUri,
                    plot.SourceElementOrdinal
                ));
            }
        }
        foreach (var axis in axes.Where(axis => axis.CrossAxisId is not null && !ids.Contains(axis.CrossAxisId.Value)))
        {
            issues.Add(new WordChartIssue(
                "CHART_CROSS_AXIS_UNRESOLVED",
                WordChartIssueSeverity.Error,
                $"Axis {axis.AxisId} crosses missing axis {axis.CrossAxisId}.",
                partUri,
                axis.SourceElementOrdinal
            ));
        }
    }

    private LosslessXmlDocument ParseChartPart(
        OpcPart part,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return LosslessXmlDocument.Parse(
                part.Entry.Content,
                new LosslessXmlOptions
                {
                    MaxSourceBytes = _options.MaxChartPartBytes,
                    MaxXmlCharacters = _options.MaxChartPartBytes,
                    MaxXmlElements = _options.MaxElementsPerChart,
                    MaxXmlDepth = 256,
                    MaxTextCharacters = _options.MaxChartPartBytes,
                },
                cancellationToken
            );
        }
        catch (LosslessXmlLimitException exception)
        {
            throw new WordChartLimitException(
                "Chart part exceeds a chart-graph XML limit: " + exception.Message
            );
        }
        catch (LosslessXmlException exception)
        {
            throw new WordChartProjectionException(
                $"Chart part '{part.Uri}' is not safe, bounded, well-formed XML.",
                exception
            );
        }
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

    private static string? ExtractDrawingText(XElement element)
    {
        var values = element.Descendants()
            .Where(child =>
                child.Name.LocalName == "t"
                && (
                    child.Name.NamespaceName == DrawingTransitionalNamespace
                    || child.Name.NamespaceName == DrawingStrictNamespace
                )
            )
            .Select(child => child.Value)
            .ToArray();
        return values.Length == 0 ? null : string.Concat(values);
    }

    private static string? ExtractChartTitleText(XElement title, XNamespace c)
    {
        var drawingText = ExtractDrawingText(title);
        if (drawingText is not null)
        {
            return drawingText;
        }
        var cachedText = title.Descendants(c + "v").Select(element => element.Value).ToArray();
        return cachedText.Length == 0 ? null : string.Concat(cachedText);
    }

    private static IReadOnlyList<string> FindUnknownDataSourceElements(
        XElement roleElement,
        XElement? sourceElement,
        XNamespace c
    )
    {
        var unknown = roleElement.Elements()
            .Where(child => !ReferenceEquals(child, sourceElement))
            .Select(child => QualifiedName(child.Name))
            .ToList();
        if (sourceElement is not null)
        {
            var known = new HashSet<string>(StringComparer.Ordinal)
            {
                "extLst", "f", "formatCode", "lvl", "multiLvlStrCache", "numCache",
                "pt", "ptCount", "strCache",
            };
            unknown.AddRange(sourceElement.Elements()
                .Where(child => child.Name.Namespace != c || !known.Contains(child.Name.LocalName))
                .Select(child => QualifiedName(child.Name))
            );
        }
        return unknown.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }

    private static string? ChildValue(XElement parent, XName name) =>
        parent.Elements(name).SingleOrDefault()?.Attribute("val")?.Value;

    private static long? TryChildUnsignedLong(XElement parent, XName name) =>
        TryParseUnsignedLong(ChildValue(parent, name));

    private static long? TryParseUnsignedLong(string? value) =>
        uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private static long ParseLongValue(XElement element, string label, string partUri)
    {
        var value = element.Attribute("val")?.Value;
        if (!uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
        {
            throw new WordChartProjectionException(
                $"Chart part '{partUri}' contains an invalid {label}."
            );
        }
        return parsed;
    }

    private static bool? ParseBoolean(string? value) => value switch
    {
        null => null,
        "1" or "true" or "on" => true,
        "0" or "false" or "off" => false,
        _ => null,
    };

    private static string? RelationshipAttribute(XElement element, string localName) =>
        element.Attributes().FirstOrDefault(attribute =>
            attribute.Name.LocalName == localName
            && (
                attribute.Name.NamespaceName == RelationshipTransitionalNamespace
                || attribute.Name.NamespaceName == RelationshipStrictNamespace
            )
        )?.Value;

    private static IReadOnlyList<string> FindUnknownChildren(
        XElement parent,
        IReadOnlySet<string> knownLocalNames
    ) => parent.Elements()
        .Where(child => !knownLocalNames.Contains(child.Name.LocalName))
        .Select(child => QualifiedName(child.Name))
        .Distinct(StringComparer.Ordinal)
        .Order(StringComparer.Ordinal)
        .ToArray();

    private static string QualifiedName(XName name) =>
        $"{{{name.NamespaceName}}}{name.LocalName}";

    private static bool IsChartNamespace(string value) =>
        value is ChartTransitionalNamespace or ChartStrictNamespace;

    private static bool IsChartRelationship(string value) =>
        value is ChartRelationship or StrictChartRelationship;

    private static WordChartRelatedPartKind ClassifyRelatedPart(string relationshipType)
    {
        var suffix = relationshipType[(relationshipType.LastIndexOf('/') + 1)..];
        return suffix switch
        {
            "package" => WordChartRelatedPartKind.EmbeddedPackage,
            "image" => WordChartRelatedPartKind.Image,
            "chartStyle" => WordChartRelatedPartKind.Style,
            "chartColorStyle" => WordChartRelatedPartKind.ColorStyle,
            "chartUserShapes" => WordChartRelatedPartKind.ChartDrawing,
            "themeOverride" => WordChartRelatedPartKind.ThemeOverride,
            "hyperlink" => WordChartRelatedPartKind.Hyperlink,
            _ => WordChartRelatedPartKind.Other,
        };
    }

    private static string StableId(string prefix, params string[] components)
    {
        var payload = Encoding.UTF8.GetBytes(string.Join('\u001f', components));
        var hash = SHA256.HashData(payload);
        return prefix + Convert.ToBase64String(hash.AsSpan(0, 15))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private sealed class IssueState
    {
        private readonly int _maximum;
        private readonly List<WordChartIssue> _issues = new();

        public IssueState(int maximum) => _maximum = maximum;

        public IReadOnlyList<WordChartIssue> Issues => _issues;

        public bool Truncated { get; private set; }

        public void Add(WordChartIssue issue)
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

public class WordChartProjectionException : IOException
{
    public WordChartProjectionException(string message)
        : base(message) { }

    public WordChartProjectionException(string message, Exception innerException)
        : base(message, innerException) { }
}

public sealed class WordChartLimitException : WordChartProjectionException
{
    public WordChartLimitException(string message)
        : base(message) { }
}
