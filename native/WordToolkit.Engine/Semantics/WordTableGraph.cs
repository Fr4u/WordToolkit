using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Xml;

namespace WordToolkit.Engine.Semantics;

public enum WordTableIssueSeverity
{
    Info,
    Warning,
    Error,
}

public enum WordTableWidthKind
{
    Unspecified,
    Auto,
    Twips,
    Percent,
    Nil,
    Unknown,
}

public enum WordTableLayoutKind
{
    Unspecified,
    AutoFit,
    Fixed,
    Unknown,
}

public enum WordTableJustification
{
    Unspecified,
    Left,
    Center,
    Right,
    Start,
    End,
    Both,
    Unknown,
}

public enum WordTableMergeState
{
    None,
    Restart,
    Continue,
    Invalid,
}

public enum WordTableRowHeightRule
{
    Unspecified,
    Auto,
    AtLeast,
    Exact,
    Unknown,
}

public enum WordTableAnchor
{
    Page,
    Margin,
    Text,
    Unknown,
}

public sealed record WordTableWidth(
    WordTableWidthKind Kind,
    int? Value,
    decimal? Percent,
    bool IsValid
);

public sealed record WordFloatingTablePosition(
    bool Declared,
    bool IsEffectiveInWord,
    string? IgnoredReason,
    WordTableAnchor HorizontalAnchor,
    WordTableAnchor VerticalAnchor,
    string? HorizontalAlignment,
    string? VerticalAlignment,
    int? HorizontalPositionTwips,
    int? VerticalPositionTwips,
    int? LeftDistanceTwips,
    int? RightDistanceTwips,
    int? TopDistanceTwips,
    int? BottomDistanceTwips
);

public sealed record WordTablePropertyOverrides(
    bool Declared,
    WordTableWidth Width,
    WordTableJustification Justification,
    WordTableWidth CellSpacing,
    int PropertyCount
);

public sealed record WordTableIssue(
    string Id,
    string Code,
    WordTableIssueSeverity Severity,
    string Message,
    string? PartUri = null,
    int? SourceElementOrdinal = null,
    string? TableId = null,
    string? RowId = null,
    string? CellId = null,
    string? MergeId = null
);

public sealed record WordTableDefinition(
    string Id,
    SemanticNodeId SemanticNodeId,
    string PartUri,
    WordStoryKind StoryKind,
    int SourceElementOrdinal,
    string? ParentTableId,
    int Depth,
    string? StyleId,
    IReadOnlyList<WordTableWidth> GridColumns,
    int DeclaredGridColumnCount,
    int LogicalColumnCount,
    int RowCount,
    int CellCount,
    WordTableWidth Width,
    WordTableLayoutKind Layout,
    WordTableJustification Justification,
    WordTableWidth Indent,
    WordTableWidth CellSpacing,
    bool BidirectionalVisual,
    string? LookMask,
    string? Caption,
    string? Description,
    WordFloatingTablePosition FloatingPosition,
    string? VisualContinuationGroupId,
    IReadOnlyList<string> RowIds,
    IReadOnlyList<string> NestedTableIds
);

public sealed record WordTableRowDefinition(
    string Id,
    string TableId,
    SemanticNodeId SemanticNodeId,
    string PartUri,
    int SourceElementOrdinal,
    int RowIndex,
    int GridBefore,
    int GridAfter,
    int LogicalColumnCount,
    bool HeaderDeclared,
    bool HeaderEffective,
    bool CannotSplit,
    bool Hidden,
    int? HeightTwips,
    WordTableRowHeightRule HeightRule,
    WordTablePropertyOverrides PropertyOverrides,
    IReadOnlyList<string> CellIds
);

public sealed record WordTableCellDefinition(
    string Id,
    string TableId,
    string RowId,
    SemanticNodeId SemanticNodeId,
    string PartUri,
    int SourceElementOrdinal,
    int PhysicalCellIndex,
    int LogicalColumnStart,
    int LogicalColumnEnd,
    int GridSpan,
    WordTableWidth Width,
    WordTableMergeState VerticalMerge,
    WordTableMergeState LegacyHorizontalMerge,
    string? VerticalMergeId,
    string? VerticalMergeRootCellId,
    string? VerticalAlignment,
    string? TextDirection,
    bool NoWrap,
    bool FitText,
    IReadOnlyList<string> NestedTableIds
);

public sealed record WordTableVerticalMergeDefinition(
    string Id,
    string TableId,
    string RootCellId,
    int LogicalColumnStart,
    int LogicalColumnEnd,
    int GridSpan,
    int StartRowIndex,
    int RowSpan,
    bool IsComplete,
    IReadOnlyList<string> CellIds
);

public sealed class WordTableGraph
{
    private readonly IReadOnlyDictionary<string, WordTableDefinition> _tablesById;
    private readonly IReadOnlyDictionary<string, WordTableRowDefinition> _rowsById;
    private readonly IReadOnlyDictionary<string, WordTableCellDefinition> _cellsById;

    internal WordTableGraph(
        string packageFingerprint,
        string mainPartUri,
        IReadOnlyList<WordTableDefinition> tables,
        IReadOnlyList<WordTableRowDefinition> rows,
        IReadOnlyList<WordTableCellDefinition> cells,
        IReadOnlyList<WordTableVerticalMergeDefinition> verticalMerges,
        IReadOnlyList<WordTableIssue> issues,
        bool issuesTruncated,
        long parsedXmlBytes,
        int parsedXmlElements
    )
    {
        PackageFingerprint = packageFingerprint;
        MainPartUri = mainPartUri;
        Tables = new ReadOnlyCollection<WordTableDefinition>(tables.ToArray());
        Rows = new ReadOnlyCollection<WordTableRowDefinition>(rows.ToArray());
        Cells = new ReadOnlyCollection<WordTableCellDefinition>(cells.ToArray());
        VerticalMerges = new ReadOnlyCollection<WordTableVerticalMergeDefinition>(
            verticalMerges.ToArray()
        );
        Issues = new ReadOnlyCollection<WordTableIssue>(issues.ToArray());
        IssuesTruncated = issuesTruncated;
        ParsedXmlBytes = parsedXmlBytes;
        ParsedXmlElements = parsedXmlElements;
        _tablesById = new ReadOnlyDictionary<string, WordTableDefinition>(
            tables.ToDictionary(table => table.Id, StringComparer.Ordinal)
        );
        _rowsById = new ReadOnlyDictionary<string, WordTableRowDefinition>(
            rows.ToDictionary(row => row.Id, StringComparer.Ordinal)
        );
        _cellsById = new ReadOnlyDictionary<string, WordTableCellDefinition>(
            cells.ToDictionary(cell => cell.Id, StringComparer.Ordinal)
        );
    }

