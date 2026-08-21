using System.Diagnostics;
using System.Text.Json;
using WordToolkit.Engine.Packaging;
using WordToolkit.Engine.Semantics;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Word;

internal sealed partial class WordLiveService
{
    private const int TableNestedIdLimit = 100;
    private const int TableGridWidthLimit = 100;

    private static Task<object> InspectPackageTablesAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        var started = Stopwatch.GetTimestamp();
        cancellationToken.ThrowIfCancellationRequested();
        var path = ResolveInspectablePackagePath(arguments);
        var view = arguments.String("view", "summary");
        if (view is not "summary" and not "tables" and not "rows" and not "cells"
            and not "merges" and not "issues")
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "view must be summary, tables, rows, cells, merges, or issues"
            );
        }

        var offset = arguments.NullableInt64("offset") ?? 0;
        var maximum = arguments.NullableInt64("max_items") ?? 30;
        if (offset is < 0 or > int.MaxValue)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "offset must be between 0 and 2147483647"
            );
        }
        if (maximum is < 1 or > 100)
        {
            throw new NativeToolException(
                "INVALID_INPUT",
                "max_items must be between 1 and 100"
            );
        }

        var tableId = BoundedOptionalArgument(arguments, "table_id", 128);
        var rowId = BoundedOptionalArgument(arguments, "row_id", 128);
        var cellId = BoundedOptionalArgument(arguments, "cell_id", 128);
        var includeLayout = arguments.Boolean("include_layout", false);
        var includeNames = arguments.Boolean("include_names", false);
        var includeSource = arguments.Boolean("include_source", false);
        var includeIssues = arguments.Boolean("include_issues", true);

        try
        {
            var package = new OpcPackageReader().Read(path, cancellationToken);
            var semantic = new WordSemanticProjector().Project(
                package,
                cancellationToken
            );
            var graph = new WordTableGraphBuilder().Build(
                package,
                semantic,
                cancellationToken
            );
            if (tableId is not null && !graph.TryGetTable(tableId, out _))
            {
                throw new NativeToolException(
                    "NOT_FOUND",
                    "The table does not exist in this package fingerprint"
                );
            }
            if (rowId is not null && !graph.TryGetRow(rowId, out _))
            {
                throw new NativeToolException(
                    "NOT_FOUND",
                    "The table row does not exist in this package fingerprint"
                );
            }
            if (cellId is not null && !graph.TryGetCell(cellId, out _))
            {
                throw new NativeToolException(
                    "NOT_FOUND",
                    "The table cell does not exist in this package fingerprint"
                );
            }

            var selectedCells = graph.Cells.Where(cell =>
                (tableId is null || cell.TableId == tableId)
                && (rowId is null || cell.RowId == rowId)
                && (cellId is null || cell.Id == cellId)
            ).ToArray();
            var selectedRowIds = selectedCells.Select(cell => cell.RowId)
                .ToHashSet(StringComparer.Ordinal);
            if (cellId is null)
            {
                foreach (var row in graph.Rows.Where(row =>
                    (tableId is null || row.TableId == tableId)
                    && (rowId is null || row.Id == rowId)
                ))
                {
                    selectedRowIds.Add(row.Id);
                }
            }
            var selectedRows = graph.Rows.Where(row => selectedRowIds.Contains(row.Id))
                .ToArray();
            var selectedTableIds = selectedRows.Select(row => row.TableId)
                .ToHashSet(StringComparer.Ordinal);
            if (rowId is null && cellId is null)
            {
                foreach (var table in graph.Tables.Where(table =>
                    tableId is null || table.Id == tableId
                ))
                {
                    selectedTableIds.Add(table.Id);
                }
            }
            var selectedTables = graph.Tables.Where(table =>
                selectedTableIds.Contains(table.Id)
            ).ToArray();
            var selectedMerges = graph.VerticalMerges.Where(merge =>
                selectedTableIds.Contains(merge.TableId)
                && (cellId is null || merge.CellIds.Contains(cellId, StringComparer.Ordinal))
                && (rowId is null || merge.CellIds.Any(id =>
                    graph.TryGetCell(id, out var cell) && cell!.RowId == rowId
                ))
            ).ToArray();
            var selectedIssues = graph.Issues.Where(issue =>
                (tableId is null || issue.TableId == tableId)
                && (rowId is null || issue.RowId == rowId)
                && (cellId is null || issue.CellId == cellId)
            ).ToArray();

            var page = TableInspectionItems(
                view,
                selectedTables,
                selectedRows,
                selectedCells,
                selectedMerges,
                selectedIssues,
                includeLayout,
                includeNames,
                includeSource,
                (int)offset,
                (int)maximum
            );
            var consumed = (long)offset + page.Items.Length;
            var issuePage = includeIssues && view != "issues"
                ? selectedIssues.Take(20)
                    .Select(issue => TableIssueItem(issue, includeSource))
                    .ToArray()
                : null;

            return Task.FromResult<object>(new
            {
                file_name = Path.GetFileName(path),
                package_fingerprint = graph.PackageFingerprint,
                table_count = selectedTables.Length,
                nested_table_count = selectedTables.Count(table => table.Depth > 0),
                floating_table_count = selectedTables.Count(table =>
                    table.FloatingPosition.Declared
                ),
                effective_floating_table_count = selectedTables.Count(table =>
                    table.FloatingPosition.IsEffectiveInWord
                ),
                row_count = selectedRows.Length,
                repeating_header_row_count = selectedRows.Count(row =>
                    row.HeaderEffective
                ),
                cell_count = selectedCells.Length,
                horizontally_spanned_cell_count = selectedCells.Count(cell =>
                    cell.GridSpan > 1
                ),
                vertical_merge_count = selectedMerges.Length,
                issue_count = selectedIssues.Length,
                issues_truncated_at_source = graph.IssuesTruncated,
                parsed_xml_bytes = graph.ParsedXmlBytes,
                parsed_xml_elements = graph.ParsedXmlElements,
                execution_policy =
                    "parse_only_never_return_cell_text_or_raw_xml",
                word_opened = false,
                package_mutated = false,
                cell_text_included = false,
                raw_xml_included = false,
                layout_included = includeLayout,
                names_included = includeNames,
                source_included = includeSource,
                view,
                table_id = tableId,
                row_id = rowId,
                cell_id = cellId,
                matched_item_count = page.MatchedCount,
                offset,
                returned_item_count = page.Items.Length,
                next_offset = consumed < page.MatchedCount
                    ? (int)consumed
                    : (int?)null,
                items = page.Items,
                issues = issuePage,
                issues_truncated = issuePage is not null
                    && selectedIssues.Length > issuePage.Length,
                runtime = "dotnet-native",
                python_used = false,
                performance = new
                {
                    total_ms = Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                },
            });
        }
        catch (WordTableLimitException exception)
        {
            throw new NativeToolException(
                "PACKAGE_LIMIT",
                "The table graph exceeds a bounded safety limit",
                new { reason = BoundForResponse(exception.Message, 512) }
            );
        }
        catch (WordTableProjectionException exception)
        {
            throw new NativeToolException(
                "INVALID_WORD_PACKAGE",
                "The package cannot be resolved into a table graph",
                new { reason = BoundForResponse(exception.Message, 512) }
            );
        }
        catch (WordSemanticLimitException exception)
        {
            throw new NativeToolException(
                "PACKAGE_LIMIT",
                "Semantic projection exceeds a bounded safety limit",
                new { reason = BoundForResponse(exception.Message, 512) }
            );
        }
        catch (WordSemanticProjectionException exception)
        {
            throw new NativeToolException(
                "INVALID_WORD_PACKAGE",
                "The package cannot be projected as a Word semantic document",
                new { reason = BoundForResponse(exception.Message, 512) }
            );
        }
        catch (OpcPackageLimitException exception)
        {
            throw new NativeToolException(
                "PACKAGE_LIMIT",
                "The package exceeds a bounded safety limit",
                new { reason = BoundForResponse(exception.Message, 512) }
            );
        }
        catch (InvalidDataException exception)
        {
            throw new NativeToolException(
                "INVALID_PACKAGE",
                "The file is not a readable OPC ZIP package",
                new { reason = BoundForResponse(exception.Message, 512) }
            );
        }
        catch (UnauthorizedAccessException)
        {
            throw new NativeToolException(
                "ACCESS_DENIED",
                "The Word package cannot be read with current permissions"
            );
        }
        catch (IOException exception)
        {
            throw new NativeToolException(
                "IO_ERROR",
                "The table graph could not be read",
                new { reason = BoundForResponse(exception.Message, 512) }
            );
        }
    }

    private static TableInspectionPage TableInspectionItems(
        string view,
        IReadOnlyList<WordTableDefinition> tables,
        IReadOnlyList<WordTableRowDefinition> rows,
        IReadOnlyList<WordTableCellDefinition> cells,
        IReadOnlyList<WordTableVerticalMergeDefinition> merges,
        IReadOnlyList<WordTableIssue> issues,
        bool includeLayout,
        bool includeNames,
        bool includeSource,
        int offset,
        int maximum
    )
    {
        IEnumerable<object> items;
        int matchedCount;
        switch (view)
        {
            case "summary":
                var storyKinds = tables.GroupBy(table => table.StoryKind)
                    .OrderBy(group => group.Key)
                    .Select(group => (object)new
                    {
                        category = "story_kind",
                        name = ToSnakeCase(group.Key.ToString()),
                        count = group.Count(),
                    });
                var issueSeverities = issues.GroupBy(issue => issue.Severity)
                    .OrderBy(group => group.Key)
                    .Select(group => (object)new
                    {
                        category = "issue_severity",
                        name = ToSnakeCase(group.Key.ToString()),
                        count = group.Count(),
                    });
                var summary = storyKinds.Concat(issueSeverities).ToArray();
                items = summary;
                matchedCount = summary.Length;
                break;
            case "tables":
                items = tables.Select(table => TableItem(
                    table,
                    includeLayout,
                    includeNames,
                    includeSource
                ));
                matchedCount = tables.Count;
                break;
            case "rows":
                items = rows.Select(row => TableRowItem(
                    row,
                    includeLayout,
                    includeSource
                ));
                matchedCount = rows.Count;
                break;
            case "cells":
                items = cells.Select(cell => TableCellItem(
                    cell,
                    includeLayout,
                    includeSource
                ));
                matchedCount = cells.Count;
                break;
            case "merges":
                items = merges.Select(merge => new
                {
                    merge_id = merge.Id,
                    table_id = merge.TableId,
                    root_cell_id = merge.RootCellId,
                    logical_column_start = merge.LogicalColumnStart,
                    logical_column_end = merge.LogicalColumnEnd,
                    grid_span = merge.GridSpan,
                    start_row_index = merge.StartRowIndex,
                    row_span = merge.RowSpan,
                    is_complete = merge.IsComplete,
                    cell_ids = merge.CellIds.Take(TableNestedIdLimit).ToArray(),
                    cell_ids_truncated = merge.CellIds.Count > TableNestedIdLimit,
                });
                matchedCount = merges.Count;
                break;
            case "issues":
                items = issues.Select(issue => TableIssueItem(issue, includeSource));
                matchedCount = issues.Count;
                break;
            default:
                throw new UnreachableException();
        }
        return new TableInspectionPage(
            items.Skip(offset).Take(maximum).ToArray(),
            matchedCount
        );
    }

    private static object TableItem(
        WordTableDefinition table,
        bool includeLayout,
        bool includeNames,
        bool includeSource
    ) => new
    {
        table_id = table.Id,
        parent_table_id = table.ParentTableId,
        depth = table.Depth,
        story_kind = ToSnakeCase(table.StoryKind.ToString()),
        row_count = table.RowCount,
        cell_count = table.CellCount,
        declared_grid_column_count = table.DeclaredGridColumnCount,
        logical_column_count = table.LogicalColumnCount,
        row_ids = table.RowIds.Take(TableNestedIdLimit).ToArray(),
        row_ids_truncated = table.RowIds.Count > TableNestedIdLimit,
        nested_table_ids = table.NestedTableIds.Take(TableNestedIdLimit).ToArray(),
        nested_table_ids_truncated = table.NestedTableIds.Count > TableNestedIdLimit,
        visual_continuation_group_id = table.VisualContinuationGroupId,
        floating_declared = table.FloatingPosition.Declared,
        floating_effective_in_word = table.FloatingPosition.IsEffectiveInWord,
        floating_ignored_reason = table.FloatingPosition.IgnoredReason,
        style_id = includeNames ? BoundForResponse(table.StyleId, 512) : null,
        caption = includeNames ? BoundForResponse(table.Caption, 512) : null,
        description = includeNames ? BoundForResponse(table.Description, 512) : null,
        width = includeLayout ? TableWidthItem(table.Width) : null,
        layout = includeLayout ? ToSnakeCase(table.Layout.ToString()) : null,
        justification = includeLayout
            ? ToSnakeCase(table.Justification.ToString())
            : null,
        indent = includeLayout ? TableWidthItem(table.Indent) : null,
        cell_spacing = includeLayout ? TableWidthItem(table.CellSpacing) : null,
        bidirectional_visual = includeLayout ? table.BidirectionalVisual : (bool?)null,
        look_mask = includeLayout ? BoundForResponse(table.LookMask, 64) : null,
        grid_columns = includeLayout
            ? table.GridColumns.Take(TableGridWidthLimit).Select(TableWidthItem).ToArray()
            : null,
        grid_columns_truncated = includeLayout
            ? table.GridColumns.Count > TableGridWidthLimit
            : (bool?)null,
        floating_position = includeLayout
            ? FloatingTablePositionItem(table.FloatingPosition)
            : null,
        semantic_node_id = includeSource ? table.SemanticNodeId.Value : null,
        part_uri = includeSource ? BoundForResponse(table.PartUri, 512) : null,
        source_element_ordinal = includeSource
            ? table.SourceElementOrdinal
            : (int?)null,
    };

    private static object TableRowItem(
        WordTableRowDefinition row,
        bool includeLayout,
        bool includeSource
    ) => new
    {
        row_id = row.Id,
        table_id = row.TableId,
        row_index = row.RowIndex,
        grid_before = row.GridBefore,
        grid_after = row.GridAfter,
        logical_column_count = row.LogicalColumnCount,
        header_declared = row.HeaderDeclared,
        header_effective = row.HeaderEffective,
        cell_ids = row.CellIds.Take(TableNestedIdLimit).ToArray(),
        cell_ids_truncated = row.CellIds.Count > TableNestedIdLimit,
        cannot_split = includeLayout ? row.CannotSplit : (bool?)null,
        hidden = includeLayout ? row.Hidden : (bool?)null,
        height_twips = includeLayout ? row.HeightTwips : null,
        height_rule = includeLayout
            ? ToSnakeCase(row.HeightRule.ToString())
            : null,
        property_overrides = includeLayout
            ? new
            {
                declared = row.PropertyOverrides.Declared,
                width = TableWidthItem(row.PropertyOverrides.Width),
                justification = ToSnakeCase(
                    row.PropertyOverrides.Justification.ToString()
                ),
                cell_spacing = TableWidthItem(row.PropertyOverrides.CellSpacing),
                property_count = row.PropertyOverrides.PropertyCount,
            }
            : null,
        semantic_node_id = includeSource ? row.SemanticNodeId.Value : null,
        part_uri = includeSource ? BoundForResponse(row.PartUri, 512) : null,
        source_element_ordinal = includeSource
            ? row.SourceElementOrdinal
            : (int?)null,
    };

    private static object TableCellItem(
        WordTableCellDefinition cell,
        bool includeLayout,
        bool includeSource
    ) => new
    {
        cell_id = cell.Id,
        table_id = cell.TableId,
        row_id = cell.RowId,
        physical_cell_index = cell.PhysicalCellIndex,
        logical_column_start = cell.LogicalColumnStart,
        logical_column_end = cell.LogicalColumnEnd,
        grid_span = cell.GridSpan,
        vertical_merge = ToSnakeCase(cell.VerticalMerge.ToString()),
        legacy_horizontal_merge = ToSnakeCase(
            cell.LegacyHorizontalMerge.ToString()
        ),
        vertical_merge_id = cell.VerticalMergeId,
        vertical_merge_root_cell_id = cell.VerticalMergeRootCellId,
        nested_table_ids = cell.NestedTableIds.Take(TableNestedIdLimit).ToArray(),
        nested_table_ids_truncated = cell.NestedTableIds.Count > TableNestedIdLimit,
        width = includeLayout ? TableWidthItem(cell.Width) : null,
        vertical_alignment = includeLayout
            ? BoundForResponse(cell.VerticalAlignment, 64)
            : null,
        text_direction = includeLayout
            ? BoundForResponse(cell.TextDirection, 64)
            : null,
        no_wrap = includeLayout ? cell.NoWrap : (bool?)null,
        fit_text = includeLayout ? cell.FitText : (bool?)null,
        semantic_node_id = includeSource ? cell.SemanticNodeId.Value : null,
        part_uri = includeSource ? BoundForResponse(cell.PartUri, 512) : null,
        source_element_ordinal = includeSource
            ? cell.SourceElementOrdinal
            : (int?)null,
    };

    private static object TableWidthItem(WordTableWidth width) => new
    {
        kind = ToSnakeCase(width.Kind.ToString()),
        value = width.Value,
        percent = width.Percent,
        is_valid = width.IsValid,
    };

    private static object FloatingTablePositionItem(
        WordFloatingTablePosition position
    ) => new
    {
        declared = position.Declared,
        effective_in_word = position.IsEffectiveInWord,
        ignored_reason = position.IgnoredReason,
        horizontal_anchor = ToSnakeCase(position.HorizontalAnchor.ToString()),
        vertical_anchor = ToSnakeCase(position.VerticalAnchor.ToString()),
        horizontal_alignment = BoundForResponse(position.HorizontalAlignment, 64),
        vertical_alignment = BoundForResponse(position.VerticalAlignment, 64),
        horizontal_position_twips = position.HorizontalPositionTwips,
        vertical_position_twips = position.VerticalPositionTwips,
        left_distance_twips = position.LeftDistanceTwips,
        right_distance_twips = position.RightDistanceTwips,
        top_distance_twips = position.TopDistanceTwips,
        bottom_distance_twips = position.BottomDistanceTwips,
    };

    private static object TableIssueItem(
        WordTableIssue issue,
        bool includeSource
    ) => new
    {
        issue_id = issue.Id,
        code = issue.Code,
        severity = ToSnakeCase(issue.Severity.ToString()),
        message = BoundForResponse(issue.Message, 512),
        table_id = issue.TableId,
        row_id = issue.RowId,
        cell_id = issue.CellId,
        merge_id = issue.MergeId,
        part_uri = includeSource ? BoundForResponse(issue.PartUri, 512) : null,
        source_element_ordinal = includeSource
            ? issue.SourceElementOrdinal
            : null,
    };

    private sealed record TableInspectionPage(object[] Items, int MatchedCount);
}