    public string PackageFingerprint { get; }

    public string MainPartUri { get; }

    public IReadOnlyList<WordTableDefinition> Tables { get; }

    public IReadOnlyList<WordTableRowDefinition> Rows { get; }

    public IReadOnlyList<WordTableCellDefinition> Cells { get; }

    public IReadOnlyList<WordTableVerticalMergeDefinition> VerticalMerges { get; }

    public IReadOnlyList<WordTableIssue> Issues { get; }

    public bool IssuesTruncated { get; }

    public long ParsedXmlBytes { get; }

    public int ParsedXmlElements { get; }

    public bool TryGetTable(string id, out WordTableDefinition? table) =>
        _tablesById.TryGetValue(id, out table);

    public bool TryGetRow(string id, out WordTableRowDefinition? row) =>
        _rowsById.TryGetValue(id, out row);

    public bool TryGetCell(string id, out WordTableCellDefinition? cell) =>
        _cellsById.TryGetValue(id, out cell);
}

public sealed record WordTableGraphOptions
{
    public static WordTableGraphOptions Default { get; } = new();

    public int MaxStoryParts { get; init; } = 256;

    public int MaxTables { get; init; } = 100_000;

    public int MaxRows { get; init; } = 1_000_000;

    public int MaxCells { get; init; } = 5_000_000;

    public int MaxGridColumnsPerTable { get; init; } = 65_536;

    public int MaxGridSpan { get; init; } = 65_536;

    public int MaxIssues { get; init; } = 10_000;

    public int MaxPartBytes { get; init; } = 128 * 1024 * 1024;

    public long MaxAggregateXmlBytes { get; init; } = 512L * 1024 * 1024;

    public int MaxElementsPerPart { get; init; } = 2_000_000;

    public int MaxAggregateElements { get; init; } = 5_000_000;

    public int MaxMetadataCharacters { get; init; } = 1_000_000;

    internal void Validate()
    {
        if (
            MaxStoryParts <= 0
            || MaxTables <= 0
            || MaxRows <= 0
            || MaxCells <= 0
            || MaxGridColumnsPerTable <= 0
            || MaxGridSpan <= 0
            || MaxIssues <= 0
            || MaxPartBytes <= 0
            || MaxAggregateXmlBytes <= 0
            || MaxElementsPerPart <= 0
            || MaxAggregateElements <= 0
            || MaxMetadataCharacters <= 0
        )
        {
            throw new ArgumentOutOfRangeException(
                nameof(WordTableGraphOptions),
                "All table graph limits must be positive."
            );
        }
        if (MaxPartBytes > MaxAggregateXmlBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxPartBytes));
        }
        if (MaxElementsPerPart > MaxAggregateElements)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxElementsPerPart));
        }
    }
}

public sealed class WordTableGraphBuilder
{
    private const string TransitionalWordNamespace =
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string StrictWordNamespace =
        "http://purl.oclc.org/ooxml/wordprocessingml/main";

    private readonly WordTableGraphOptions _options;

    public WordTableGraphBuilder(WordTableGraphOptions? options = null)
    {
        _options = options ?? WordTableGraphOptions.Default;
        _options.Validate();
    }

    public WordTableGraph Build(
        OpcPackageSnapshot package,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(package);
        var semantic = new WordSemanticProjector().Project(package, cancellationToken);
        return Build(package, semantic, cancellationToken);
    }

    public WordTableGraph Build(
        OpcPackageSnapshot package,
        WordSemanticDocument semanticDocument,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(semanticDocument);
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(
            package.Fingerprint,
            semanticDocument.PackageFingerprint,
            StringComparison.Ordinal
        ))
        {
            throw new WordTableProjectionException(
                "The semantic document does not belong to the supplied package snapshot."
            );
        }
        if (semanticDocument.ProjectedPartCount > _options.MaxStoryParts)
        {
            throw new WordTableLimitException(
                $"Projected story count exceeds {_options.MaxStoryParts}."
            );
        }

        var state = new BuildState(_options, semanticDocument);
        foreach (var partUri in semanticDocument.ProjectedPartUris)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!package.Parts.TryGetValue(partUri, out var part))
            {
                throw new WordTableProjectionException(
                    $"Projected story part '{partUri}' is missing from the package."
                );
            }
            var source = ParsePart(part, state, cancellationToken);
            ParseStoryPart(partUri, source, state, cancellationToken);
        }

        return state.Freeze(package.Fingerprint, semanticDocument.MainPartUri);
    }

    private LosslessXmlDocument ParsePart(
        OpcPart part,
        BuildState state,
        CancellationToken cancellationToken
    )
    {
        if (part.Entry.Content.Length > _options.MaxPartBytes)
        {
            throw new WordTableLimitException(
                $"Story part '{part.Uri}' exceeds {_options.MaxPartBytes} bytes."
            );
        }
        try
        {
            var source = LosslessXmlDocument.Parse(
                part.Entry.Content,
                new LosslessXmlOptions
                {
                    MaxSourceBytes = _options.MaxPartBytes,
                    MaxXmlCharacters = _options.MaxPartBytes,
                    MaxXmlElements = _options.MaxElementsPerPart,
                    MaxXmlDepth = 512,
                    MaxTextCharacters = _options.MaxPartBytes,
                },
                cancellationToken
            );
            state.AddParsedXml(part.Entry.Content.Length, source.Elements.Count);
            return source;
        }
        catch (LosslessXmlLimitException exception)
        {
            throw new WordTableLimitException(
                $"Part '{part.Uri}' exceeds an XML safety limit: {exception.Message}"
            );
        }
        catch (LosslessXmlException exception)
        {
            throw new WordTableProjectionException(
                $"Part '{part.Uri}' is not safe, well-formed XML.",
                exception
            );
        }
    }

    private void ParseStoryPart(
        string partUri,
        LosslessXmlDocument source,
        BuildState state,
        CancellationToken cancellationToken
    )
    {
        var root = source.ParsedDocument.Root
            ?? throw new WordTableProjectionException(
                $"Projected story part '{partUri}' has no root element."
            );
        var tableElements = root.DescendantsAndSelf()
            .Where(element => IsWordElement(element, "tbl"))
            .OrderBy(source.GetElementOrdinal)
            .ToArray();
        if (state.TableDrafts.Count + tableElements.Length > _options.MaxTables)
        {
            throw new WordTableLimitException(
                $"Table count exceeds {_options.MaxTables}."
            );
        }

        var tableIds = new Dictionary<XElement, string>(
            ReferenceEqualityComparer.Instance
        );
        foreach (var tableElement in tableElements)
        {
            var ordinal = source.GetElementOrdinal(tableElement);
            var semantic = state.RequiredSemantic(partUri, ordinal, WordSemanticNodeKind.Table);
            tableIds[tableElement] = StableId("wdt_", semantic.Id.Value);
        }

        var continuationGroups = BuildVisualContinuationGroups(
            tableElements,
            source,
            partUri
        );
        foreach (var tableElement in tableElements)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = tableIds[tableElement];
            var ordinal = source.GetElementOrdinal(tableElement);
            var semantic = state.RequiredSemantic(partUri, ordinal, WordSemanticNodeKind.Table);
            var parentElement = tableElement.Ancestors()
                .FirstOrDefault(element => IsWordElement(element, "tbl"));
            var parentTableId = parentElement is null ? null : tableIds[parentElement];
            var depth = tableElement.Ancestors().Count(element => IsWordElement(element, "tbl"));
            var properties = DirectChild(tableElement, "tblPr");
            var styleId = ValueAttribute(DirectChild(properties, "tblStyle"));
            state.AddMetadata(styleId);

            var story = StoryFor(semantic, state.SemanticDocument);
            var grid = DirectChild(tableElement, "tblGrid")?.Elements()
                .Where(element => IsWordElement(element, "gridCol"))
                .Select(element => ParseWidth(element, state, partUri, source.GetElementOrdinal(element), id))
                .ToArray() ?? [];
            if (grid.Length > _options.MaxGridColumnsPerTable)
            {
                throw new WordTableLimitException(
                    $"Table '{id}' has more than {_options.MaxGridColumnsPerTable} grid columns."
                );
            }
            var rowElements = tableElement.Descendants()
                .Where(element => IsWordElement(element, "tr"))
                .Where(element => ReferenceEquals(
                    element.Ancestors().FirstOrDefault(ancestor => IsWordElement(ancestor, "tbl")),
                    tableElement
                ))
                .OrderBy(source.GetElementOrdinal)
                .ToArray();
            if (state.RowDrafts.Count + rowElements.Length > _options.MaxRows)
            {
                throw new WordTableLimitException($"Row count exceeds {_options.MaxRows}.");
            }

            var tableDraft = new TableDraft(
                id,
                semantic,
                partUri,
                story.Kind,
                ordinal,
                parentTableId,
                depth,
                styleId,
                grid,
                ParseWidth(DirectChild(properties, "tblW"), state, partUri, ordinal, id),
                ParseLayout(DirectChild(properties, "tblLayout")),
                ParseJustification(DirectChild(properties, "jc")),
                ParseWidth(DirectChild(properties, "tblInd"), state, partUri, ordinal, id),
                ParseWidth(DirectChild(properties, "tblCellSpacing"), state, partUri, ordinal, id),
                OnOff(DirectChild(properties, "bidiVisual")),
                ValueAttribute(DirectChild(properties, "tblLook")),
                ValueAttribute(DirectChild(properties, "tblCaption")),
                ValueAttribute(DirectChild(properties, "tblDescription")),
                ParseFloating(
                    DirectChild(properties, "tblpPr"),
                    story.Kind,
                    state,
                    partUri,
                    ordinal,
                    id
                ),
                continuationGroups.GetValueOrDefault(tableElement)
            );
            state.AddMetadata(tableDraft.Caption);
            state.AddMetadata(tableDraft.Description);
            state.TableDrafts.Add(tableDraft);

            ParseRows(
                tableElement,
                rowElements,
                tableDraft,
                tableIds,
                source,
                state,
                cancellationToken
            );
        }
    }

    private static IReadOnlyDictionary<XElement, string> BuildVisualContinuationGroups(
        IReadOnlyList<XElement> tableElements,
        LosslessXmlDocument source,
        string partUri
    )
    {
        var result = new Dictionary<XElement, string>(
            ReferenceEqualityComparer.Instance
        );
        foreach (var table in tableElements)
        {
            var previous = table.ElementsBeforeSelf().LastOrDefault();
            if (previous is null || !IsWordElement(previous, "tbl"))
            {
                continue;
            }
            var previousStyle = ValueAttribute(
                DirectChild(DirectChild(previous, "tblPr"), "tblStyle")
            );
            var currentStyle = ValueAttribute(
                DirectChild(DirectChild(table, "tblPr"), "tblStyle")
            );
            if (!string.Equals(previousStyle, currentStyle, StringComparison.Ordinal))
            {
                continue;
            }
            var groupId = result.GetValueOrDefault(previous)
                ?? StableId(
                    "wdtvg_",
                    partUri,
                    source.GetElementOrdinal(previous)
                        .ToString(CultureInfo.InvariantCulture)
                );
            result[previous] = groupId;
            result[table] = groupId;
        }
        return result;
    }

    private void ParseRows(
        XElement tableElement,
        IReadOnlyList<XElement> rowElements,
        TableDraft table,
        IReadOnlyDictionary<XElement, string> tableIds,
        LosslessXmlDocument source,
        BuildState state,
        CancellationToken cancellationToken
    )
    {
        var headerPrefixOpen = true;
        var activeMerges = new Dictionary<(int Start, int End), MergeDraft>();
        for (var rowIndex = 0; rowIndex < rowElements.Count; rowIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rowElement = rowElements[rowIndex];
            var rowOrdinal = source.GetElementOrdinal(rowElement);
            var rowSemantic = state.RequiredSemantic(
                table.PartUri,
                rowOrdinal,
                WordSemanticNodeKind.TableRow
            );
            var rowId = StableId("wdtr_", rowSemantic.Id.Value);
            var rowProperties = DirectChild(rowElement, "trPr");
            var gridBefore = ParseNonNegativeInteger(
                ValueAttribute(DirectChild(rowProperties, "gridBefore")),
                "TABLE_GRID_BEFORE_INVALID",
                state,
                table,
                rowOrdinal,
                rowId
            );
            var gridAfter = ParseNonNegativeInteger(
                ValueAttribute(DirectChild(rowProperties, "gridAfter")),
                "TABLE_GRID_AFTER_INVALID",
                state,
                table,
                rowOrdinal,
                rowId
            );
            var headerDeclared = OnOff(DirectChild(rowProperties, "tblHeader"));
            var headerEffective = headerDeclared && headerPrefixOpen;
            if (!headerDeclared)
            {
                headerPrefixOpen = false;
            }
            else if (!headerEffective)
            {
                state.AddIssue(
                    "TABLE_HEADER_NOT_CONTIGUOUS",
                    WordTableIssueSeverity.Warning,
                    "A repeating header row is not part of the contiguous header prefix at the top of the table and is ignored by Word.",
                    table.PartUri,
                    rowOrdinal,
                    table.Id,
                    rowId
                );
            }

            var rowCells = rowElement.Descendants()
                .Where(element => IsWordElement(element, "tc"))
                .Where(element => ReferenceEquals(
                    element.Ancestors().FirstOrDefault(ancestor => IsWordElement(ancestor, "tr")),
                    rowElement
                ))
                .Where(element => ReferenceEquals(
                    element.Ancestors().FirstOrDefault(ancestor => IsWordElement(ancestor, "tbl")),
                    tableElement
                ))
                .OrderBy(source.GetElementOrdinal)
                .ToArray();
            if (state.CellDrafts.Count + rowCells.Length > _options.MaxCells)
            {
                throw new WordTableLimitException($"Cell count exceeds {_options.MaxCells}.");
            }

            var rowDraft = new RowDraft(
                rowId,
                table.Id,
                rowSemantic,
                table.PartUri,
                rowOrdinal,
                rowIndex,
                gridBefore,
                gridAfter,
                headerDeclared,
                headerEffective,
                OnOff(DirectChild(rowProperties, "cantSplit")),
                OnOff(DirectChild(rowProperties, "hidden")),
                ParseInteger(ValueAttribute(DirectChild(rowProperties, "trHeight"))),
                ParseHeightRule(AttributeByLocal(DirectChild(rowProperties, "trHeight"), "hRule")),
                ParseOverrides(DirectChild(rowElement, "tblPrEx"), state, table, rowOrdinal)
            );
            state.RowDrafts.Add(rowDraft);
            table.RowIds.Add(rowId);

            var continuedKeys = new HashSet<(int Start, int End)>();
            var cursor = gridBefore;
            for (var physicalIndex = 0; physicalIndex < rowCells.Length; physicalIndex++)
            {
                var cellElement = rowCells[physicalIndex];
                var cellOrdinal = source.GetElementOrdinal(cellElement);
                var cellSemantic = state.RequiredSemantic(
                    table.PartUri,
                    cellOrdinal,
                    WordSemanticNodeKind.TableCell
                );
                var cellId = StableId("wdtc_", cellSemantic.Id.Value);
                var cellProperties = DirectChild(cellElement, "tcPr");
                var span = ParseSpan(
                    ValueAttribute(DirectChild(cellProperties, "gridSpan")),
                    state,
                    table,
                    rowId,
                    cellOrdinal,
                    cellId
                );
                if (cursor > int.MaxValue - span)
                {
                    throw new WordTableLimitException("Logical table column index overflowed.");
                }
                var end = cursor + span;
                var verticalMerge = ParseMerge(DirectChild(cellProperties, "vMerge"));
                var horizontalMerge = ParseMerge(DirectChild(cellProperties, "hMerge"));
                if (verticalMerge == WordTableMergeState.Invalid)
                {
                    state.AddIssue(
                        "TABLE_VERTICAL_MERGE_VALUE_INVALID",
                        WordTableIssueSeverity.Error,
                        "The vertical merge value must be restart or continue.",
                        table.PartUri,
                        cellOrdinal,
                        table.Id,
                        rowId,
                        cellId
                    );
                }
                if (horizontalMerge == WordTableMergeState.Invalid)
                {
                    state.AddIssue(
                        "TABLE_HORIZONTAL_MERGE_VALUE_INVALID",
                        WordTableIssueSeverity.Error,
                        "The legacy horizontal merge value must be restart or continue.",
                        table.PartUri,
                        cellOrdinal,
                        table.Id,
                        rowId,
                        cellId
                    );
                }
                if (horizontalMerge != WordTableMergeState.None)
                {
                    state.AddIssue(
                        "TABLE_LEGACY_HORIZONTAL_MERGE",
                        WordTableIssueSeverity.Info,
                        "The cell uses legacy hMerge markup. It is preserved as a separate state and is not silently rewritten as gridSpan.",
                        table.PartUri,
                        cellOrdinal,
                        table.Id,
                        rowId,
                        cellId
                    );
                }
                var nestedTableIds = cellElement.Descendants()
                    .Where(element => IsWordElement(element, "tbl"))
                    .Where(element => ReferenceEquals(
                        element.Ancestors().FirstOrDefault(ancestor => IsWordElement(ancestor, "tc")),
                        cellElement
                    ))
                    .Where(element => ReferenceEquals(
                        element.Ancestors().FirstOrDefault(ancestor => IsWordElement(ancestor, "tbl")),
                        tableElement
                    ))
                    .Select(element => tableIds[element])
                    .ToArray();
                var cellDraft = new CellDraft(
                    cellId,
                    table.Id,
                    rowId,
                    cellSemantic,
                    table.PartUri,
                    cellOrdinal,
                    physicalIndex,
                    cursor,
                    end,
                    span,
                    ParseWidth(DirectChild(cellProperties, "tcW"), state, table.PartUri, cellOrdinal, table.Id),
                    verticalMerge,
                    horizontalMerge,
                    ValueAttribute(DirectChild(cellProperties, "vAlign")),
                    ValueAttribute(DirectChild(cellProperties, "textDirection")),
                    OnOff(DirectChild(cellProperties, "noWrap")),
                    OnOff(DirectChild(cellProperties, "tcFitText")),
                    nestedTableIds
                );
                state.CellDrafts.Add(cellDraft);
                rowDraft.CellIds.Add(cellId);
                table.NestedTableIds.AddRange(nestedTableIds);

                var key = (cursor, end);
                switch (verticalMerge)
                {
                    case WordTableMergeState.Restart:
                        if (activeMerges.Remove(key, out var replaced))
                        {
                            state.FinalizeMerge(replaced, isComplete: true);
                        }
                        var merge = new MergeDraft(
                            StableId("wdtvm_", table.Id, cellId),
                            table.Id,
                            cellId,
                            cursor,
                            end,
                            span,
                            rowIndex
                        );
                        merge.CellIds.Add(cellId);
                        activeMerges[key] = merge;
                        cellDraft.VerticalMergeId = merge.Id;
                        cellDraft.VerticalMergeRootCellId = cellId;
                        continuedKeys.Add(key);
                        break;
                    case WordTableMergeState.Continue:
                        if (activeMerges.TryGetValue(key, out var active))
                        {
                            active.CellIds.Add(cellId);
                            cellDraft.VerticalMergeId = active.Id;
                            cellDraft.VerticalMergeRootCellId = active.RootCellId;
                            continuedKeys.Add(key);
                        }
                        else
                        {
                            var overlaps = activeMerges.Keys.Any(activeKey =>
                                activeKey.Start < end && cursor < activeKey.End
                            );
                            state.AddIssue(
                                overlaps
                                    ? "TABLE_VERTICAL_MERGE_SPAN_MISMATCH"
                                    : "TABLE_VERTICAL_MERGE_ORPHAN_CONTINUATION",
                                WordTableIssueSeverity.Error,
                                overlaps
                                    ? "A vertical merge continuation does not use the same logical grid span as the active merge above it."
                                    : "A vertical merge continuation has no compatible restart cell in the preceding row.",
                                table.PartUri,
                                cellOrdinal,
                                table.Id,
                                rowId,
                                cellId
                            );
                        }
                        break;
                }
                cursor = end;
            }

            foreach (var staleKey in activeMerges.Keys.Where(key => !continuedKeys.Contains(key)).ToArray())
            {
                state.FinalizeMerge(activeMerges[staleKey], isComplete: true);
                activeMerges.Remove(staleKey);
            }
            rowDraft.LogicalColumnCount = cursor + gridAfter;
            table.LogicalColumnCount = Math.Max(table.LogicalColumnCount, rowDraft.LogicalColumnCount);
            if (table.GridColumns.Count > 0)
            {
                if (rowDraft.LogicalColumnCount > table.GridColumns.Count)
                {
                    state.AddIssue(
                        "TABLE_ROW_GRID_OVERFLOW",
                        WordTableIssueSeverity.Error,
                        "A row occupies more logical columns than the declared table grid.",
                        table.PartUri,
                        rowOrdinal,
                        table.Id,
                        rowId
                    );
                }
                else if (rowDraft.LogicalColumnCount < table.GridColumns.Count)
                {
                    state.AddIssue(
                        "TABLE_ROW_GRID_UNDERFLOW",
                        WordTableIssueSeverity.Warning,
                        "A row occupies fewer logical columns than the declared table grid.",
                        table.PartUri,
                        rowOrdinal,
                        table.Id,
                        rowId
                    );
                }
                if (gridBefore > table.GridColumns.Count || gridAfter > table.GridColumns.Count)
                {
                    state.AddIssue(
                        "TABLE_ROW_GRID_SKIP_OUT_OF_RANGE",
                        WordTableIssueSeverity.Error,
                        "gridBefore or gridAfter exceeds the declared table grid.",
                        table.PartUri,
                        rowOrdinal,
                        table.Id,
                        rowId
                    );
                }
            }
        }
        foreach (var active in activeMerges.Values)
        {
            state.FinalizeMerge(active, isComplete: true);
        }
    }

    private WordFloatingTablePosition ParseFloating(
        XElement? element,
        WordStoryKind storyKind,
        BuildState state,
        string partUri,
        int tableOrdinal,
        string tableId
    )
    {
        if (element is null)
        {
            return new WordFloatingTablePosition(
                false,
                false,
                null,
                WordTableAnchor.Text,
                WordTableAnchor.Margin,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null
            );
        }
        var horizontalAnchor = ParseAnchor(
            AttributeByLocal(element, "horzAnchor"),
            WordTableAnchor.Text
        );
        var verticalAnchor = ParseAnchor(
            AttributeByLocal(element, "vertAnchor"),
            WordTableAnchor.Margin
        );
        var horizontalAlignment = AttributeByLocal(element, "tblpXSpec");
        var verticalAlignment = AttributeByLocal(element, "tblpYSpec");
        var horizontalPosition = ParsePositionAttribute(element, "tblpX", state, partUri, tableOrdinal, tableId);
        var verticalPosition = ParsePositionAttribute(element, "tblpY", state, partUri, tableOrdinal, tableId);
        var left = ParsePositionAttribute(element, "leftFromText", state, partUri, tableOrdinal, tableId);
        var right = ParsePositionAttribute(element, "rightFromText", state, partUri, tableOrdinal, tableId);
        var top = ParsePositionAttribute(element, "topFromText", state, partUri, tableOrdinal, tableId);
        var bottom = ParsePositionAttribute(element, "bottomFromText", state, partUri, tableOrdinal, tableId);

        string? ignoredReason = storyKind switch
        {
            WordStoryKind.TextBox => "textbox_story",
            WordStoryKind.Footnote => "footnote_story",
            WordStoryKind.Endnote => "endnote_story",
            WordStoryKind.Comment => "comment_story",
            _ => null,
        };
        if (!element.HasAttributes)
        {
            ignoredReason ??= "empty_properties";
        }
        var allZeroDefault = horizontalPosition.GetValueOrDefault() == 0
            && verticalPosition.GetValueOrDefault() == 0
            && left.GetValueOrDefault() == 0
            && right.GetValueOrDefault() == 0
            && top.GetValueOrDefault() == 0
            && bottom.GetValueOrDefault() == 0
            && horizontalAlignment is null
            && verticalAlignment is null
            && horizontalAnchor == WordTableAnchor.Text
            && verticalAnchor == WordTableAnchor.Margin;
        if (element.HasAttributes && allZeroDefault)
        {
            ignoredReason ??= "word_all_zero_default_case";
        }
        return new WordFloatingTablePosition(
            true,
            ignoredReason is null,
            ignoredReason,
            horizontalAnchor,
            verticalAnchor,
            horizontalAlignment,
            verticalAlignment,
            horizontalPosition,
            verticalPosition,
            left,
            right,
            top,
            bottom
        );
    }

    private static WordTablePropertyOverrides ParseOverrides(
        XElement? element,
        BuildState state,
        TableDraft table,
        int rowOrdinal
    ) => element is null
        ? new WordTablePropertyOverrides(
            false,
            UnspecifiedWidth(),
            WordTableJustification.Unspecified,
            UnspecifiedWidth(),
            0
        )
        : new WordTablePropertyOverrides(
            true,
            ParseWidth(DirectChild(element, "tblW"), state, table.PartUri, rowOrdinal, table.Id),
            ParseJustification(DirectChild(element, "jc")),
            ParseWidth(DirectChild(element, "tblCellSpacing"), state, table.PartUri, rowOrdinal, table.Id),
            element.Elements().Count(IsWordElement)
        );

    private static WordTableWidth ParseWidth(
        XElement? element,
        BuildState state,
        string partUri,
        int ordinal,
        string tableId
    )
    {
        if (element is null)
        {
            return UnspecifiedWidth();
        }
        var rawType = AttributeByLocal(element, "type");
        var kind = rawType switch
        {
            null or "dxa" when element.Name.LocalName == "gridCol" => WordTableWidthKind.Twips,
            null => WordTableWidthKind.Unspecified,
            "auto" => WordTableWidthKind.Auto,
            "dxa" => WordTableWidthKind.Twips,
            "pct" => WordTableWidthKind.Percent,
            "nil" => WordTableWidthKind.Nil,
            _ => WordTableWidthKind.Unknown,
        };
        var rawValue = AttributeByLocal(element, "w");
        var valid = rawValue is null || int.TryParse(
            rawValue,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out _
        );
        var value = valid ? ParseInteger(rawValue) : null;
        if (!valid || value < 0 || kind == WordTableWidthKind.Unknown)
        {
            state.AddIssue(
                "TABLE_WIDTH_INVALID",
                WordTableIssueSeverity.Warning,
                "A table width uses an unknown unit or a non-integer/negative value.",
                partUri,
                ordinal,
                tableId
            );
            valid = false;
        }
        return new WordTableWidth(
            kind,
            value,
            kind == WordTableWidthKind.Percent && value is not null
                ? value.Value / 50m
                : null,
            valid
        );
    }

    private static WordTableWidth UnspecifiedWidth() => new(
        WordTableWidthKind.Unspecified,
        null,
        null,
        true
    );

    private static WordTableLayoutKind ParseLayout(XElement? element) =>
        AttributeByLocal(element, "type") switch
        {
            null => WordTableLayoutKind.Unspecified,
            "autofit" => WordTableLayoutKind.AutoFit,
            "fixed" => WordTableLayoutKind.Fixed,
            _ => WordTableLayoutKind.Unknown,
        };

    private static WordTableJustification ParseJustification(XElement? element) =>
        ValueAttribute(element) switch
        {
            null => WordTableJustification.Unspecified,
            "left" => WordTableJustification.Left,
            "center" => WordTableJustification.Center,
            "right" => WordTableJustification.Right,
            "start" => WordTableJustification.Start,
            "end" => WordTableJustification.End,
            "both" => WordTableJustification.Both,
            _ => WordTableJustification.Unknown,
        };

    private static WordTableRowHeightRule ParseHeightRule(string? value) => value switch
    {
        null => WordTableRowHeightRule.Unspecified,
        "auto" => WordTableRowHeightRule.Auto,
        "atLeast" => WordTableRowHeightRule.AtLeast,
        "exact" => WordTableRowHeightRule.Exact,
        _ => WordTableRowHeightRule.Unknown,
    };

    private static WordTableAnchor ParseAnchor(string? value, WordTableAnchor defaultValue) =>
        value switch
        {
            null => defaultValue,
            "page" => WordTableAnchor.Page,
            "margin" => WordTableAnchor.Margin,
            "text" => WordTableAnchor.Text,
            _ => WordTableAnchor.Unknown,
        };

    private static WordTableMergeState ParseMerge(XElement? element)
    {
        if (element is null)
        {
            return WordTableMergeState.None;
        }
        return ValueAttribute(element) switch
        {
            null or "continue" => WordTableMergeState.Continue,
            "restart" => WordTableMergeState.Restart,
            _ => WordTableMergeState.Invalid,
        };
    }

    private int ParseSpan(
        string? value,
        BuildState state,
        TableDraft table,
        string rowId,
        int ordinal,
        string cellId
    )
    {
        if (value is null)
        {
            return 1;
        }
        if (
            int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var span)
            && span > 0
            && span <= _options.MaxGridSpan
        )
        {
            return span;
        }
        state.AddIssue(
            "TABLE_GRID_SPAN_INVALID",
            WordTableIssueSeverity.Error,
            $"gridSpan must be between 1 and {_options.MaxGridSpan}; the logical model uses a fallback span of one.",
            table.PartUri,
            ordinal,
            table.Id,
            rowId,
            cellId
        );
        return 1;
    }

    private static int ParseNonNegativeInteger(
        string? value,
        string code,
        BuildState state,
        TableDraft table,
        int ordinal,
        string rowId
    )
    {
        if (value is null)
        {
            return 0;
        }
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) && result >= 0)
        {
            return result;
        }
        state.AddIssue(
            code,
            WordTableIssueSeverity.Error,
            "A row grid skip must be a non-negative integer; the logical model uses zero.",
            table.PartUri,
            ordinal,
            table.Id,
            rowId
        );
        return 0;
    }

    private static int? ParsePositionAttribute(
        XElement element,
        string localName,
        BuildState state,
        string partUri,
        int ordinal,
        string tableId
    )
    {
        var value = AttributeByLocal(element, localName);
        if (value is null)
        {
            return null;
        }
        if (
            int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            && parsed is >= 0 and <= 32767
        )
        {
            return parsed;
        }
        state.AddIssue(
            "TABLE_FLOATING_POSITION_OUT_OF_WORD_RANGE",
            WordTableIssueSeverity.Warning,
            $"Word accepts {localName} only in the range 0..32767 twips.",
            partUri,
            ordinal,
            tableId
        );
        return null;
    }

    private static int? ParseInteger(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;

    private static bool OnOff(XElement? element)
    {
        if (element is null)
        {
            return false;
        }
        return ValueAttribute(element) switch
        {
            null or "1" or "true" or "on" => true,
            _ => false,
        };
    }

    private static XElement? DirectChild(XElement? parent, string localName) =>
        parent?.Elements().FirstOrDefault(element => IsWordElement(element, localName));

    private static string? ValueAttribute(XElement? element) =>
        AttributeByLocal(element, "val");

    private static string? AttributeByLocal(XElement? element, string localName) =>
        element?.Attributes().FirstOrDefault(attribute =>
            !attribute.IsNamespaceDeclaration && attribute.Name.LocalName == localName
        )?.Value;

    private static bool IsWordElement(XElement element) =>
        IsWordNamespace(element.Name.NamespaceName);

    private static bool IsWordElement(XElement element, string localName) =>
        element.Name.LocalName == localName && IsWordNamespace(element.Name.NamespaceName);

    private static bool IsWordNamespace(string value) =>
        value is TransitionalWordNamespace or StrictWordNamespace;

    private static StoryLocation StoryFor(
        WordSemanticNode node,
        WordSemanticDocument document
    )
    {
        WordSemanticNode? current = node;
        while (current is not null)
        {
            var kind = current.Kind switch
            {
                WordSemanticNodeKind.TextBox => WordStoryKind.TextBox,
                WordSemanticNodeKind.Footnote => WordStoryKind.Footnote,
                WordSemanticNodeKind.Endnote => WordStoryKind.Endnote,
                WordSemanticNodeKind.Comment => WordStoryKind.Comment,
                WordSemanticNodeKind.GlossaryEntry => WordStoryKind.GlossaryEntry,
                WordSemanticNodeKind.Header => WordStoryKind.Header,
                WordSemanticNodeKind.Footer => WordStoryKind.Footer,
                WordSemanticNodeKind.Document => WordStoryKind.Main,
                _ => (WordStoryKind?)null,
            };
            if (kind is not null)
            {
                return new StoryLocation(kind.Value, current.Id);
            }
            current = current.ParentId is { } parentId
                && document.TryGetNode(parentId, out var parent)
                    ? parent
                    : null;
        }
        return new StoryLocation(WordStoryKind.Other, null);
    }

    private static string StableId(string prefix, params string[] values)
    {
        var material = string.Join('\u001f', values);
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return prefix + Convert.ToBase64String(digest.AsSpan(0, 15))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private sealed class BuildState
    {
        private readonly WordTableGraphOptions _options;
        private readonly Dictionary<(string PartUri, int Ordinal), WordSemanticNode> _semanticBySource;
        private readonly List<WordTableVerticalMergeDefinition> _merges = [];
        private readonly List<WordTableIssue> _issues = [];
        private long _metadataCharacters;

        public BuildState(
            WordTableGraphOptions options,
            WordSemanticDocument semanticDocument
        )
        {
            _options = options;
            SemanticDocument = semanticDocument;
            _semanticBySource = semanticDocument.Nodes.ToDictionary(
                node => (node.SourcePartUri, node.SourceElementOrdinal)
            );
        }

        public WordSemanticDocument SemanticDocument { get; }

        public List<TableDraft> TableDrafts { get; } = [];

        public List<RowDraft> RowDrafts { get; } = [];

        public List<CellDraft> CellDrafts { get; } = [];

        public long ParsedXmlBytes { get; private set; }

        public int ParsedXmlElements { get; private set; }

        public bool IssuesTruncated { get; private set; }

        public void AddParsedXml(int bytes, int elements)
        {
            ParsedXmlBytes += bytes;
            ParsedXmlElements += elements;
            if (ParsedXmlBytes > _options.MaxAggregateXmlBytes)
            {
                throw new WordTableLimitException(
                    $"Aggregate story XML exceeds {_options.MaxAggregateXmlBytes} bytes."
                );
            }
            if (ParsedXmlElements > _options.MaxAggregateElements)
            {
                throw new WordTableLimitException(
                    $"Aggregate story XML exceeds {_options.MaxAggregateElements} elements."
                );
            }
        }

        public void AddMetadata(string? value)
        {
            if (value is null)
            {
                return;
            }
            _metadataCharacters += value.Length;
            if (_metadataCharacters > _options.MaxMetadataCharacters)
            {
                throw new WordTableLimitException(
                    $"Table metadata exceeds {_options.MaxMetadataCharacters} characters."
                );
            }
        }

        public WordSemanticNode RequiredSemantic(
            string partUri,
            int ordinal,
            WordSemanticNodeKind expectedKind
        )
        {
            if (
                !_semanticBySource.TryGetValue((partUri, ordinal), out var node)
                || node.Kind != expectedKind
            )
            {
                throw new WordTableProjectionException(
                    $"Table source {partUri}#{ordinal} has no matching {expectedKind} semantic node."
                );
            }
            return node;
        }

        public void AddIssue(
            string code,
            WordTableIssueSeverity severity,
            string message,
            string? partUri = null,
            int? sourceElementOrdinal = null,
            string? tableId = null,
            string? rowId = null,
            string? cellId = null,
            string? mergeId = null
        )
        {
            if (_issues.Count >= _options.MaxIssues)
            {
                IssuesTruncated = true;
                return;
            }
            var id = StableId(
                "wdti_",
                code,
                partUri ?? "",
                sourceElementOrdinal?.ToString(CultureInfo.InvariantCulture) ?? "",
                tableId ?? "",
                rowId ?? "",
                cellId ?? "",
                mergeId ?? ""
            );
            _issues.Add(
                new WordTableIssue(
                    id,
                    code,
                    severity,
                    message,
                    partUri,
                    sourceElementOrdinal,
                    tableId,
                    rowId,
                    cellId,
                    mergeId
                )
            );
        }

        public void FinalizeMerge(MergeDraft merge, bool isComplete)
        {
            if (_merges.Any(item => item.Id == merge.Id))
            {
                return;
            }
            _merges.Add(
                new WordTableVerticalMergeDefinition(
                    merge.Id,
                    merge.TableId,
                    merge.RootCellId,
                    merge.Start,
                    merge.End,
                    merge.End - merge.Start,
                    merge.StartRowIndex,
                    merge.CellIds.Count,
                    isComplete,
                    merge.CellIds.ToArray()
                )
            );
        }

        public WordTableGraph Freeze(string packageFingerprint, string mainPartUri)
        {
            var tableById = TableDrafts.ToDictionary(table => table.Id, StringComparer.Ordinal);
            foreach (var table in TableDrafts)
            {
                table.NestedTableIds.Clear();
            }
            foreach (var table in TableDrafts.Where(table => table.ParentTableId is not null))
            {
                tableById[table.ParentTableId!].NestedTableIds.Add(table.Id);
            }
            var cellCounts = CellDrafts
                .GroupBy(cell => cell.TableId, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.Count(),
                    StringComparer.Ordinal
                );
            var tables = TableDrafts.Select(table => table.Freeze(
                cellCounts.GetValueOrDefault(table.Id)
            )).ToArray();
            var rows = RowDrafts.Select(row => row.Freeze()).ToArray();
            var cells = CellDrafts.Select(cell => cell.Freeze()).ToArray();
            return new WordTableGraph(
                packageFingerprint,
                mainPartUri,
                tables,
                rows,
                cells,
                _merges.OrderBy(merge => merge.StartRowIndex)
                    .ThenBy(merge => merge.LogicalColumnStart)
                    .ToArray(),
                _issues.OrderByDescending(issue => issue.Severity)
                    .ThenBy(issue => issue.SourceElementOrdinal)
                    .ThenBy(issue => issue.Code, StringComparer.Ordinal)
                    .ToArray(),
                IssuesTruncated,
                ParsedXmlBytes,
                ParsedXmlElements
            );
        }
    }

    private sealed class TableDraft(
        string id,
        WordSemanticNode semantic,
        string partUri,
        WordStoryKind storyKind,
        int sourceElementOrdinal,
        string? parentTableId,
        int depth,
        string? styleId,
        IReadOnlyList<WordTableWidth> gridColumns,
        WordTableWidth width,
        WordTableLayoutKind layout,
        WordTableJustification justification,
        WordTableWidth indent,
        WordTableWidth cellSpacing,
        bool bidirectionalVisual,
        string? lookMask,
        string? caption,
        string? description,
        WordFloatingTablePosition floatingPosition,
        string? visualContinuationGroupId
    )
    {
        public string Id { get; } = id;
        public WordSemanticNode Semantic { get; } = semantic;
        public string PartUri { get; } = partUri;
        public WordStoryKind StoryKind { get; } = storyKind;
        public int SourceElementOrdinal { get; } = sourceElementOrdinal;
        public string? ParentTableId { get; } = parentTableId;
        public int Depth { get; } = depth;
        public string? StyleId { get; } = styleId;
        public IReadOnlyList<WordTableWidth> GridColumns { get; } = gridColumns;
        public WordTableWidth Width { get; } = width;
        public WordTableLayoutKind Layout { get; } = layout;
        public WordTableJustification Justification { get; } = justification;
        public WordTableWidth Indent { get; } = indent;
        public WordTableWidth CellSpacing { get; } = cellSpacing;
        public bool BidirectionalVisual { get; } = bidirectionalVisual;
        public string? LookMask { get; } = lookMask;
        public string? Caption { get; } = caption;
        public string? Description { get; } = description;
        public WordFloatingTablePosition FloatingPosition { get; } = floatingPosition;
        public string? VisualContinuationGroupId { get; } = visualContinuationGroupId;
        public List<string> RowIds { get; } = [];
        public List<string> NestedTableIds { get; } = [];
        public int LogicalColumnCount { get; set; }

        public WordTableDefinition Freeze(int cellCount) => new(
            Id,
            Semantic.Id,
            PartUri,
            StoryKind,
            SourceElementOrdinal,
            ParentTableId,
            Depth,
            StyleId,
            GridColumns.ToArray(),
            GridColumns.Count,
            LogicalColumnCount,
            RowIds.Count,
            cellCount,
            Width,
            Layout,
            Justification,
            Indent,
            CellSpacing,
            BidirectionalVisual,
            LookMask,
            Caption,
            Description,
            FloatingPosition,
            VisualContinuationGroupId,
            RowIds.ToArray(),
            NestedTableIds.Distinct(StringComparer.Ordinal).ToArray()
        );
    }

    private sealed class RowDraft(
        string id,
        string tableId,
        WordSemanticNode semantic,
        string partUri,
        int sourceElementOrdinal,
        int rowIndex,
        int gridBefore,
        int gridAfter,
        bool headerDeclared,
        bool headerEffective,
        bool cannotSplit,
        bool hidden,
        int? heightTwips,
        WordTableRowHeightRule heightRule,
        WordTablePropertyOverrides propertyOverrides
    )
    {
        public string Id { get; } = id;
        public string TableId { get; } = tableId;
        public WordSemanticNode Semantic { get; } = semantic;
        public string PartUri { get; } = partUri;
        public int SourceElementOrdinal { get; } = sourceElementOrdinal;
        public int RowIndex { get; } = rowIndex;
        public int GridBefore { get; } = gridBefore;
        public int GridAfter { get; } = gridAfter;
        public bool HeaderDeclared { get; } = headerDeclared;
        public bool HeaderEffective { get; } = headerEffective;
        public bool CannotSplit { get; } = cannotSplit;
        public bool Hidden { get; } = hidden;
        public int? HeightTwips { get; } = heightTwips;
        public WordTableRowHeightRule HeightRule { get; } = heightRule;
        public WordTablePropertyOverrides PropertyOverrides { get; } = propertyOverrides;
        public List<string> CellIds { get; } = [];
        public int LogicalColumnCount { get; set; }

        public WordTableRowDefinition Freeze() => new(
            Id,
            TableId,
            Semantic.Id,
            PartUri,
            SourceElementOrdinal,
            RowIndex,
            GridBefore,
            GridAfter,
            LogicalColumnCount,
            HeaderDeclared,
            HeaderEffective,
            CannotSplit,
            Hidden,
            HeightTwips,
            HeightRule,
            PropertyOverrides,
            CellIds.ToArray()
        );
    }

    private sealed class CellDraft(
        string id,
        string tableId,
        string rowId,
        WordSemanticNode semantic,
        string partUri,
        int sourceElementOrdinal,
        int physicalCellIndex,
        int logicalColumnStart,
        int logicalColumnEnd,
        int gridSpan,
        WordTableWidth width,
        WordTableMergeState verticalMerge,
        WordTableMergeState legacyHorizontalMerge,
        string? verticalAlignment,
        string? textDirection,
        bool noWrap,
        bool fitText,
        IReadOnlyList<string> nestedTableIds
    )
    {
        public string Id { get; } = id;
        public string TableId { get; } = tableId;
        public string RowId { get; } = rowId;
        public WordSemanticNode Semantic { get; } = semantic;
        public string PartUri { get; } = partUri;
        public int SourceElementOrdinal { get; } = sourceElementOrdinal;
        public int PhysicalCellIndex { get; } = physicalCellIndex;
        public int LogicalColumnStart { get; } = logicalColumnStart;
        public int LogicalColumnEnd { get; } = logicalColumnEnd;
        public int GridSpan { get; } = gridSpan;
        public WordTableWidth Width { get; } = width;
        public WordTableMergeState VerticalMerge { get; } = verticalMerge;
        public WordTableMergeState LegacyHorizontalMerge { get; } = legacyHorizontalMerge;
        public string? VerticalMergeId { get; set; }
        public string? VerticalMergeRootCellId { get; set; }
        public string? VerticalAlignment { get; } = verticalAlignment;
        public string? TextDirection { get; } = textDirection;
        public bool NoWrap { get; } = noWrap;
        public bool FitText { get; } = fitText;
        public IReadOnlyList<string> NestedTableIds { get; } = nestedTableIds;

        public WordTableCellDefinition Freeze() => new(
            Id,
            TableId,
            RowId,
            Semantic.Id,
            PartUri,
            SourceElementOrdinal,
            PhysicalCellIndex,
            LogicalColumnStart,
            LogicalColumnEnd,
            GridSpan,
            Width,
            VerticalMerge,
            LegacyHorizontalMerge,
            VerticalMergeId,
            VerticalMergeRootCellId,
            VerticalAlignment,
            TextDirection,
            NoWrap,
            FitText,
            NestedTableIds.ToArray()
        );
    }

    private sealed class MergeDraft(
        string id,
        string tableId,
        string rootCellId,
        int start,
        int end,
        int gridSpan,
        int startRowIndex
    )
    {
        public string Id { get; } = id;
        public string TableId { get; } = tableId;
        public string RootCellId { get; } = rootCellId;
        public int Start { get; } = start;
        public int End { get; } = end;
        public int GridSpan { get; } = gridSpan;
        public int StartRowIndex { get; } = startRowIndex;
        public List<string> CellIds { get; } = [];
    }

    private sealed record StoryLocation(WordStoryKind Kind, SemanticNodeId? NodeId);
}

public class WordTableProjectionException : IOException
{
    public WordTableProjectionException(string message)
        : base(message) { }

    public WordTableProjectionException(string message, Exception innerException)
        : base(message, innerException) { }
}

public sealed class WordTableLimitException : WordTableProjectionException
{
    public WordTableLimitException(string message)
        : base(message) { }
}
